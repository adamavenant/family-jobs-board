#!/bin/sh
set -eu

network_name=${TELEMETRY_NETWORK_NAME:-family-jobs-board-telemetry}

case "$network_name" in
  '' | *[!A-Za-z0-9_.-]*)
    echo "Invalid TELEMETRY_NETWORK_NAME: use only letters, digits, dot, underscore, and hyphen." >&2
    exit 1
    ;;
esac

if network_properties=$(docker network inspect \
  --format '{{.Internal}} {{.Driver}} {{.Scope}}' \
  "$network_name" 2>/dev/null); then
  if [ "$network_properties" != "true bridge local" ]; then
    echo "Network $network_name exists but is not an internal, local bridge network ($network_properties)." >&2
    exit 1
  fi

  echo "Telemetry network $network_name is present and private."
  exit 0
fi

docker network create \
  --driver bridge \
  --internal \
  --label com.family-jobs-board.purpose=application-telemetry \
  "$network_name" >/dev/null

echo "Created private telemetry network $network_name."
