#!/usr/bin/env python3
"""Privacy-bounded Prometheus exporter for Family Jobs Board containers."""

from __future__ import annotations

import datetime as dt
import http.client
import json
import os
import re
import socket
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any


ALLOWED_SERVICES = ("api", "db", "proxy", "web")
VALID_PROJECT = re.compile(r"^[a-zA-Z0-9_.-]{1,63}$")
VALID_CONTAINER_ID = re.compile(r"^[a-f0-9]{64}$")
VALID_IMAGE_TAG = re.compile(r"^[a-zA-Z0-9_.-]{1,128}$")
VALID_IMAGE_ID = re.compile(r"^sha256:[a-f0-9]{64}$")
DOCKER_API_VERSION = "v1.41"


def _metric_help(name: str, description: str, metric_type: str = "gauge") -> list[str]:
    return [f"# HELP {name} {description}", f"# TYPE {name} {metric_type}"]


def _escape_label(value: str) -> str:
    return value.replace("\\", "\\\\").replace("\n", "\\n").replace('"', '\\"')


def _parse_timestamp(value: object) -> float | None:
    if not isinstance(value, str) or value.startswith("0001-01-01"):
        return None
    try:
        return dt.datetime.fromisoformat(value.replace("Z", "+00:00")).timestamp()
    except ValueError:
        return None


def _image_tag(image: object) -> str:
    if not isinstance(image, str):
        return "unknown"
    if "@sha256:" in image:
        candidate = image.rsplit("@sha256:", 1)[1]
    elif ":" in image.rsplit("/", 1)[-1]:
        candidate = image.rsplit(":", 1)[1]
    else:
        candidate = "unknown"
    return candidate if VALID_IMAGE_TAG.fullmatch(candidate) else "unknown"


def _image_id(image: object) -> str:
    return image if isinstance(image, str) and VALID_IMAGE_ID.fullmatch(image) else "unknown"


class UnixSocketHTTPConnection(http.client.HTTPConnection):
    def __init__(self, socket_path: str, timeout_seconds: float):
        super().__init__("localhost", timeout=timeout_seconds)
        self._socket_path = socket_path

    def connect(self) -> None:
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout)
        self.sock.connect(self._socket_path)


class DockerApi:
    def __init__(self, base_url: str, project: str, timeout_seconds: float = 3.0):
        if not VALID_PROJECT.fullmatch(project):
            raise ValueError("FJB_COMPOSE_PROJECT_NAME has an invalid value")
        self._base_url = base_url.rstrip("/")
        self._project = project
        self._timeout_seconds = timeout_seconds

    def _get_json(self, path: str) -> Any:
        parsed_path = urllib.parse.urlsplit(path)
        query_values = urllib.parse.parse_qs(parsed_path.query)
        is_list = (
            parsed_path.path == "/containers/json"
            and set(query_values) == {"all", "filters"}
            and query_values["all"] == ["true"]
            and len(query_values["filters"]) == 1
        )
        is_inspect = re.fullmatch(
            r"/containers/[a-f0-9]{64}/json", parsed_path.path
        ) and not parsed_path.query
        if not is_list and not is_inspect:
            raise ValueError("Docker API path is outside the fixed read allowlist")

        request_path = f"/{DOCKER_API_VERSION}{path}"
        if self._base_url.startswith("unix://"):
            connection = UnixSocketHTTPConnection(
                self._base_url.removeprefix("unix://"), self._timeout_seconds
            )
            try:
                connection.request("GET", request_path)
                response = connection.getresponse()
                if response.status != 200:
                    raise OSError(f"Docker API returned HTTP {response.status}")
                return json.loads(response.read())
            finally:
                connection.close()

        request = urllib.request.Request(
            f"{self._base_url}{request_path}", method="GET"
        )
        with urllib.request.urlopen(request, timeout=self._timeout_seconds) as response:
            return json.load(response)

    def inspect_service(self, service: str) -> dict[str, Any] | None:
        if service not in ALLOWED_SERVICES:
            raise ValueError("service is outside the fixed allowlist")
        filters = json.dumps(
            {
                "label": [
                    f"com.docker.compose.project={self._project}",
                    f"com.docker.compose.service={service}",
                ]
            },
            separators=(",", ":"),
        )
        query = urllib.parse.urlencode({"all": "true", "filters": filters})
        containers = self._get_json(f"/containers/json?{query}")
        if not isinstance(containers, list) or not containers:
            return None
        candidates = [
            item
            for item in containers
            if isinstance(item, dict)
            and isinstance(item.get("Id"), str)
            and VALID_CONTAINER_ID.fullmatch(item["Id"])
            and item.get("Labels", {}).get("com.docker.compose.project") == self._project
            and item.get("Labels", {}).get("com.docker.compose.service") == service
        ]
        if not candidates:
            return None
        selected = max(candidates, key=lambda item: int(item.get("Created", 0)))
        inspected = self._get_json(f"/containers/{selected['Id']}/json")
        if not isinstance(inspected, dict):
            raise ValueError("Docker inspect response is not an object")
        labels = inspected.get("Config", {}).get("Labels", {})
        if (
            labels.get("com.docker.compose.project") != self._project
            or labels.get("com.docker.compose.service") != service
        ):
            raise ValueError("Docker inspect labels did not match the requested service")
        return inspected


class Collector:
    def __init__(self, docker: DockerApi, clock=time.time):
        self._docker = docker
        self._clock = clock
        self._lock = threading.Lock()
        self._last_success: float | None = None

    def collect_with_status(self) -> tuple[str, bool]:
        with self._lock:
            now = self._clock()
            try:
                inspections = {
                    service: self._docker.inspect_service(service)
                    for service in ALLOWED_SERVICES
                }
            except (OSError, ValueError, json.JSONDecodeError, urllib.error.URLError):
                return self._render_failure(), False
            self._last_success = now
            return self._render_success(inspections, now), True

    def collect(self) -> str:
        return self.collect_with_status()[0]

    def _collector_status_lines(
        self, succeeded: bool, last_success: float | None
    ) -> list[str]:
        lines = _metric_help(
            "family_jobs_board_container_collector_success",
            "Whether the most recent bounded Docker API collection succeeded.",
        )
        lines.append(
            f"family_jobs_board_container_collector_success {int(succeeded)}"
        )
        lines.extend(
            _metric_help(
                "family_jobs_board_container_collector_last_success_timestamp_seconds",
                "Unix timestamp of the most recent successful bounded Docker API collection.",
            )
        )
        if last_success is not None:
            lines.append(
                "family_jobs_board_container_collector_last_success_timestamp_seconds "
                f"{last_success:.3f}"
            )
        return lines

    def _render_failure(self) -> str:
        lines = self._collector_status_lines(False, self._last_success)
        return "\n".join(lines) + "\n"

    def _render_success(
        self, inspections: dict[str, dict[str, Any] | None], now: float
    ) -> str:
        definitions = (
            ("family_jobs_board_container_expected", "Whether this fixed service is expected."),
            ("family_jobs_board_container_present", "Whether a matching container is present."),
            ("family_jobs_board_container_running", "Whether the container is running."),
            ("family_jobs_board_container_restarting", "Whether the container is restarting."),
            ("family_jobs_board_container_oom_killed", "Whether the last container stop was OOM-killed."),
            ("family_jobs_board_container_restart_count", "Docker restart count for the container."),
            ("family_jobs_board_container_started_timestamp_seconds", "Unix timestamp when the container last started."),
            ("family_jobs_board_container_age_seconds", "Seconds since the container last started."),
            ("family_jobs_board_container_state", "Current Docker state for the container."),
            ("family_jobs_board_container_health", "Current Docker health-check status for the container."),
            ("family_jobs_board_container_info", "Bounded deployment tag and immutable image content ID for the container."),
        )
        lines: list[str] = []
        for name, description in definitions:
            lines.extend(_metric_help(name, description))

        for service, inspected in inspections.items():
            label = f'service="{service}"'
            lines.append(f"family_jobs_board_container_expected{{{label}}} 1")
            lines.append(
                f"family_jobs_board_container_present{{{label}}} {1 if inspected else 0}"
            )
            if inspected is None:
                lines.append(f"family_jobs_board_container_running{{{label}}} 0")
                lines.append(f"family_jobs_board_container_restarting{{{label}}} 0")
                lines.append(f"family_jobs_board_container_oom_killed{{{label}}} 0")
                continue

            state = inspected.get("State", {})
            status = state.get("Status", "unknown")
            if status not in {
                "created",
                "running",
                "paused",
                "restarting",
                "removing",
                "exited",
                "dead",
            }:
                status = "unknown"
            running = bool(state.get("Running"))
            restarting = bool(state.get("Restarting"))
            oom_killed = bool(state.get("OOMKilled"))
            restart_count = inspected.get("RestartCount", 0)
            if not isinstance(restart_count, int) or restart_count < 0:
                restart_count = 0
            health = (state.get("Health") or {}).get("Status", "none")
            if health not in {"healthy", "unhealthy", "starting", "none"}:
                health = "unknown"
            started_at = _parse_timestamp(state.get("StartedAt"))
            image_tag = _escape_label(_image_tag(inspected.get("Config", {}).get("Image")))
            image_id = _escape_label(_image_id(inspected.get("Image")))

            lines.append(f"family_jobs_board_container_running{{{label}}} {int(running)}")
            lines.append(
                f"family_jobs_board_container_restarting{{{label}}} {int(restarting)}"
            )
            lines.append(
                f"family_jobs_board_container_oom_killed{{{label}}} {int(oom_killed)}"
            )
            lines.append(
                f"family_jobs_board_container_restart_count{{{label}}} {restart_count}"
            )
            lines.append(
                f'family_jobs_board_container_state{{{label},state="{status}"}} 1'
            )
            lines.append(
                f'family_jobs_board_container_health{{{label},status="{health}"}} '
                f'{1 if health == "healthy" else 0.5 if health == "starting" else 0}'
            )
            lines.append(
                f'family_jobs_board_container_info{{{label},image_tag="{image_tag}",'
                f'image_id="{image_id}"}} 1'
            )
            if started_at is not None:
                lines.append(
                    f"family_jobs_board_container_started_timestamp_seconds{{{label}}} "
                    f"{started_at:.3f}"
                )
                lines.append(
                    f"family_jobs_board_container_age_seconds{{{label}}} "
                    f"{max(0.0, now - started_at):.3f}"
                )

        lines.extend(self._collector_status_lines(True, now))
        return "\n".join(lines) + "\n"


class Handler(BaseHTTPRequestHandler):
    collector: Collector

    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/health":
            _metrics, succeeded = self.collector.collect_with_status()
            status = 200 if succeeded else 503
            body = b"healthy\n" if succeeded else b"unhealthy\n"
            self._respond(status, "text/plain; charset=utf-8", body)
            return
        if self.path == "/metrics":
            self._respond(
                200,
                "text/plain; version=0.0.4; charset=utf-8",
                self.collector.collect().encode(),
            )
            return
        self._respond(404, "text/plain; charset=utf-8", b"not found\n")

    def log_message(self, _format: str, *_args: object) -> None:
        return

    def _respond(self, status: int, content_type: str, body: bytes) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def main() -> None:
    project = os.environ.get("FJB_COMPOSE_PROJECT_NAME", "family-jobs-board")
    docker_url = os.environ.get("DOCKER_API_URL", "unix:///var/run/docker.sock")
    port = int(os.environ.get("PORT", "9400"))
    Handler.collector = Collector(DockerApi(docker_url, project))
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()


if __name__ == "__main__":
    main()
