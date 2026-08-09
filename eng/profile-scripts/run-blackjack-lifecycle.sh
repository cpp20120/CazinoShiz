#!/usr/bin/env bash
set -euo pipefail

# Runs a disposable, multi-user Blackjack start -> stand diagnostic.
# It owns the Testcontainers host it starts and never touches k3s or dev data.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
port="${FULL_REST_WRITE_LOAD_PORT:-18124}"
duration="${BLACKJACK_LOAD_DURATION:-10s}"
users="${BLACKJACK_LOAD_USERS:-10000}"
user_base="${BLACKJACK_LOAD_USER_BASE:-8800000}"
log_file="$(mktemp /tmp/casinoshiz-blackjack-lifecycle.XXXXXX.log)"

cleanup() {
  if [[ -n "${host_pid:-}" ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill "$host_pid"
    wait "$host_pid" || true
  fi
}
trap cleanup EXIT

(
  cd "$repo_root"
  env DOCKER_HOST=unix:///var/run/docker.sock FULL_REST_WRITE_LOAD_PORT="$port" \
    dotnet run --project tests/CasinoShiz.FullRestWriteLoadTest -c Release --no-build
) >"$log_file" 2>&1 &
host_pid=$!

for _ in {1..120}; do
  if grep -q "FULL_REST_WRITE_LOAD_READY" "$log_file"; then
    break
  fi
  if ! kill -0 "$host_pid" 2>/dev/null; then
    cat "$log_file" >&2
    exit 1
  fi
  sleep 0.25
done

if ! grep -q "FULL_REST_WRITE_LOAD_READY" "$log_file"; then
  echo "The isolated REST host did not become ready." >&2
  cat "$log_file" >&2
  exit 1
fi

run_wrk() {
  local path="$1"
  local body="$2"
  env REST_DEV_TOKEN=full-rest-write-load-test \
    REST_TENANT=write-load \
    WRITE_PATH="$path" \
    WRITE_BODY="$body" \
    LOAD_USER_BASE="$user_base" \
    LOAD_USER_COUNT="$users" \
    wrk -t2 -c8 -d"$duration" \
      -s "$repo_root/eng/profile-scripts/write/atomic-play.lua" \
      "http://127.0.0.1:$port"
}

echo "== Blackjack start =="
run_wrk /blackjack/start '{"bet":10}'
echo "== Blackjack stand =="
run_wrk /blackjack/stand '{}'
echo "== Host warnings and errors =="
grep -E '(^fail:|^warn:|Unhandled|Exception)' "$log_file" || true
echo "Diagnostic log: $log_file"
