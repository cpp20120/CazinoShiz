#!/usr/bin/env bash
set -euo pipefail

# Disposable baseline/post-migration profile for the single-player wager batch.
# The host owns a fresh PostgreSQL Testcontainer and is stopped on exit.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
port="${WAGER_BATCH_PROFILE_PORT:-18125}"
duration="${WAGER_BATCH_PROFILE_DURATION:-8s}"
users="${WAGER_BATCH_PROFILE_USERS:-10000}"
user_base="${WAGER_BATCH_PROFILE_USER_BASE:-9100000}"
log_file="$(mktemp /tmp/casinoshiz-wager-batch.XXXXXX.log)"

cleanup() {
  if [[ -n "${host_pid:-}" ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill "$host_pid" 2>/dev/null || true
    wait "$host_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

(
  cd "$repo_root"
  env DOCKER_HOST=unix:///var/run/docker.sock FULL_REST_WRITE_LOAD_PORT="$port" \
    FULL_REST_WRITE_LOAD_SEED_USER_BASE="$user_base" FULL_REST_WRITE_LOAD_SEED_USER_COUNT="$users" \
    FULL_REST_WRITE_LOAD_SEED_COINS=1000000 \
    dotnet run --project tests/CasinoShiz.FullRestWriteLoadTest -c Release --no-build
) >"$log_file" 2>&1 &
host_pid=$!

for _ in {1..120}; do
  if grep -q "FULL_REST_WRITE_LOAD_READY" "$log_file"; then
    break
  fi
  if ! kill -0 "$host_pid" 2>/dev/null; then
    sed -n '1,200p' "$log_file" >&2
    exit 1
  fi
  sleep 0.25
done

if ! grep -q "FULL_REST_WRITE_LOAD_READY" "$log_file"; then
  sed -n '1,200p' "$log_file" >&2
  exit 1
fi

run_case() {
  local name="$1"
  local path="$2"
  local body="$3"

  echo "=== $name ==="
  env REST_DEV_TOKEN=full-rest-write-load-test \
    REST_TENANT=write-load \
    WRITE_PATH="$path" \
    WRITE_BODY="$body" \
    LOAD_USER_BASE="$user_base" \
    LOAD_USER_COUNT="$users" \
    wrk -t2 -c8 -d"$duration" --latency \
      -s "$repo_root/eng/profile-scripts/write/atomic-play.lua" \
      "http://127.0.0.1:$port"
}

run_case dice /dice/roll '{"slotValue":1}'
run_case dicecube /dicecube/play '{"amount":10}'
run_case darts /darts/play '{"amount":10}'
run_case football /football/play '{"amount":10}'
run_case basketball /basketball/play '{"amount":10}'
run_case bowling /bowling/play '{"amount":10}'
run_case horse /horse/bet '{"horseId":1,"amount":10}'

echo "=== host diagnostics ==="
grep -E '(^fail:|^warn:|Unhandled|Exception)' "$log_file" | sed -n '1,80p' || true
echo "diagnostic_log=$log_file"
