# Legacy atomic control profile — 2026-08-09

This is the seeded re-run of the legacy aliases, used as the closest control
for the saga profile. It uses the same `wrk -t2 -c8 -d8s --latency` settings,
10,000 isolated users, and a disposable PostgreSQL 17 Testcontainer. Wallets
were seeded so the requests exercised accepted game writes rather than the
insufficient-funds rejection path.

| Route | Requests/sec | Avg latency | p99 latency |
|---|---:|---:|---:|
| `/dice/roll` | 336.51 | 55.72 ms | 714.05 ms |
| `/dicecube/play` | 162.64 | 49.97 ms | 154.31 ms |
| `/darts/play` | 280.13 | 28.53 ms | 49.37 ms |
| `/football/play` | 144.95 | 55.06 ms | 80.26 ms |
| `/basketball/play` | 130.60 | 61.13 ms | 139.69 ms |
| `/bowling/play` | 61.58 | 128.99 ms | 190.85 ms |
| `/horse/bet` | 183.64 | 43.65 ms | 90.38 ms |

Shutdown cancellation messages are harness artifacts; Quartz schema warnings
are pre-existing diagnostics.
