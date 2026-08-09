#!/usr/bin/env bash
set -euo pipefail

# Post-migration profile for the eventual wager path. Each request is a new
# bet and exercises reserve -> game command -> committed outcome -> settlement.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
port="${WAGER_SAGA_PROFILE_PORT:-18126}"
duration="${WAGER_SAGA_PROFILE_DURATION:-8s}"
users="${WAGER_SAGA_PROFILE_USERS:-10000}"
user_base="${WAGER_SAGA_PROFILE_USER_BASE:-9200000}"
log_file="$(mktemp /tmp/casinoshiz-wager-saga.XXXXXX.log)"

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
    sed -n '1,240p' "$log_file" >&2
    exit 1
  fi
  sleep 0.25
done

if ! grep -q "FULL_REST_WRITE_LOAD_READY" "$log_file"; then
  sed -n '1,240p' "$log_file" >&2
  exit 1
fi

run_case() {
  local name="$1"
  local game_id="$2"
  local payload="$3"
  local rules_version="$4"

  echo "=== $name ==="
  env REST_DEV_TOKEN=full-rest-write-load-test \
    REST_TENANT=write-load \
    WAGER_GAME_ID="$game_id" \
    WAGER_GAME_INPUT="$payload" \
    WAGER_RULES_VERSION="$rules_version" \
    WAGER_STAKE=10 \
    LOAD_USER_BASE="$user_base" \
    LOAD_USER_COUNT="$users" \
    wrk -t2 -c8 -d"$duration" --latency \
      -s "$repo_root/eng/profile-scripts/write/wager-play.lua" \
      "http://127.0.0.1:$port"
}

run_case dice dice '{"chatId":42,"displayName":"load","action":"play","payload":"{\"diceValue\":1}"}' dice.v1
run_case dicecube dicecube '{"chatId":42,"displayName":"load","action":"play","payload":"{\"face\":1}"}' dicecube.v1
run_case darts darts '{"chatId":42,"displayName":"load","action":"play","payload":"{\"face\":1}"}' darts.v1
run_case football football '{"chatId":42,"displayName":"load","action":"play","payload":"{\"face\":1}"}' football.v1
run_case basketball basketball '{"chatId":42,"displayName":"load","action":"play","payload":"{\"face\":1}"}' basketball.v1
run_case bowling bowling '{"chatId":42,"displayName":"load","action":"play","payload":"{\"face\":1}"}' bowling.v1
run_case pick pick '{"chatId":42,"displayName":"load","action":"play","payload":"{\"variants\":[\"a\",\"b\",\"c\"],\"backedIndices\":[0]}"}' pick.v1

echo "=== host diagnostics ==="
grep -E '(^fail:|^warn:|Unhandled|Exception)' "$log_file" | sed -n '1,120p' || true
echo "diagnostic_log=$log_file"
