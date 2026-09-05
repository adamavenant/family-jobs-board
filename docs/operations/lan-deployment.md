# LAN deployment

This deployment keeps the database and API private to Docker's internal network. Only the web app is published to the LAN. It does not add authentication or make the app safe to expose to the internet.

## Prepare the server

Install Docker Engine with the Compose plugin, clone the repository, and create a deployment environment file outside the checkout:

```sh
sudo install -d -m 700 /etc/family-jobs-board
sudo cp .env.example /etc/family-jobs-board/family-jobs-board.env
sudo sh -c 'password=$(openssl rand -hex 32); sed -i "s/^DATABASE_PASSWORD=.*/DATABASE_PASSWORD=$password/" /etc/family-jobs-board/family-jobs-board.env'
sudo chmod 600 /etc/family-jobs-board/family-jobs-board.env
```

The generated hexadecimal password is strong and safe to use inside the PostgreSQL connection string. Set `IMAGE_TAG` to the full commit SHA from a successful `main` build in GitHub Actions. This selects the matching API, migration, and web images published to GHCR. `APP_HOSTNAME` defaults to `dashboard.home.arpa`; create a matching local DNS record that points to the server before deployment. Leave the other values empty to bind the Caddy entry point to every LAN interface on port 80 and use the `Africa/Johannesburg` household time zone. Set `APP_HOSTNAME`, `WEB_BIND_ADDRESS`, `WEB_PORT`, or `HOUSEHOLD_TIME_ZONE` in the same file to override those defaults.

Caddy owns the server's LAN HTTP port and routes requests for `APP_HOSTNAME` to the private web container. This leaves the web container, API, and database unpublished so additional applications can share port 80 through hostname-based routes later.

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

Open `http://dashboard.home.arpa/` from another device on the LAN, or use the hostname configured in `APP_HOSTNAME`. Do not configure router port forwarding for this service.

Check service state and the web health endpoint:

```sh
docker compose \
  --env-file /etc/family-jobs-board/family-jobs-board.env \
  -f compose.yaml \
  -f compose.production.yaml \
  ps

curl --fail --header "Host: dashboard.home.arpa" http://127.0.0.1/health
```

If `APP_HOSTNAME` differs from the default, use that value in the `Host` header. If `WEB_PORT` is not 80, include it in the health URL.

## Deploy or upgrade

Fetch the desired revision so the server has its Compose files, then set `IMAGE_TAG` in the deployment environment file to that revision's full commit SHA. The GitHub Actions run for that commit on `main` must have completed successfully. Rerun both start commands: Compose pulls the three matching images, runs forward-only migrations, and replaces changed containers without building on the server. PostgreSQL data remains in the named `postgres-data` volume.

Back up that Docker volume before server maintenance or significant upgrades. Stopping or replacing containers does not remove it.

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

Only run it when a complete data reset is intentional. Start the app again to create an empty database.
