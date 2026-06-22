# CasinoShiz: математическая модель казино-бота

Дата среза: 2026-06-22.

Документ описывает текущую модель кода, а не желаемый баланс. Основные источники:
`games/Games.*/Application/Services/*`, `games/Games.*/Domain/Configuration/*`,
`games/Games.Meta/*`, `framework/BotFramework.Host/Economics/*`.

## 1. Обозначения

- `B_t` - баланс игрока перед операцией.
- `s` - ставка игрока.
- `p` - выплата игроку после исхода.
- `m` - множитель выплаты, так что `p = s * m`.
- `EV[p]` - математическое ожидание выплаты.
- `RTP = EV[p] / s` - return-to-player без внешних бонусов.
- `edge = 1 - RTP` - ожидаемая доля ставки, остающаяся системе.
- `net = p - s` - чистый результат игрока, так как ставка списывается до исхода.

В большинстве игр экономика устроена одинаково:

```text
B_after = B_before - s + p
EV[net] = EV[p] - s = s * (RTP - 1)
```

Операции списания/начисления делаются идемпотентно через operation id, поэтому повторная доставка события не должна удваивать результат.

## 2. Базовая экономика монет

### 2.1. Ставки и выплаты

Для игр с предварительной ставкой:

```text
debit(s, reason = "<game>.bet")
roll outcome
credit(p, reason = "<game>.payout") if p > 0
```

Ставка ограничивается `MaxBet` в настройках конкретной игры. Если средств не хватает, ставка не принимается.

### 2.2. Переводы

Transfer отправляет получателю `n` монет, а с отправителя списывает:

```text
fee_raw = n * FeePercent
fee_half = round_away_from_zero(fee_raw * 2) / 2
fee = max(MinFeeCoins, round_away_from_zero(fee_half))
total_debit = n + fee
```

Дефолт:

```text
FeePercent = 0.03
MinFeeCoins = 1
```

Комиссия перевода является sink монет.

### 2.3. Daily bonus

Ежедневный бонус:

```text
bonus = min(MaxBonus, floor(balance * PercentOfBalance / 100))
```

Если `balance <= 0`, `PercentOfBalance <= 0`, `MaxBonus <= 0` или `floor(...) < 1`, бонус равен `0`.

Дефолт:

```text
PercentOfBalance = 0.35
MaxBonus = 8
MaxCatchUpDays = 14
```

Это faucet монет, ограниченный сверху `8` монетами за локальный день.

### 2.4. Gas tax и bank tax

Gas tax:

```text
GasDefault = 0.0285
GasModifier = sqrt(2)

GasFunction(x) = max(1, ((x + 1) ^ log10(x + 1) - 1) / 39.15)

if x < 10:
  gas(x) = round(GasFunction(x) * GasModifier)
else:
  gas(x) = round(x * GasDefault * GasModifier)
```

Текущее важное поведение: `gas(9) = 1`, но `gas(10) = 0` из-за округления во второй ветке.

Bank tax:

```text
if bank < 70:       bank_tax = -2
else if bank < 120: bank_tax = 0
else:               bank_tax = round(max(4, min(gas(floor(bank)), 25)) / 2)
```

## 3. Модель отдельных игр

### 3.1. Telegram dice games

Исходы Telegram dice считаются равномерными по целым значениям:

- `dicecube`, `darts`, `bowling`: `face in {1..6}`;
- `football`, `basketball`: `face in {1..5}`;
- `slots`: 64 закодированных комбинации барабана.

#### Dice cube

Дефолтные множители:

| face | 1 | 2 | 3 | 4 | 5 | 6 |
|---:|---:|---:|---:|---:|---:|---:|
| `m` | 0 | 0 | 0 | 1 | 2 | 2 |

```text
EV[p] = s * (0 + 0 + 0 + 1 + 2 + 2) / 6 = 5s/6
RTP = 83.3333%
edge = 16.6667%
```

`Mult4`, `Mult5`, `Mult6` конфигурируемые и snapshot-ятся в строку pending bet. Общая формула:

```text
RTP = (Mult4 + Mult5 + Mult6) / 6
```

#### Darts

Множители:

| face | 1 | 2 | 3 | 4 | 5 | 6 |
|---:|---:|---:|---:|---:|---:|---:|
| `m` | 0 | 0 | 0 | 1 | 2 | 2 |

```text
RTP = 5/6 = 83.3333%
edge = 16.6667%
```

#### Bowling

Множители:

| face | 1 | 2 | 3 | 4 | 5 | 6 |
|---:|---:|---:|---:|---:|---:|---:|
| `m` | 0 | 0 | 0 | 1 | 2 | 2 |

```text
RTP = 5/6 = 83.3333%
edge = 16.6667%
```

#### Football

Множители:

| face | 1 | 2 | 3 | 4 | 5 |
|---:|---:|---:|---:|---:|---:|
| `m` | 0 | 0 | 0 | 2 | 2 |

```text
EV[p] = s * 4/5
RTP = 80%
edge = 20%
```

#### Basketball

Множители:

| face | 1 | 2 | 3 | 4 | 5 |
|---:|---:|---:|---:|---:|---:|
| `m` | 0 | 0 | 0 | 2 | 2 |

```text
RTP = 80%
edge = 20%
```

### 3.2. Slots (`/dice`, Telegram slot machine)

Текущий `DiceOptions.Cost = 9`. Сверху добавляется gas:

```text
loss = Cost + gas(Cost)
```

Для дефолта:

```text
gas(9) = 1
loss = 10
```

Telegram slot value декодируется в 3 барабана по 4 символа:

```text
roll_0 = ((value - 1) >> 0) & 3
roll_1 = ((value - 1) >> 2) & 3
roll_2 = ((value - 1) >> 4) & 3
```

Символы и веса суммы:

| index | symbol | stake price |
|---:|---|---:|
| 0 | bar | 1 |
| 1 | cherry | 1 |
| 2 | lemon | 2 |
| 3 | seven | 3 |

Выплата:

```text
rolls_sum = sum(stake_price[roll_i])

three seven  -> 77
three lemon  -> 30
three cherry -> 23
three bar    -> 21
two seven    -> 10 + rolls_sum
two lemon    -> 6 + rolls_sum
any other pair -> 4 + rolls_sum
no pair      -> rolls_sum - 3
```

По всем 64 равновероятным исходам:

```text
sum(payouts) = 610
EV[p] = 610 / 64 = 9.53125
```

Для дефолтного `loss = 10`:

```text
RTP = 9.53125 / 10 = 95.3125%
edge = 4.6875%
EV[net] = -0.46875 монеты за spin
```

Если изменится `Cost`, RTP относительно списания меняется:

```text
RTP(Cost) = 9.53125 / (Cost + gas(Cost))
```

### 3.3. Blackjack

Модель:

- колода перемешивается случайно;
- старт: игроку 2 карты, дилеру 2 карты;
- natural blackjack игрока на старте settles immediately;
- дилер добирает, пока сумма `< 17`;
- double разрешен только на первых двух картах игрока;
- double списывает еще одну ставку и удваивает `Bet`;
- туз считается как 11, если не перебор, иначе как 1.

Формула settlement:

```text
if player_total > 21: payout = 0
else if player_blackjack and not dealer_blackjack: payout = bet + floor(3 * bet / 2)
else if player_blackjack and dealer_blackjack: payout = bet
else if dealer_total > 21: payout = 2 * bet
else if player_total > dealer_total: payout = 2 * bet
else if player_total < dealer_total: payout = 0
else: payout = bet
```

Для natural blackjack выплата равна `2.5x` ставки с целочисленным `floor(1.5 * bet)` в бонусной части.

Закрытого RTP в коде нет, потому что он зависит от стратегии игрока: hit/stand/double выбираются пользователем. Для аудита нужен отдельный Monte Carlo или exact enumeration по стратегии.

### 3.4. Pick

Пользователь задает `N` вариантов и выбирает `k` backed variants. Победа:

```text
P(win) = k / N
gross = s * N / k
base_payout = floor(gross * (1 - HouseEdge))
```

Дефолт:

```text
HouseEdge = 0.05
MinVariants = 2
MaxVariants = 10
MaxBet = 5000
```

Без streak bonus:

```text
EV[p] ~= s * (1 - HouseEdge)
RTP ~= 95%
edge ~= 5%
```

Из-за `floor` фактический RTP немного ниже 95% на малых ставках.

Streak bonus применяется только к top-level Pick, не к chain:

```text
factor = min(max(0, streak_after - 1), StreakCap)
bonus = floor(s * factor * StreakBonusPerWin)
total_credit = base_payout + bonus
```

Дефолт:

```text
StreakBonusPerWin = 0.5
StreakCap = 4
ChainMaxDepth = 5
```

Следствие: при длинной серии побед streak bonus может перекрывать house edge и становиться faucet монет. Это не баг реализации, а текущая баланс-особенность.

Chain:

```text
stake_next = total_credit
```

Chain не применяет streak math, но позволяет реинвестировать выигрыш до `ChainMaxDepth`.

### 3.5. Pick lottery

Pool lottery:

```text
pot = sum(stake_i)
if entrants < MinEntrantsToSettle:
  refund all stakes
else:
  winner ~ Uniform(entries)
  fee = floor(pot * HouseFeePercent)
  payout = pot - fee
```

Дефолт:

```text
MinEntrantsToSettle = 2
HouseFeePercent = 0.05
```

Для участника с одной равной ставкой `s` среди `n` участников:

```text
P(win) = 1/n
EV[p] = (pot - fee) / n
```

Если все ставки равны и `fee ~= 5%`, RTP около `95%`.

Daily lottery использует ту же идею с tickets:

```text
pot = ticket_count * TicketPrice
winner probability = user_tickets / total_tickets
payout = pot - floor(pot * HouseFeePercent)
```

### 3.6. Horse race

Horse race - pari-mutuel pool с коэффициентом, зависящим от распределения ставок.

Для лошади `h`:

```text
S_h = total stake on horse h
P = total pot

if S_h = 0:
  k_h = 1.0
else:
  k_h = floor(((P - S_h) / (1.1 * S_h)) * 1000) / 1000 + 1
```

Если `h` победила, выплата каждой ставки на нее:

```text
payout_i = floor(stake_i * k_h)
```

Суммарно для победившей лошади:

```text
total_payout_h ~= S_h + (P - S_h) / 1.1
```

Ожидаемый hold до округления примерно:

```text
house_hold_h = P - total_payout_h
             = P - S_h - (P - S_h) / 1.1
             = (P - S_h) / 11
```

Hold зависит от того, какая лошадь победила и сколько на нее поставили. Если победит фаворит с большим `S_h`, hold меньше; если победит андердог, hold больше.

Победитель генерируется в `SpeedGenerator.GenPlaces(HorseCount)`. Для полного EV по horse нужно отдельно проверить распределение победителей в генераторе.

## 4. Meta progression

### 4.1. XP за игру

Формула читается из `meta_seasons.config.xp`. Если JSON отсутствует или битый, используются дефолты:

```text
play = 5
win = 25
loss = 2
stakeMultiplier = 0.01
minXpPerGame = 1
maxXpPerGame = 500
```

Расчет:

```text
base_xp = is_win ? config.xp.win : config.xp.loss
stake_xp = floor(max(0, stake) * config.xp.stakeMultiplier)
xp_delta = clamp(config.xp.play + base_xp + stake_xp, minXpPerGame, maxXpPerGame)
```

Для дефолта:

```text
win:  xp = clamp(30 + floor(0.01 * stake), 1, 500)
loss: xp = clamp(7  + floor(0.01 * stake), 1, 500)
```

### 4.2. Rating

Rating читается из `meta_seasons.config.rating`:

```text
enabled = true
start = 1000
winDelta = 16
lossDelta = -12
```

Расчет:

```text
rating_delta = enabled ? (is_win ? winDelta : lossDelta) : 0
rating_after = max(0, rating_before + rating_delta)
```

Стартовый rating при первой записи:

```text
rating_initial = max(0, start + rating_delta)
```

### 4.3. Level curve

Level curve читается из `meta_seasons.config.levels`:

```text
xpPerLevelSquaredBase = 100
```

```text
level(xp) = max(1, floor(sqrt(xp / xpPerLevelSquaredBase)) + 1)
xp_floor(level) = xpPerLevelSquaredBase * (level - 1)^2
```

Примеры:

| level | minimum XP |
|---:|---:|
| 1 | 0 |
| 2 | 100 |
| 3 | 400 |
| 4 | 900 |
| 5 | 1600 |
| 10 | 8100 |

### 4.4. Seasons

Текущая сезонная модель:

```text
DefaultDurationDays = 14
DefaultPreparedSeasonCount = 30
```

Runtime поведение:

1. истекший active season переводится в `finished`;
2. planned season, который покрывает `now`, переводится в `active`;
3. если подходящего planned нет, создается новый active season на 14 дней;
4. очередь planned seasons дозаполняется до 30 будущих сезонов.

`SeasonPlanFactory` генерирует разные JSON-config темы:

```text
balanced
quest_rush
high_roller
clan_wars
tournament_arc
jackpot_hunt
streak_sprint
risk_watch
```

XP, rating start, rating delta и level curve применяются из JSON-конфига сезона.

Season rewards тоже читаются из JSON-конфига сезона:

```text
rewards.playerTop = [5000, 2500, 1000]
rewards.clanTop   = [10000, 5000, 2500]
```

Фактическая выплата за место:

```text
reward(place) =
  rewards[place - 1], если place в пределах массива
  0, иначе
```

Автоматический rollover выполняет тот же lifecycle, что и ручная активация: истекший active season закрывается, подходящий planned season активируется, очередь будущих сезонов дозаполняется. Награды finished-сезонов обрабатываются идемпотентно через operation id, поэтому повтор job не должен удваивать выплаты.

## 5. Quest rotation

Каталог квестов хранится в `games/Games.Meta/Infrastructure/Catalog/quest-pool.json` и разворачивается в набор `QuestTemplate`.

Активная ротация выбирается детерминированно:

```text
seed = season_id : chat_id : user_id : period_key : slot_id : index
```

Периоды:

```text
daily  -> yyyy-MM-dd
weekly -> ISO yyyy-Www
```

Выбор weighted:

```text
common    -> 100
uncommon  -> 60
rare      -> 25
epic      -> 10
legendary -> 4
```

`meta_seasons.config.quests.focus` сужает candidate pool, если в каталоге есть подходящие квесты. Поддерживаемые практические фокусы:

```text
all-round / balanced / normal -> без сужения
daily / weekly                -> квесты указанного периода
volume                        -> volume/high_stake
payout                        -> payout/profit/multiplier
streaks                       -> streak tags/clusters
clans                         -> clan tags
tournaments                   -> tournament tags
controlled                    -> low_stake/play/loss
```

`meta_seasons.config.quests.rarityBias` меняет эффективный вес:

```text
normal   -> без изменения
uncommon -> uncommon * 2
rare     -> rare/epic/legendary * 3
epic     -> epic/legendary * 4
common   -> common * 2, остальные / 2
```

Это не отменяет progress gate: редкие или тяжелые квесты все равно не попадут новичку, если у них задан `MinLevel`, `MinGamesPlayed` или `MinTotalStaked`.

Повторяемость режется через:

- запрет одинакового quest id в одной активной выдаче;
- запрет одинакового repeat key в одной активной выдаче;
- soft block по прошлым периодам на глубину `RepeatCooldownPeriods`.

Progress gate:

```text
quest unlocked iff
  player.level >= MinLevel
  and player.games_played >= MinGamesPlayed
  and player.total_staked >= MinTotalStaked
```

Quest progress delta:

```text
volume -> stake
payout -> payout
profit -> max(0, payout - stake)
other  -> 1
```

Condition filters:

```text
MinStake
MaxStake
MinPayout
MinProfit
MinMultiplier
```

## 6. Faucets, sinks и ожидаемый drift

### 6.1. Основные sinks

- отрицательное EV dice games;
- transfer fee;
- pick/pick lottery rake;
- horse pool hold;
- gas tax на slots;
- возможные admin/economy операции, если используются вручную.

### 6.2. Основные faucets

- daily bonus;
- quest rewards: XP и coins;
- season rewards;
- clan rewards;
- redeem drops;
- Pick streak bonus при длинных сериях.

### 6.3. Условная модель drift

Для периода `T`:

```text
DeltaSupply(T) =
  sum_game_payouts(T)
  + sum_daily_bonus(T)
  + sum_quest_coin_rewards(T)
  + sum_season_rewards(T)
  + sum_redeem_rewards(T)
  - sum_stakes_debited(T)
  - sum_transfer_fees(T)
  - sum_lottery_fees(T)
```

По играм с RTP:

```text
EV[DeltaSupply_game] = -sum_stakes * edge
```

Если надо удерживать стабильную экономику, faucet budget должен быть не выше ожидаемого sink:

```text
daily_bonus + quest_coins + season_rewards + redeem_value
  <= expected_house_edge + transfer_fees + lottery_fees
```

Админская страница `/admin/meta-economy` считает rough simulation для выбранных `days`, `players`, среднего stake/RTP и сезонных reward pool, а рядом показывает фактический ledger drift по `economics_ledger`. Это быстрый sanity check, не Monte Carlo.


## 7. Быстрый баланс-свод

| Модуль | RTP / эффект | Edge / sink |
|---|---:|---:|
| DiceCube | 83.3333% | 16.6667% |
| Darts | 83.3333% | 16.6667% |
| Bowling | 83.3333% | 16.6667% |
| Football | 80.0000% | 20.0000% |
| Basketball | 80.0000% | 20.0000% |
| Slots default | 95.3125% | 4.6875% |
| Pick base | около 95% | около 5% |
| Pick lottery | около 95% | около 5% |
| Horse | зависит от pool | примерно `(P - S_winner) / 11` |
| Transfer | нет RTP | комиссия 3%, минимум 1 |
| Daily bonus | faucet | до 8 монет/день |
