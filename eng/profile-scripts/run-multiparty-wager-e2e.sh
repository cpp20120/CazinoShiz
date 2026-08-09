#!/usr/bin/env bash
set -euo pipefail

# Proves the generic multi-party reservation saga: reserve all players, run an
# outcome-only adapter, settle each reservation, and refund earlier reserves
# when a later participant cannot reserve.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
port="${MULTIPARTY_WAGER_E2E_PORT:-18129}"
base="http://127.0.0.1:$port"
log_file="$(mktemp /tmp/casinoshiz-multiparty-wager-e2e.XXXXXX.log)"
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
    FULL_REST_WRITE_LOAD_SEED_USER_BASE=9510001 FULL_REST_WRITE_LOAD_SEED_USER_COUNT=2 \
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

create_group() {
  local user_id="$1" workflow_id="$2" game_id="$3" game_input="$4" second_stake="$5"
  local body
  body="$(jq -cn \
    --arg gameId "$game_id" \
    --arg gameInput "$game_input" \
    --arg betOne "${workflow_id}-one" \
    --arg betTwo "${workflow_id}-two" \
    --arg playerOne "9510001" \
    --arg playerTwo "9510002" \
    --argjson secondStake "$second_stake" \
    '{gameId:$gameId,gameInput:$gameInput,participants:[
      {betId:$betOne,playerId:$playerOne,stake:100,currency:"coins"},
      {betId:$betTwo,playerId:$playerTwo,stake:$secondStake,currency:"coins"}
    ]}')"
  curl_auth "$user_id" -fsS -X POST \
    "$base/api/v1/tenants/write-load/scopes/42/wager-groups" \
    -H "Idempotency-Key: $workflow_id" -d "$body"
}

wait_group() {
  local workflow_id="$1" expected="$2"
  local response status raw http_code
  for _ in {1..80}; do
    raw="$(curl_auth 9510001 -sS -w '\n%{http_code}' \
      "$base/api/v1/tenants/write-load/scopes/42/wager-groups/$workflow_id")"
    http_code="${raw##*$'\n'}"
    response="${raw%$'\n'*}"
    [[ "$http_code" == "404" ]] && { sleep 0.25; continue; }
    [[ "$http_code" != "200" ]] && {
      echo "workflow $workflow_id status endpoint returned HTTP $http_code: $response" >&2
      return 1
    }
    status="$(jq -r '.status' <<<"$response")"
    [[ "$status" == "$expected" ]] && { echo "$response"; return 0; }
    [[ "$status" == "failed" ]] && {
      echo "workflow $workflow_id failed: $response" >&2
      return 1
    }
    sleep 0.25
  done
  echo "timeout waiting for $workflow_id -> $expected" >&2
  return 1
}

completed_id="multiparty-challenge-e2e"
create_group 9510001 "$completed_id" challenge \
  '{"maxRoll":6}' 100
completed_response="$(wait_group "$completed_id" completed)"
[[ "$(jq -r '.outcomes | length' <<<"$completed_response")" == "2" ]]
[[ "$(jq '[.outcomes[].payout] | add' <<<"$completed_response")" == "196" ]]

compensated_id="multiparty-compensation-e2e"
create_group 9510001 "$compensated_id" pick-pool \
  '{}' 2000
compensated_response="$(wait_group "$compensated_id" compensated)"
[[ "$(jq -r '.errorCode' <<<"$compensated_response")" == "participant_reservation_rejected" ]]

echo "multiparty_wager_e2e=passed"
echo "completed=$completed_response"
echo "compensated=$compensated_response"
echo "diagnostic_log=$log_file"
grep -E '(^fail:|^warn:|Unhandled|Exception)' "$log_file" | sed -n '1,120p' || true
