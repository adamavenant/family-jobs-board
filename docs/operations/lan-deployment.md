# LAN deployment

This deployment keeps the database and API private to Docker's internal network. Only the PIN-protected web app is published to the LAN. The PIN boundary does not make the app safe to expose to the internet.

## Prepare the server

Install Docker Engine with the Compose plugin, clone the repository, and create a deployment environment file outside the checkout:

```sh
sudo install -d -m 700 /etc/family-jobs-board
sudo cp .env.example /etc/family-jobs-board/family-jobs-board.env
sudo sh -c 'password=$(openssl rand -hex 32); sed -i "s/^DATABASE_PASSWORD=.*/DATABASE_PASSWORD=$password/" /etc/family-jobs-board/family-jobs-board.env'
sudo sh -c 'secret=$(openssl rand -base64 32); sed -i "s|^AUTHENTICATION_PIN_PEPPER=.*|AUTHENTICATION_PIN_PEPPER=$secret|" /etc/family-jobs-board/family-jobs-board.env'
sudo sh -c 'secret=$(openssl rand -base64 32); sed -i "s|^AUTHENTICATION_JWT_SIGNING_KEY=.*|AUTHENTICATION_JWT_SIGNING_KEY=$secret|" /etc/family-jobs-board/family-jobs-board.env'
sudo chmod 600 /etc/family-jobs-board/family-jobs-board.env
```

For an existing deployment environment file, add the three new settings before
upgrading, then run the same two `openssl` commands above to fill the secret
values:

```sh
sudo sh -c 'grep -q "^AUTHENTICATION_PIN_PEPPER=" /etc/family-jobs-board/family-jobs-board.env || printf "\nAUTHENTICATION_PIN_PEPPER=\n" >> /etc/family-jobs-board/family-jobs-board.env'
sudo sh -c 'grep -q "^AUTHENTICATION_JWT_SIGNING_KEY=" /etc/family-jobs-board/family-jobs-board.env || printf "AUTHENTICATION_JWT_SIGNING_KEY=\n" >> /etc/family-jobs-board/family-jobs-board.env'
sudo sh -c 'grep -q "^APPLICATION_ORIGIN=" /etc/family-jobs-board/family-jobs-board.env || printf "APPLICATION_ORIGIN=https://dashboard.home.arpa\n" >> /etc/family-jobs-board/family-jobs-board.env'
sudo sed -i 's|^APPLICATION_ORIGIN=http://|APPLICATION_ORIGIN=https://|' /etc/family-jobs-board/family-jobs-board.env
```

Keep the generated authentication secrets stable across deployments. Losing the
PIN pepper requires every PIN to be set again; changing the JWT key signs out
active sessions. `APPLICATION_ORIGIN` must exactly match the HTTPS browser
origin. Production HTTPS is fixed to port 443; `WEB_PORT` changes only the HTTP
redirect listener.

The generated hexadecimal password is strong and safe to use inside the PostgreSQL connection string. Set `IMAGE_TAG` to the full commit SHA from a successful `main` build in GitHub Actions. This selects the matching API, migration, and web images published to GHCR. `APP_HOSTNAME` defaults to `dashboard.home.arpa`; create a matching local DNS record that points to the server before deployment. Leave the other values empty to bind Caddy to every LAN interface on HTTP port 80 and HTTPS port 443 and use the `Africa/Johannesburg` household time zone. Set `APP_HOSTNAME`, `WEB_BIND_ADDRESS`, `WEB_PORT`, or `HOUSEHOLD_TIME_ZONE` in the same file to override those defaults. Port 443 is intentionally fixed; free it before deploying rather than assuming `WEB_PORT` changes HTTPS.

Caddy owns the server's LAN HTTP and HTTPS ports and routes requests for `APP_HOSTNAME` to the private web container. This leaves the web container, API, and database unpublished so additional applications can share the entry points through hostname-based routes later.

Anyone with Docker administrator access on the server can inspect container environment variables, including the database password. Restrict Docker access and the environment file to trusted administrators.

The container packages must be public, or Docker must be logged in to GHCR with a token that has `read:packages` permission. If authentication is required, run `docker login ghcr.io --username <github-username>` and enter the token at the prompt. Do not put the token in the repository or deployment environment file.

## Start the app

Run these commands from the repository checkout:

```sh
docker compose \
  --env-file /etc/family-jobs-board/family-jobs-board.env \
  -f compose.yaml \
  -f compose.production.yaml \
  pull

docker compose \
  --env-file /etc/family-jobs-board/family-jobs-board.env \
  -f compose.yaml \
  -f compose.production.yaml \
  up --detach --wait --no-build
```

Export Caddy's root certificate after the first start:

```sh
docker compose \
  --env-file /etc/family-jobs-board/family-jobs-board.env \
  -f compose.yaml \
  -f compose.production.yaml \
  cp proxy:/data/caddy/pki/authorities/local/root.crt ./family-jobs-board-root.crt
```

Transfer that public certificate through a trusted channel and install it as a
trusted root on each LAN client. Never copy the private key from `caddy-data`.
Open `https://dashboard.home.arpa/`, or the hostname configured in
`APP_HOSTNAME`, only after the client trusts the root. Do not configure router
port forwarding for this service.

Check service state and the web health endpoint:

```sh
docker compose \
  --env-file /etc/family-jobs-board/family-jobs-board.env \
  -f compose.yaml \
  -f compose.production.yaml \
  ps

curl --fail --cacert ./family-jobs-board-root.crt \
  --resolve dashboard.home.arpa:443:127.0.0.1 \
  https://dashboard.home.arpa/health
```

If `APP_HOSTNAME` differs from the default, use it in both `--resolve` and the
URL. A separate HTTP redirect check may use `WEB_PORT`, but the user-facing
HTTPS check always uses port 443.

## Deploy or upgrade

Fetch the desired revision so the server has its Compose files, then set `IMAGE_TAG` in the deployment environment file to that revision's full commit SHA. The GitHub Actions run for that commit on `main` must have completed successfully. Rerun both start commands: Compose pulls the three matching images, runs forward-only migrations, and replaces changed containers without building on the server. PostgreSQL data remains in the named `postgres-data` volume.

Back up both `postgres-data` and `caddy-data` before server maintenance or
significant upgrades. The latter contains the internal CA key; losing it makes
Caddy create a new root that every client must trust again. Stopping or
replacing containers does not remove either volume. Never use `down --volumes`
as an ordinary rollback or upgrade step.

For example, archive the CA volume into a new root-only backup directory while
the proxy is stopped:

```sh
backup_dir=/srv/backups/family-jobs-board/2026-10-23T120000Z
sudo install -d -m 700 "$backup_dir"
docker run --rm --network none --read-only --cap-drop ALL \
  --cap-add DAC_READ_SEARCH \
  -v family-jobs-board_caddy-data:/source:ro \
  -v "$backup_dir":/backup \
  busybox:1.37.0-uclibc \
  tar czf /backup/caddy-data.tgz -C /source .
```

The archive contains the CA private key. Keep it encrypted and accessible only
to trusted administrators, alongside the separately protected PostgreSQL
backup.

### Automatic deployment after image publication

The `main` CI workflow dispatches `deploy-family-dashboard.yml` in
`adamavenant/serendipity-deploy` only after the API, migration, and web image
publication matrix succeeds. It passes the exact 40-character `github.sha`, so
Serendipity deploys one matching image set. Pull-request workflows never run
the deployment job. The deployment repository's scheduled and manual triggers
remain available as fallbacks.

Create a fine-grained personal access token for the `adamavenant` resource
owner with access only to the `serendipity-deploy` repository. Grant repository
permission **Actions: Read and write**; no Contents write or package permission
is required. Record the token's expiry so it can be replaced before automatic
deployments begin failing.

In `adamavenant/family-jobs-board`, create an Actions environment named
`serendipity-production`, restrict its deployment branches to the selected
branch `main`, and store the token there as an environment secret named
`SERENDIPITY_DEPLOY_TOKEN`. Environment scoping keeps the credential unavailable
to pull-request jobs and branches that are not permitted to deploy.

The token can be stored through GitHub's repository settings under **Secrets
and variables → Actions**, or from an authenticated owner terminal:

```sh
gh secret set SERENDIPITY_DEPLOY_TOKEN \
  --repo adamavenant/family-jobs-board \
  --env serendipity-production
```

Paste the token only at the secure prompt. Never put it in a command argument,
workflow file, deployment environment file, issue, or pull request.

## Roll back the application

Choose a previously deployed, known-good commit whose images are still available in GHCR. Set `IMAGE_TAG` in the deployment environment file to that full commit SHA, then rerun both start commands. This rolls the API, migration runner, and web app back together.

Do not downgrade the database or restore an older database volume as part of an application rollback. Migrations are forward-only: the rollback deployment leaves the current schema and data in place. Only roll back to an application version that is compatible with the current schema. If it is not compatible, deploy a corrective forward change instead.

## Reset all data

The following operation is destructive. It stops the app and permanently removes the PostgreSQL volume and all household data:

```sh
docker compose \
  --env-file /etc/family-jobs-board/family-jobs-board.env \
  -f compose.yaml \
  -f compose.production.yaml \
  down --volumes
```

Only run it when a complete data and local-CA reset is intentional. It removes
both household data and Caddy's root CA, so every client must trust the newly
generated root after the app starts again.
