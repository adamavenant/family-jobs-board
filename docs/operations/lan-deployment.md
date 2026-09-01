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

The generated hexadecimal password is strong and safe to use inside the PostgreSQL connection string. Leave the other values empty to bind the web app to every LAN interface on port 80 and use the `Africa/Johannesburg` household time zone. Set `WEB_BIND_ADDRESS`, `WEB_PORT`, or `HOUSEHOLD_TIME_ZONE` in the same file to override those defaults.

Anyone with Docker administrator access on the server can inspect container environment variables, including the database password. Restrict Docker access and the environment file to trusted administrators.

## Start the app

Run these commands from the repository checkout:

```sh
docker compose \
  --env-file /etc/family-jobs-board/family-jobs-board.env \
  -f compose.yaml \
  -f compose.production.yaml \
  up --build --detach --wait
```

Open `http://<server-address>/` from another device on the LAN. Do not configure router port forwarding for this service.

Check service state and the web health endpoint:

```sh
docker compose \
  --env-file /etc/family-jobs-board/family-jobs-board.env \
  -f compose.yaml \
  -f compose.production.yaml \
  ps

curl --fail http://127.0.0.1/health
```

If `WEB_PORT` is not 80, include it in the health URL.

## Upgrade

Pull the desired revision, then rerun the start command. Compose rebuilds the images, runs migrations, and replaces changed containers. PostgreSQL data remains in the named `postgres-data` volume.

Back up that Docker volume before server maintenance or significant upgrades. Stopping or replacing containers does not remove it.

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
