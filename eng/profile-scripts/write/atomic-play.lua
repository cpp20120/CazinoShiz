-- Real multi-user write workload for atomic game operations.
--
-- Required environment:
--   REST_DEV_TOKEN     development bearer token
--   WRITE_PATH         REST route, e.g. /football/play
-- Optional environment:
--   WRITE_BODY         JSON request body (default: {"amount":10})
--   REST_TENANT        tenant route value (default: e2e)
--   LOAD_USER_BASE     first isolated user ID (default: 7000001)
--   LOAD_USER_COUNT    number of users per wrk thread (default: 32)

local setup_thread_id = 0

function setup(thread)
  setup_thread_id = setup_thread_id + 1
  thread:set("write_thread_id", setup_thread_id)
end

function init()
  thread_id = tonumber(wrk.thread:get("write_thread_id")) or 1
  request_number = 0
  local token = assert(os.getenv("REST_DEV_TOKEN"), "REST_DEV_TOKEN is required")
  path = assert(os.getenv("WRITE_PATH"), "WRITE_PATH is required")
  body = os.getenv("WRITE_BODY") or '{"amount":10}'
  user_base = tonumber(os.getenv("LOAD_USER_BASE") or "7000001")
  user_count = tonumber(os.getenv("LOAD_USER_COUNT") or "32")
  tenant = os.getenv("REST_TENANT") or "e2e"
  headers = {
    ["Authorization"] = "Bearer " .. token,
    ["Content-Type"] = "application/json"
  }
end

function request()
  request_number = request_number + 1
  local user_id = user_base + ((thread_id - 1) * user_count) + ((request_number - 1) % user_count)
  headers["X-Load-Test-User-Id"] = tostring(user_id)
  headers["Idempotency-Key"] = string.format("write-%d-%d", thread_id, request_number)
  return wrk.format("POST", "/api/v1/tenants/" .. tenant .. "/scopes/42" .. path, headers, body)
end
