-- Multi-user eventual wager workload.
--
-- Required environment:
--   REST_DEV_TOKEN, REST_TENANT
-- Optional environment:
--   WAGER_GAME_ID       game resolver id (default: dice)
--   WAGER_GAME_INPUT    JSON object passed as the nested gameInput string
--   WAGER_RULES_VERSION terms version (default: dice.v1)
--   WAGER_STAKE         reservation amount (default: 10)
--   LOAD_USER_BASE, LOAD_USER_COUNT

local setup_thread_id = 0

function setup(thread)
  setup_thread_id = setup_thread_id + 1
  thread:set("wager_thread_id", setup_thread_id)
end

local function json_escape(value)
  return value:gsub('\\', '\\\\')
             :gsub('"', '\\"')
             :gsub('\n', '\\n')
             :gsub('\r', '\\r')
             :gsub('\t', '\\t')
end

function init()
  thread_id = tonumber(wrk.thread:get("wager_thread_id")) or 1
  request_number = 0
  game_id = os.getenv("WAGER_GAME_ID") or "dice"
  game_input = os.getenv("WAGER_GAME_INPUT") or '{"chatId":42,"displayName":"load","action":"play","payload":"{\\"diceValue\\":1}"}'
  rules_version = os.getenv("WAGER_RULES_VERSION") or (game_id .. ".v1")
  stake = tonumber(os.getenv("WAGER_STAKE") or "10")
  tenant = os.getenv("REST_TENANT") or "e2e"
  user_base = tonumber(os.getenv("LOAD_USER_BASE") or "7000001")
  user_count = tonumber(os.getenv("LOAD_USER_COUNT") or "32")
  headers = {
    ["Authorization"] = "Bearer " .. assert(os.getenv("REST_DEV_TOKEN"), "REST_DEV_TOKEN is required"),
    ["Content-Type"] = "application/json"
  }
end

function request()
  request_number = request_number + 1
  local user_id = user_base + ((thread_id - 1) * user_count) + ((request_number - 1) % user_count)
  local suffix = string.format("%d-%d-%d", thread_id, request_number, user_id)
  local body = string.format(
    '{"gameId":"%s","betId":"wager-load-%s","gameInput":"%s","terms":{"rulesVersion":"%s","stake":%d,"currency":"coins","settlementRule":"standard"}}',
    json_escape(game_id),
    suffix,
    json_escape(game_input),
    json_escape(rules_version),
    stake)
  headers["X-Load-Test-User-Id"] = tostring(user_id)
  headers["Idempotency-Key"] = "wager-write-" .. suffix
  return wrk.format("POST", "/api/v1/tenants/" .. tenant .. "/scopes/42/wagers", headers, body)
end
