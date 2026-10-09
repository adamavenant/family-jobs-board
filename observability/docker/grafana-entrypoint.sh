#!/bin/sh
set -eu

secret_source=/run/secrets/grafana-admin-password
secret_target=/tmp/grafana-admin-password

if [ ! -s "$secret_source" ]; then
  echo "Grafana operator-password secret is missing or empty." >&2
  exit 1
fi

umask 077
cp "$secret_source" "$secret_target"
chmod 0400 "$secret_target"
chown 472:0 "$secret_target"

# Local Compose file secrets are bind mounts, so a host mode-0600 secret may
# not be readable by Grafana's uid. Root exists only for this copy step; the
# long-running server is always replaced by the unprivileged image user.
exec su -p grafana -s /bin/sh -c 'exec /run.sh'
