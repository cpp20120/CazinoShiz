# Wager batch post-migration profile — 2026-08-09

Profile: `bash eng/profile-scripts/run-wager-saga.sh`

- disposable PostgreSQL 17 Testcontainer
- `wrk -t2 -c8 -d8s --latency`
- 10,000 isolated users, seeded wallets, same host profile as the legacy control
- endpoint: `POST /api/v1/tenants/write-load/scopes/42/wagers`
- path: reserve → game command → committed game outcome → settlement
- stake: 10 coins; all requests returned 2xx in this run

| game | req/s | avg latency | p99 latency | requests |
|---|---:|---:|---:|---:|
| dice | 315.38 | 50.75 ms | 635.85 ms | 2,525 |
| dicecube | 597.72 | 14.88 ms | 44.74 ms | 4,786 |
| darts | 862.53 | 11.93 ms | 43.78 ms | 6,904 |
| football | 1,112.21 | 10.41 ms | 41.78 ms | 8,903 |
| basketball | 1,325.55 | 9.88 ms | 43.69 ms | 10,616 |
| bowling | 1,539.27 | 9.35 ms | 42.06 ms | 12,329 |
| pick | 1,727.08 | 9.09 ms | 46.44 ms | 13,829 |

The original pre-migration snapshot is recorded in
`wager-batch-baseline-2026-08-09.md`; the seeded legacy control is in
`wager-batch-legacy-control-2026-08-09.md`. This post profile uses the generic
saga route, so it is an operational comparison, not a same-code-path
microbenchmark. The successful run includes the integration migration fix,
concrete-command runtime dispatch fix, case-insensitive game-input parsing,
and generic outcome entropy registration.

Host diagnostics contained only the pre-existing Quartz schema warnings.
