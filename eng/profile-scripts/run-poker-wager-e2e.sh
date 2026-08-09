#!/usr/bin/env bash
set -euo pipefail

# Reserve two players, create/join one table, run an all-in hand, and wait for
# both independent wager operations to settle. This is the post-migration
# functional proof for the multi-player Poker saga.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
port="${POKER_WAGER_E2E_PORT:-18128}"
base="http://127.0.0.1:$port"
log_file="$(mktemp /tmp/casinoshiz-poker-wager-e2e.XXXXXX.log)"
host_pid=""

cleanup() {
  if [[ -n "$host_pid" ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill "$host_pid" 2>/dev/null || true
    wait "$host_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

(
  cd "$repo_root"
  env DOCKER_HOST=unix:///var/run/docker.sock FULL_REST_WRITE_LOAD_PORT="$port" \
    FULL_REST_WRITE_LOAD_SEED_USER_BASE=9410001 FULL_REST_WRITE_LOAD_SEED_USER_COUNT=2 \
    FULL_REST_WRITE_LOAD_SEED_COINS=1000 \
    dotnet run --project tests/CasinoShiz.FullRestWriteLoadTest -c Release --no-build
) >"$log_file" 2>&1 &
host_pid=$!

for _ in {1..120}; do
  grep -q "FULL_REST_WRITE_LOAD_READY" "$log_file" && break
  kill -0 "$host_pid" 2>/dev/null || { sed -n '1,240p' "$log_file" >&2; exit 1; }
  sleep 0.25
done
grep -q "FULL_REST_WRITE_LOAD_READY" "$log_file" || { sed -n '1,240p' "$log_file" >&2; exit 1; }

curl_auth() {
  local user_id="$1"
  shift
  curl -H "Authorization: Bearer full-rest-write-load-test" \
    -H "X-Load-Test-User-Id: $user_id" \
    -H "Content-Type: application/json" "$@"
}

create_wager() {
  local user_id="$1" bet_id="$2" idempotency="$3" action="$4" payload="$5"
  local game_input body
  game_input="$(jq -cn --arg action "$action" --arg payload "$payload" \
    '{chatId:42,displayName:("poker-" + $action),action:$action,payload:$payload}')"
  body="$(jq -cn --arg gameInput "$game_input" --arg betId "$bet_id" \
    '{gameId:"poker",betId:$betId,gameInput:$gameInput,terms:{rulesVersion:"poker.v1",stake:10,currency:"coins",settlementRule:"standard"}}')"
  curl_auth "$user_id" -fsS -X POST "$base/api/v1/tenants/write-load/scopes/42/wagers" \
    -H "Idempotency-Key: $idempotency" -d "$body"
}

wait_status() {
  local operation_id="$1" user_id="$2" expected="$3"
  for _ in {1..80}; do
    status="$(curl_auth "$user_id" -fsS \
      "$base/api/v1/tenants/write-load/scopes/42/wagers/$operation_id" | jq -r '.status')"
    [[ "$status" == "$expected" ]] && return 0
    [[ "$status" == "Rejected" || "$status" == "Failed" ]] && {
      echo "operation $operation_id stopped at $status" >&2
      curl_auth "$user_id" -fsS \
        "$base/api/v1/tenants/write-load/scopes/42/wagers/$operation_id" >&2
      return 1
    }
    sleep 0.25
  done
  echo "timeout waiting for $operation_id -> $expected" >&2
  return 1
}

user_one=9410001
user_two=9410002

create_response="$(create_wager "$user_one" poker-bet-one poker-create-e2e create \
  '{"buyIn":10,"smallBlind":1,"bigBlind":2}')"
create_operation="$(jq -r '.operationId' <<<"$create_response")"
wait_status "$create_operation" "$user_one" Playing

# The game-facing table is visible independently from the wager operation. The
# operation reaches Playing before the integration command is consumed, so
# allow the game outbox a short window to create the table.
table=""
table_id=""
for _ in {1..80}; do
  table="$(curl_auth "$user_one" -fsS \
    "$base/api/v1/tenants/write-load/scopes/42/poker/tables/me" 2>/dev/null || true)"
  table_id="$(jq -r '.table.inviteCode // empty' <<<"$table" 2>/dev/null || true)"
  [[ -n "$table_id" ]] && break
  sleep 0.25
done
[[ -n "$table_id" ]]

join_response="$(create_wager "$user_two" poker-bet-two poker-join-e2e join \
  "{\"inviteCode\":\"$table_id\",\"buyIn\":10,\"maxPlayers\":8}")"
join_operation="$(jq -r '.operationId' <<<"$join_response")"
wait_status "$join_operation" "$user_two" Playing

# Start the hand through the existing table API; the game operation itself is
# outcome-only and only emits settlement facts when the hand ends.
start_response="$(curl_auth "$user_one" -sS -X POST \
  "$base/api/v1/tenants/write-load/scopes/42/poker/tables/$table_id/start" \
  -H "Idempotency-Key: poker-start-e2e")"
echo "start_response=$start_response"
one_response="$(curl_auth "$user_one" -sS -X POST \
  "$base/api/v1/tenants/write-load/scopes/42/poker/tables/$table_id/actions" \
  -H "Idempotency-Key: poker-allin-one-e2e" -d '{"verb":"allin","amount":0}')"
echo "allin_one_response=$one_response"

wait_status "$create_operation" "$user_one" Completed
wait_status "$join_operation" "$user_two" Completed

echo "poker_wager_e2e=passed"
echo "create_operation=$create_operation"
echo "join_operation=$join_operation"
echo "table_id=$table_id"
echo "diagnostic_log=$log_file"
grep -E '(^fail:|^warn:|Unhandled|Exception)' "$log_file" | sed -n '1,120p' || true
