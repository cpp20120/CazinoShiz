# Full REST write-load host

This is a local, disposable performance host for REST game write paths. It
starts its own PostgreSQL 17 Testcontainer, applies the real framework and game
migrations, and serves the complete REST surface on loopback only. It never
connects to k3s, a development database, Redis, or ClickHouse.

Run it from the repository root:

```bash
dotnet run --project tests/CasinoShiz.FullRestWriteLoadTest -c Release
```

The host prints its address and the fixed development bearer token. Stop it
with `Ctrl+C`; its PostgreSQL container is then disposed as well.

`eng/profile-scripts/write/atomic-play.lua` drives independent-user atomic
write paths. For example:

```bash
REST_DEV_TOKEN=full-rest-write-load-test \
REST_TENANT=write-load \
WRITE_PATH=/football/play \
WRITE_BODY='{"amount":10}' \
LOAD_USER_BASE=8400000 \
LOAD_USER_COUNT=10000 \
wrk -t2 -c8 -d15s \
  -s eng/profile-scripts/write/atomic-play.lua \
  http://127.0.0.1:18120
```

Each request receives a distinct user ID (cycling only after
`LOAD_USER_COUNT`) and idempotency key. Use enough users for the expected
request count to avoid measuring one player wallet or one active game session.
The script is appropriate for self-contained actions such as native-dice
`/play`. Multi-step games must be prepared and measured as separate lifecycle
phases (for example Blackjack `start` followed by `stand`), because the result
of one action is state for the next.

This is a benchmark harness, not a production host. Its authentication handler
and high starting balance exist solely inside this test process.
