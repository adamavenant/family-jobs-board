#!/bin/sh
set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
api_port=${OPENAPI_PORT:-5099}
api_pid=""
contract_tmp=$(mktemp)

cleanup() {
  if [ -n "$api_pid" ]; then
    kill "$api_pid" 2>/dev/null || true
    wait "$api_pid" 2>/dev/null || true
  fi
  rm -f "$contract_tmp"
}
trap cleanup EXIT INT TERM

cd "$root_dir"
dotnet build src/backend/FamilyJobsBoard.Api/FamilyJobsBoard.Api.csproj --configuration Release
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --no-build --configuration Release \
  --project src/backend/FamilyJobsBoard.Api/FamilyJobsBoard.Api.csproj \
  --urls "http://127.0.0.1:$api_port" >/dev/null 2>&1 &
api_pid=$!

attempt=0
until curl --fail --silent "http://127.0.0.1:$api_port/openapi/v1.json" >"$contract_tmp"; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 40 ]; then
    echo "API did not expose its OpenAPI document." >&2
    exit 1
  fi
  sleep 0.25
done

mv "$contract_tmp" src/web/openapi.json
contract_tmp=$(mktemp)
cd src/web
npm run generate:api
