# Operations runbook


CasinoShiz is a simulation-only bot. All balances are virtual credits with no real-money value.

## Daily bonus

Users can claim a once-per-local-day bonus with:

```text
/daily
```

The wallet row stores the last claimed local day:

```text
users.last_daily_bonus_on
```

The ledger operation id is deterministic per user, scope, and local day:

```text
daily.bonus:{balanceScopeId}:{userId}:{yyyy-MM-dd}
```

This protects daily bonus delivery from duplicate Telegram updates, retries, and repeated command processing.

### Bonus calculation

The bonus is computed from the current wallet balance:

```text
floor(balance * PercentOfBalance / 100)
```

Then it is capped by `MaxBonus`.

If the current balance is zero, or the computed amount rounds down to zero, the day is not recorded as claimed. The user can still claim later on the same local day after earning enough coins.

### Catch-up after downtime

`DailyBonusCatchUpHostedService` handles missed days when the bot was offline.

It runs:

- once at startup
- then once per local day at `00:05`

The local day boundary is controlled by:

```text
Bot:DailyBonus:TimezoneOffsetHours
```

Catch-up credits missed **past** local days only. It does not auto-credit the current day, so `/daily` remains the manual claim for today.

Catch-up only applies to wallets that claimed daily at least once before:

```sql
last_daily_bonus_on IS NOT NULL
```

This avoids granting historical bonuses to wallets that never participated in daily bonuses.

Catch-up is capped by `MaxCatchUpDays`, so long outages or copied old databases cannot trigger an unbounded payout loop.

### Daily bonus configuration

Environment variables:

```env
Bot__DailyBonus__Enabled=true
Bot__DailyBonus__PercentOfBalance=0.35
Bot__DailyBonus__MaxBonus=8
Bot__DailyBonus__TimezoneOffsetHours=7
Bot__DailyBonus__CatchUpEnabled=true
Bot__DailyBonus__MaxCatchUpDays=14
```

Equivalent `appsettings.json` shape:

```json
{
  "Bot": {
    "DailyBonus": {
      "Enabled": true,
      "PercentOfBalance": 0.35,
      "MaxBonus": 8,
      "TimezoneOffsetHours": 7,
      "CatchUpEnabled": true,
      "MaxCatchUpDays": 14
    }
  }
}
```

### Daily bonus smoke checks

Recent daily bonus rows:

```sql
select id, telegram_user_id, balance_scope_id, delta, balance_after, reason, operation_id, created_at
from economics_ledger
where reason = 'daily.bonus'
order by id desc
limit 30;
```

Expected operation id shape:

```text
daily.bonus:{balanceScopeId}:{userId}:{yyyy-MM-dd}
```

Duplicate check:

```sql
select operation_id, count(*)
from economics_ledger
where operation_id is not null
  and reason = 'daily.bonus'
group by operation_id
having count(*) > 1;
```

Expected: zero rows.

Catch-up candidates:

```sql
select telegram_user_id, balance_scope_id, coins, last_daily_bonus_on
from users
where coins > 0
  and last_daily_bonus_on is not null
order by last_daily_bonus_on asc
limit 20;
```

## Scheduled jobs

There are two job categories in the UI:

| Kind | Meaning | Examples |
|---|---|---|
| `module` | Jobs registered by modules and supervised by `BackgroundJobRunner`. | `HorseScheduledRaceJob` |
| `host` | Host-level services that self-report to the shared status registry. | `DailyBonusCatchUpHostedService` |

Both are visible in:

```text
/__debug_jobs
/admin
```

The admin dashboard table shows:

- job name
- kind
- state
- last heartbeat
- next run time
- last completed / failed time
- crash count
- restart backoff
- note or last error

### Horse scheduled races

Horse races already have a module job:

```text
HorseScheduledRaceJob
```

Enable daily autorun with:

```env
Games__horse__AutoRunEnabled=true
Games__horse__AutoRunLocalHour=21
Games__horse__AutoRunLocalMinute=0
Games__horse__TimezoneOffsetHours=7
Games__horse__Admins__0=925337014
```

The job runs at most one global race per local calendar day after the configured local time, if enough bets exist.

Manual runs remain available through admin commands/UI.

### Scheduled jobs smoke checks

Telegram debug:

```text
/__debug_jobs
```

Expected example output:

```text
HorseScheduledRaceJob module
state: running

DailyBonusCatchUpHostedService host
state: waiting
next: ...
note: daily catch-up at local 00:05
```

Admin UI:

```text
/admin
```

Expected: **Background / host jobs** table contains both module and host jobs.

## Idempotent money-flow checks

Most virtual-credit mutations write a durable `operation_id` to `economics_ledger`.

Useful broad audit query:

```sql
select reason,
       count(*) as rows,
       count(operation_id) as with_operation_id,
       count(*) - count(operation_id) as without_operation_id
from economics_ledger
where created_at >= now() - interval '7 days'
group by reason
order by without_operation_id desc, reason;
```

Duplicate operation id check:

```sql
select operation_id, count(*)
from economics_ledger
where operation_id is not null
group by operation_id
having count(*) > 1
order by count(*) desc, operation_id;
```

Expected: zero rows.

### Important operation id shapes

```text
blackjack:start:{chatId}:{messageId}:{userId}
blackjack:settle:{userId}:{chatId}:{createdAtMs}

darts:bet:{chatId}:{messageId}:{userId}
darts:payout:{...}

bowling:bet:{chatId}:{messageId}:{userId}
basketball:bet:{chatId}:{messageId}:{userId}
football:bet:{chatId}:{messageId}:{userId}

dice:roll:{chatId}:{sourceMessageId}:{userId}:stake
dice:roll:{chatId}:{sourceMessageId}:{userId}:prize

dicecube:bet:{chatId}:{sourceMessageId}:{userId}
dicecube:payout:{userId}:{chatId}:{createdAtMs}

horse:bet:{balanceScopeId}:{sourceMessageId}:{userId}
horse:payout:{raceDate}:{kind}:{resultScope}:{userId}:{balanceScopeId}

peer:{chatId}:{sourceMessageId}:{fromUserId}:{toUserId}:send
peer:{chatId}:{sourceMessageId}:{fromUserId}:{toUserId}:receive

poker:create:{chatId}:{messageId-or-inviteCode}:{userId}
poker:join:{chatId}:{messageId-or-joinedAtMs}:{userId}:{code}
poker:refund:{code}:{userId}:{chatId}:{reason}:{joinedAt}
poker:win:{code}:{tableCreatedAt}:{lastActionAt}:{userId}:{won}

daily.bonus:{balanceScopeId}:{userId}:{yyyy-MM-dd}
admin.set / admin.adjust: GUID operation id generated by Admin Wallets form
```

## Admin wallet edits

Admin wallet edits in Admin → Wallets are idempotent per rendered form.

The page emits a hidden GUID operation id for:

- `Set`
- `+/-`

Ledger reasons:

```text
admin.set
admin.adjust
```

Smoke check:

```sql
select id, telegram_user_id, balance_scope_id, delta, balance_after, reason, operation_id, created_at
from economics_ledger
where reason in ('admin.set', 'admin.adjust')
order by id desc
limit 20;
```

Expected: new admin rows have non-null GUID operation ids.

## Event dispatch failures

Failed projection/event dispatches are tracked in:

```text
event_dispatch_failures
```

Telegram debug commands:

```text
/__debug_dispatch_failures
/__debug_retry_dispatch_failure <id>
```

SQL:

```sql
select id, stream_id, stream_version, event_type, retry_count, resolved_at
from event_dispatch_failures
order by id desc
limit 20;
```

Expected healthy state:

```text
unresolved dispatch failures: 0
```

## Event-sourced smoke projection

Debug ES smoke events use:

```text
debug.es_smoke_incremented
```

Checks:

```sql
select count(*)
from module_events
where event_type = 'debug.es_smoke_incremented';

select *
from debug_es_smoke_projection
order by stream_id;
```

The projection can be truncated and should rebuild from `module_events` through replay/subscription.

## Docker port mapping notes

If the host app listens inside the container on port `3000`, but you want the admin UI on host port `4000`, map host-to-container like this:

```yaml
ports:
  - "127.0.0.1:4000:3000"
```

Do not use `4000:4000` unless ASP.NET is actually listening on port `4000` inside the container.

Useful checks:

```bash
sudo docker compose ps
sudo docker port cazinoshiz-cazino-bot-1
sudo docker logs cazinoshiz-cazino-bot-1 --tail=100
curl -v http://127.0.0.1:4000/admin
```

Expected port mapping example:

```text
3000/tcp -> 127.0.0.1:4000
```

## Weekly summaries and economy anomaly alerts

`meta.operations_reporting` evaluates operations state every 15 minutes. It queues the previous completed ISO week's summary once per configured `Bot:Admins` recipient through `telegram_outbox`. It also alerts on:

- wallets below zero;
- a wallet balance differing from its latest ledger balance;
- ledger mutations of at least 100,000 coins in the preceding 15 minutes.

Identical anomaly signatures are deduplicated per UTC hour in `operations_report_checkpoint`. Useful checks:

```sql
SELECT * FROM operations_report_checkpoint ORDER BY updated_at DESC;
SELECT status, count(*) FROM telegram_outbox GROUP BY status;
SELECT * FROM telegram_outbox
WHERE dedupe_key LIKE 'weekly-summary:%' OR dedupe_key LIKE 'economy-alert:%'
ORDER BY id DESC LIMIT 20;
```

Player protections are stored in `player_protection`. Cooldowns and self-exclusion can be extended but cannot be shortened before expiry. A player's current state can be inspected without changing it:

```sql
SELECT * FROM player_protection WHERE telegram_user_id = <USER_ID>;
```
