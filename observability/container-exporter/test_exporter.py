import json
import unittest
import urllib.parse
import urllib.request
from unittest.mock import patch

from exporter import (
    ALLOWED_SERVICES,
    Collector,
    DockerApi,
    Handler,
    _image_id,
    _image_tag,
)


def inspection(
    service: str,
    *,
    status: str = "running",
    health: str = "healthy",
    restarting: bool = False,
    oom_killed: bool = False,
    restart_count: int = 0,
) -> dict:
    return {
        "Image": "sha256:" + "f" * 64,
        "Config": {
            "Image": f"ghcr.io/adamavenant/family-jobs-board-{service}:" + "a" * 40,
            "Labels": {
                "com.docker.compose.project": "family-jobs-board",
                "com.docker.compose.service": service,
            },
        },
        "RestartCount": restart_count,
        "State": {
            "Status": status,
            "Running": status == "running",
            "Restarting": restarting,
            "OOMKilled": oom_killed,
            "StartedAt": "2026-10-14T18:00:00Z",
            "Health": {"Status": health},
        },
    }


class FakeDocker:
    def __init__(self, fixtures, storage_usage=None):
        self.fixtures = fixtures
        self.storage_usage = storage_usage or {
            "prometheus": 1024,
            "loki": 2048,
        }
        self.storage_calls = 0

    def inspect_service(self, service):
        return self.fixtures.get(service)

    def telemetry_storage_usage(self):
        self.storage_calls += 1
        return self.storage_usage


class CollectorTests(unittest.TestCase):
    def test_deterministic_health_state_and_safety_fixtures(self):
        fixtures = {
            "api": inspection("api"),
            "db": inspection("db", status="running", health="unhealthy", restart_count=3),
            "proxy": inspection(
                "proxy",
                status="restarting",
                health="starting",
                restarting=True,
                restart_count=4,
            ),
            "web": inspection(
                "web", status="exited", health="none", oom_killed=True, restart_count=2
            ),
        }
        output = Collector(FakeDocker(fixtures), clock=lambda: 1_760_467_200.0).collect()

        self.assertIn('container_health{service="api",status="healthy"} 1', output)
        self.assertIn('container_health{service="db",status="unhealthy"} 0', output)
        self.assertIn('container_health{service="proxy",status="starting"} 0.5', output)
        self.assertIn('container_state{service="proxy",state="restarting"} 1', output)
        self.assertIn('container_restarting{service="proxy"} 1', output)
        self.assertIn('container_state{service="web",state="exited"} 1', output)
        self.assertIn('container_oom_killed{service="web"} 1', output)
        self.assertIn('container_restart_count{service="db"} 3', output)
        self.assertIn('telemetry_storage_used_bytes{store="prometheus"} 1024', output)
        self.assertIn('telemetry_storage_used_bytes{store="loki"} 2048', output)
        self.assertIn('container_collector_success 1', output)
        self.assertNotIn("container_name", output)
        self.assertNotIn("environment", output)

    def test_absent_service_is_not_reported_as_healthy(self):
        output = Collector(FakeDocker({}), clock=lambda: 100.0).collect()
        for service in ALLOWED_SERVICES:
            self.assertIn(f'container_present{{service="{service}"}} 0', output)
            self.assertIn(f'container_running{{service="{service}"}} 0', output)
        self.assertNotIn("container_health{", output)

    def test_storage_usage_is_cached_across_health_and_scrape_calls(self):
        docker = FakeDocker({service: inspection(service) for service in ALLOWED_SERVICES})
        clock = iter((100.0, 110.0, 120.0, 410.0))
        collector = Collector(docker, clock=lambda: next(clock))

        startup_health, startup_ok = collector.collect_with_status(
            refresh_storage=False
        )
        first, first_ok = collector.collect_with_status()
        health, health_ok = collector.collect_with_status(refresh_storage=False)
        refreshed, refreshed_ok = collector.collect_with_status()

        self.assertTrue(startup_ok and first_ok and health_ok and refreshed_ok)
        self.assertEqual(docker.storage_calls, 2)
        self.assertIn("telemetry_storage_collector_success 0", startup_health)
        self.assertNotIn("telemetry_storage_used_bytes{", startup_health)
        self.assertIn("telemetry_storage_collector_success 1", first)
        self.assertIn("telemetry_storage_collector_success 1", health)
        self.assertIn(
            "telemetry_storage_collector_last_success_timestamp_seconds 410.000",
            refreshed,
        )

    def test_failed_storage_refresh_is_rate_limited_and_keeps_last_good_values(self):
        class StorageFailsAfterFirst(FakeDocker):
            def telemetry_storage_usage(self):
                self.storage_calls += 1
                if self.storage_calls > 1:
                    raise OSError("disk usage unavailable")
                return self.storage_usage

        docker = StorageFailsAfterFirst(
            {service: inspection(service) for service in ALLOWED_SERVICES}
        )
        clock = iter((100.0, 400.0, 430.0))
        collector = Collector(docker, clock=lambda: next(clock))

        initial = collector.collect()
        failed_refresh = collector.collect()
        bounded_retry = collector.collect()

        self.assertEqual(docker.storage_calls, 2)
        self.assertIn("telemetry_storage_collector_success 1", initial)
        self.assertIn("telemetry_storage_collector_success 0", failed_refresh)
        self.assertIn("telemetry_storage_collector_success 0", bounded_retry)
        self.assertIn(
            'telemetry_storage_used_bytes{store="prometheus"} 1024',
            failed_refresh,
        )
        self.assertIn(
            "telemetry_storage_collector_last_success_timestamp_seconds 100.000",
            bounded_retry,
        )

    def test_initial_storage_failure_keeps_container_collection_available(self):
        class StorageAlwaysFails(FakeDocker):
            def telemetry_storage_usage(self):
                self.storage_calls += 1
                raise OSError("disk usage unavailable")

        docker = StorageAlwaysFails(
            {service: inspection(service) for service in ALLOWED_SERVICES}
        )
        output, succeeded = Collector(docker, clock=lambda: 100.0).collect_with_status()

        self.assertTrue(succeeded)
        self.assertEqual(docker.storage_calls, 1)
        self.assertIn("container_collector_success 1", output)
        self.assertIn("telemetry_storage_collector_success 0", output)
        self.assertNotIn("telemetry_storage_used_bytes{", output)
        for service in ALLOWED_SERVICES:
            self.assertIn(f'container_present{{service="{service}"}} 1', output)

    def test_failed_collection_preserves_last_success_for_staleness(self):
        class FailsAfterFirst:
            remaining = len(ALLOWED_SERVICES)

            def inspect_service(self, service):
                if self.remaining == 0:
                    raise OSError("collector unavailable")
                self.remaining -= 1
                return inspection(service)

            def telemetry_storage_usage(self):
                return {"prometheus": 1024, "loki": 2048}

        clock = iter((100.0, 160.0))
        collector = Collector(FailsAfterFirst(), clock=lambda: next(clock))
        self.assertIn("container_collector_success 1", collector.collect())
        failure = collector.collect()
        self.assertIn("container_collector_success 0", failure)
        self.assertIn("container_collector_last_success_timestamp_seconds 100.000", failure)

    def test_collection_status_fails_closed_before_first_success(self):
        class AlwaysFails:
            def inspect_service(self, service):
                raise OSError("socket unavailable")

            def telemetry_storage_usage(self):
                return {}

        output, succeeded = Collector(AlwaysFails()).collect_with_status()

        self.assertFalse(succeeded)
        self.assertIn("container_collector_success 0", output)
        self.assertFalse(
            any(
                line.startswith(
                    "family_jobs_board_container_collector_last_success_timestamp_seconds "
                )
                for line in output.splitlines()
            )
        )

    def test_health_endpoint_fails_closed_when_collection_fails(self):
        class AlwaysFails:
            def inspect_service(self, service):
                raise OSError("socket unavailable")

            def telemetry_storage_usage(self):
                return {}

        handler = object.__new__(Handler)
        handler.path = "/health"
        handler.collector = Collector(AlwaysFails())
        responses = []
        handler._respond = lambda status, content_type, body: responses.append(
            (status, content_type, body)
        )

        handler.do_GET()

        self.assertEqual(
            responses, [(503, "text/plain; charset=utf-8", b"unhealthy\n")]
        )


class DockerApiTests(unittest.TestCase):
    @patch("urllib.request.urlopen")
    def test_queries_only_exact_project_and_service_labels(self, urlopen):
        container_id = "a" * 64
        listed = {
            "Id": container_id,
            "Created": 20,
            "Labels": {
                "com.docker.compose.project": "family-jobs-board",
                "com.docker.compose.service": "api",
            },
        }
        unrelated = {
            "Id": "b" * 64,
            "Created": 99,
            "Labels": {
                "com.docker.compose.project": "other-project",
                "com.docker.compose.service": "api",
            },
        }
        responses = [
            unittest.mock.MagicMock(
                __enter__=lambda self: self,
                __exit__=lambda *_: None,
                read=lambda: json.dumps([listed, unrelated]).encode(),
            ),
            unittest.mock.MagicMock(
                __enter__=lambda self: self,
                __exit__=lambda *_: None,
                read=lambda: json.dumps(inspection("api")).encode(),
            ),
        ]
        urlopen.side_effect = responses

        result = DockerApi("http://proxy:2375", "family-jobs-board").inspect_service("api")

        self.assertIsNotNone(result)
        list_url = urlopen.call_args_list[0].args[0].full_url
        parsed = urllib.parse.urlparse(list_url)
        filters = json.loads(urllib.parse.parse_qs(parsed.query)["filters"][0])
        self.assertEqual(
            filters,
            {
                "label": [
                    "com.docker.compose.project=family-jobs-board",
                    "com.docker.compose.service=api",
                ]
            },
        )
        self.assertEqual(
            urlopen.call_args_list[1].args[0].full_url,
            f"http://proxy:2375/v1.42/containers/{container_id}/json",
        )

    def test_rejects_arbitrary_service(self):
        with self.assertRaises(ValueError):
            DockerApi("http://proxy:2375", "family-jobs-board").inspect_service("unrelated")

    def test_rejects_docker_api_path_outside_read_allowlist(self):
        docker = DockerApi("http://proxy:2375", "family-jobs-board")

        for path in (
            "/images/json",
            "/containers/" + "a" * 64 + "/logs",
            "/containers/json?all=true",
        ):
            with self.subTest(path=path), self.assertRaises(ValueError):
                docker._get_json(path)

    @patch("urllib.request.urlopen")
    def test_reports_only_exact_observability_volume_usage(self, urlopen):
        response = {
            "Volumes": [
                {"Name": "obs-prometheus-data", "UsageData": {"Size": 1024}},
                {"Name": "obs-loki-data", "UsageData": {"Size": 2048}},
                {"Name": "other-private-data", "UsageData": {"Size": 999999}},
            ]
        }
        urlopen.return_value = unittest.mock.MagicMock(
            __enter__=lambda self: self,
            __exit__=lambda *_: None,
            read=lambda: json.dumps(response).encode(),
        )

        usage = DockerApi(
            "http://proxy:2375", "family-jobs-board", "obs"
        ).telemetry_storage_usage()

        self.assertEqual(usage, {"prometheus": 1024, "loki": 2048})
        self.assertEqual(
            urlopen.call_args.args[0].full_url,
            "http://proxy:2375/v1.42/system/df?type=volume",
        )

    @patch("urllib.request.urlopen")
    def test_rejects_missing_or_invalid_observability_volume_usage(self, urlopen):
        invalid_responses = (
            {"Volumes": []},
            {
                "Volumes": [
                    {"Name": "obs-prometheus-data", "UsageData": {"Size": 1024}}
                ]
            },
            {
                "Volumes": [
                    {"Name": "obs-prometheus-data", "UsageData": {"Size": 1024}},
                    {"Name": "obs-loki-data", "UsageData": {"Size": -1}},
                ]
            },
        )

        for response in invalid_responses:
            urlopen.return_value = unittest.mock.MagicMock(
                __enter__=lambda self: self,
                __exit__=lambda *_: None,
                read=lambda response=response: json.dumps(response).encode(),
            )
            with self.subTest(response=response), self.assertRaises(ValueError):
                DockerApi(
                    "http://proxy:2375", "family-jobs-board", "obs"
                ).telemetry_storage_usage()

    def test_image_label_is_reduced_to_bounded_tag(self):
        self.assertEqual(_image_tag("registry.example/app:" + "a" * 40), "a" * 40)
        self.assertEqual(_image_tag("registry.example/app:private/path"), "unknown")
        self.assertEqual(_image_id("sha256:" + "a" * 64), "sha256:" + "a" * 64)
        self.assertEqual(_image_id("registry.example/app:mutable"), "unknown")


if __name__ == "__main__":
    unittest.main()
