# Wager batch performance baseline

Captured before the batch migration on 2026-08-09.

- host: `CasinoShiz.FullRestWriteLoadTest`
- database: disposable PostgreSQL 17 Testcontainer
- load: `wrk -t2 -c8 -d8s --latency`
- users: 10,000 per wrk thread range
- payloads: amount/slot value 10/1 as applicable

| Route | Requests/sec | Avg latency | p99 latency |
|---|---:|---:|---:|
| `/dice/roll` | 338.03 | 62.96 ms | 800.00 ms |
| `/dicecube/play` | 160.97 | 50.08 ms | 115.10 ms |
| `/darts/play` | 298.77 | 26.73 ms | 41.77 ms |
| `/football/play` | 146.85 | 54.28 ms | 85.19 ms |
| `/basketball/play` | 129.69 | 61.37 ms | 133.93 ms |
| `/bowling/play` | 62.77 | 126.48 ms | 181.88 ms |
| `/horse/bet` | 189.50 | 42.24 ms | 88.84 ms |

The host emitted cancellation failures while shutting down immediately after
the load phases. Quartz schema and MediatR development warnings are harness
warnings; the values above are throughput observations, not an error-rate
claim. The same harness and settings must be used after migration.
