#!/bin/sh
set -eu

network_name=${TELEMETRY_NETWORK_NAME:-family-jobs-board-telemetry}
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec /bin/sh "$script_dir/ensure-private-network.sh" \
  "$network_name" application-telemetry Telemetry
