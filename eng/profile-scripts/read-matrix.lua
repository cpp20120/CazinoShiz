-- Equal-weight successful REST read workload for the local development cluster.
--
-- This measures aggregate read capacity of REST -> gRPC -> game backends.
-- Stateful commands intentionally do not belong here: they need a prepared,
-- multi-user fixture and must be measured as their own workload.

local routes = {
  "/horse/info",
  "/horse/result",
  "/pick/daily/history",
  "/pick/daily/schedule",
  "/pixelbattle/grid",
  "/leaderboard/",
  "/meta/season",
  "/meta/profile",
  "/meta/top",
  "/meta/achievements",
  "/meta/streaks",
  "/meta/quests",
  "/meta/clan/top",
  "/meta/tournaments/open",
  "/meta/risk"
}

function init()
  local token = assert(os.getenv("REST_DEV_TOKEN"), "REST_DEV_TOKEN is required")
  headers = { ["Authorization"] = "Bearer " .. token }
  next_route = 0
end

function request()
  next_route = (next_route % #routes) + 1
  return wrk.format("GET", "/api/v1/tenants/e2e/scopes/42" .. routes[next_route], headers)
end
