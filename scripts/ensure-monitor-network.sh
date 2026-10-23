#!/bin/sh
set -eu

network_name=${MONITOR_INGRESS_NETWORK_NAME:-family-jobs-board-monitor-ingress}
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec /bin/sh "$script_dir/ensure-private-network.sh" \
  "$network_name" monitor-ingress "Monitor ingress"
