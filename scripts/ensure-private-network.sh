#!/bin/sh
set -eu

network_name=${1:?network name is required}
network_purpose=${2:?network purpose is required}
network_label=${3:?network label is required}

case "$network_name" in
  '' | *[!A-Za-z0-9_.-]*)
    echo "Invalid $network_label network name: use only letters, digits, dot, underscore, and hyphen." >&2
    exit 1
    ;;
esac

if network_properties=$(docker network inspect \
  --format '{{.Internal}} {{.Driver}} {{.Scope}} {{index .Labels "com.family-jobs-board.purpose"}}' \
  "$network_name" 2>/dev/null); then
  expected_properties="true bridge local $network_purpose"
  if [ "$network_properties" != "$expected_properties" ]; then
    echo "Network $network_name exists but is not the expected private $network_label network ($network_properties)." >&2
    exit 1
  fi

  echo "$network_label network $network_name is present and private."
  exit 0
fi

docker network create \
  --driver bridge \
  --internal \
  --label "com.family-jobs-board.purpose=$network_purpose" \
  "$network_name" >/dev/null

echo "Created private $network_label network $network_name."
