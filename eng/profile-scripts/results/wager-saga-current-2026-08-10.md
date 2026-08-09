# Wager saga current profile — 2026-08-10

Profile: `WAGER_SAGA_PROFILE_DURATION=8s WAGER_SAGA_PROFILE_USERS=10000 bash eng/profile-scripts/run-wager-saga.sh`

- disposable PostgreSQL 17 Testcontainer
- `wrk -t2 -c8 -d8s --latency`
- 10,000 seeded users
- route: reserve → game command → committed outcome → settlement

| game | req/s | avg latency | p99 latency | requests |
|---|---:|---:|---:|---:|
| dice | 413.03 | 31.52 ms | 413.39 ms | 3,307 |
| dicecube | 833.57 | 10.40 ms | 27.72 ms | 6,672 |
| darts | 1,197.85 | 8.34 ms | 28.91 ms | 9,702 |
| football | 1,538.40 | 7.63 ms | 29.59 ms | 12,318 |
| basketball | 1,814.29 | 7.40 ms | 31.34 ms | 14,523 |
| bowling | 2,070.10 | 7.12 ms | 32.11 ms | 16,565 |
| pick | 2,263.71 | 7.45 ms | 38.23 ms | 18,117 |

The run completed successfully. Diagnostics contained the pre-existing Quartz
schema warnings and expected `OperationCanceledException` entries when the
load harness stopped the host after the final phase.
