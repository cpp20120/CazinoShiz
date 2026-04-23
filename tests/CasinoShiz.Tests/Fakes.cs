using BotFramework.Host;
using BotFramework.Sdk;
using Games.Admin;
using Games.Blackjack;
using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.Football;
using Games.DiceCube;
using Games.Dice;
using Games.Horse;
using Games.Leaderboard;
using Games.Redeem;

namespace CasinoShiz.Tests;

sealed class FakeEconomicsService : IEconomicsService
{
    private readonly Dictionary<(long UserId, long ScopeId), int> _balances = new();
    public int StartingBalance { get; init; } = 1_000;
    public List<(long UserId, long ScopeId, int Amount, string Reason)> Debits { get; } = [];
    public List<(long UserId, long ScopeId, int Amount, string Reason)> Credits { get; } = [];

    public int GetCurrentBalance(long userId, long balanceScopeId) =>
        _balances.GetValueOrDefault((userId, balanceScopeId), StartingBalance);

    public Task EnsureUserAsync(long userId, long balanceScopeId, string displayName, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<int> GetBalanceAsync(long userId, long balanceScopeId, CancellationToken ct) =>
        Task.FromResult(_balances.GetValueOrDefault((userId, balanceScopeId), StartingBalance));

    public Task<bool> TryDebitAsync(
        long userId, long balanceScopeId, int amount, string reason, CancellationToken ct)
    {
        var key = (userId, balanceScopeId);
        var bal = _balances.GetValueOrDefault(key, StartingBalance);
        if (amount > bal) return Task.FromResult(false);
        _balances[key] = bal - amount;
        Debits.Add((userId, balanceScopeId, amount, reason));
        return Task.FromResult(true);
    }

    public Task DebitAsync(long userId, long balanceScopeId, int amount, string reason, CancellationToken ct)
    {
        var key = (userId, balanceScopeId);
        _balances[key] = _balances.GetValueOrDefault(key, StartingBalance) - amount;
        Debits.Add((userId, balanceScopeId, amount, reason));
        return Task.CompletedTask;
    }

    public Task CreditAsync(long userId, long balanceScopeId, int amount, string reason, CancellationToken ct)
    {
        var key = (userId, balanceScopeId);
        _balances[key] = _balances.GetValueOrDefault(key, StartingBalance) + amount;
        Credits.Add((userId, balanceScopeId, amount, reason));
        return Task.CompletedTask;
    }

    public Task AdjustUncheckedAsync(long userId, long balanceScopeId, int delta, CancellationToken ct)
    {
        var key = (userId, balanceScopeId);
        _balances[key] = _balances.GetValueOrDefault(key, StartingBalance) + delta;
        return Task.CompletedTask;
    }

    public Task<LedgerRevertResult> RevertLedgerEntryAsync(long economicsLedgerId, CancellationToken ct) =>
        Task.FromResult(new LedgerRevertResult(LedgerRevertStatus.NotFound, 0));
}

sealed class NullAnalyticsService : IAnalyticsService
{
    public void Track(string moduleId, string eventName, IReadOnlyDictionary<string, object?> tags) { }
}

sealed class NullEventBus : IDomainEventBus
{
    public List<IDomainEvent> Published { get; } = [];
    public Task PublishAsync(IDomainEvent ev, CancellationToken ct) { Published.Add(ev); return Task.CompletedTask; }
    public void Subscribe(string eventTypePattern, IDomainEventSubscriber subscriber) { }
}

sealed class NullDiceHistoryStore : IDiceHistoryStore
{
    public Task AppendAsync(DiceRoll roll, CancellationToken ct) => Task.CompletedTask;
}

sealed class InMemoryBlackjackHandStore : IBlackjackHandStore
{
    private readonly Dictionary<long, BlackjackHandRow> _hands = new();

    public Task<BlackjackHandRow?> FindAsync(long userId, CancellationToken ct) =>
        Task.FromResult(_hands.GetValueOrDefault(userId));

    public Task<bool> InsertAsync(BlackjackHandRow hand, CancellationToken ct)
    {
        if (_hands.ContainsKey(hand.UserId)) return Task.FromResult(false);
        _hands[hand.UserId] = hand;
        return Task.FromResult(true);
    }

    public Task UpdateAsync(BlackjackHandRow hand, CancellationToken ct)
    {
        _hands[hand.UserId] = hand;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(long userId, CancellationToken ct)
    {
        _hands.Remove(userId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<long>> ListStuckUserIdsAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<long>>([.. _hands.Keys]);

    public Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct)
    {
        if (_hands.TryGetValue(userId, out var h))
            _hands[userId] = h with { StateMessageId = messageId };
        return Task.CompletedTask;
    }
}

sealed class InMemoryBasketballBetStore : IBasketballBetStore
{
    private readonly Dictionary<(long, long), BasketballBet> _bets = new();

    public Task<BasketballBet?> FindAsync(long userId, long chatId, CancellationToken ct) =>
        Task.FromResult(_bets.GetValueOrDefault((userId, chatId)));

    public Task<bool> InsertAsync(BasketballBet bet, CancellationToken ct)
    {
        if (_bets.ContainsKey((bet.UserId, bet.ChatId))) return Task.FromResult(false);
        _bets[(bet.UserId, bet.ChatId)] = bet;
        return Task.FromResult(true);
    }

    public Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        _bets.Remove((userId, chatId));
        return Task.CompletedTask;
    }
}

sealed class InMemoryBowlingBetStore : IBowlingBetStore
{
    private readonly Dictionary<(long, long), BowlingBet> _bets = new();

    public Task<BowlingBet?> FindAsync(long userId, long chatId, CancellationToken ct) =>
        Task.FromResult(_bets.GetValueOrDefault((userId, chatId)));

    public Task<bool> InsertAsync(BowlingBet bet, CancellationToken ct)
    {
        if (_bets.ContainsKey((bet.UserId, bet.ChatId))) return Task.FromResult(false);
        _bets[(bet.UserId, bet.ChatId)] = bet;
        return Task.FromResult(true);
    }

    public Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        _bets.Remove((userId, chatId));
        return Task.CompletedTask;
    }
}

sealed class InMemoryDartsRoundStore : IDartsRoundStore
{
    private readonly Dictionary<long, DartsRound> _rounds = new();
    private long _nextId = 1;

    public Task<long> InsertQueuedAsync(DartsRound row, CancellationToken ct)
    {
        var id = _nextId++;
        var withId = row with { Id = id };
        _rounds[id] = withId;
        return Task.FromResult(id);
    }

    public Task<DartsRound?> FindByIdAsync(long roundId, CancellationToken ct) =>
        Task.FromResult(_rounds.GetValueOrDefault(roundId));

    public Task<bool> TryMarkAwaitingOutcomeAsync(long roundId, int botMessageId, CancellationToken ct)
    {
        if (!_rounds.TryGetValue(roundId, out var row) || row.Status != DartsRoundStatus.Queued)
            return Task.FromResult(false);
        _rounds[roundId] = row with { Status = DartsRoundStatus.AwaitingOutcome, BotMessageId = botMessageId };
        return Task.FromResult(true);
    }

    public Task DeleteAsync(long roundId, CancellationToken ct)
    {
        _rounds.Remove(roundId);
        return Task.CompletedTask;
    }

    public Task<int> CountRollsAheadInChatAsync(long chatId, long roundId, CancellationToken ct)
    {
        var n = _rounds.Values.Count(r =>
            r.ChatId == chatId && r.Id < roundId
            && (r.Status == DartsRoundStatus.Queued || r.Status == DartsRoundStatus.AwaitingOutcome));
        return Task.FromResult(n);
    }

    public Task<int> CountActiveByUserChatAsync(long userId, long chatId, CancellationToken ct)
    {
        var n = _rounds.Values.Count(r =>
            r.UserId == userId && r.ChatId == chatId
            && (r.Status == DartsRoundStatus.Queued || r.Status == DartsRoundStatus.AwaitingOutcome));
        return Task.FromResult(n);
    }
}

sealed class InMemoryFootballBetStore : IFootballBetStore
{
    private readonly Dictionary<(long, long), FootballBet> _bets = new();

    public Task<FootballBet?> FindAsync(long userId, long chatId, CancellationToken ct) =>
        Task.FromResult(_bets.GetValueOrDefault((userId, chatId)));

    public Task<bool> InsertAsync(FootballBet bet, CancellationToken ct)
    {
        if (_bets.ContainsKey((bet.UserId, bet.ChatId))) return Task.FromResult(false);
        _bets[(bet.UserId, bet.ChatId)] = bet;
        return Task.FromResult(true);
    }

    public Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        _bets.Remove((userId, chatId));
        return Task.CompletedTask;
    }
}

sealed class InMemoryDiceCubeBetStore : IDiceCubeBetStore
{
    private readonly Dictionary<(long, long), DiceCubeBet> _bets = new();

    public Task<DiceCubeBet?> FindAsync(long userId, long chatId, CancellationToken ct) =>
        Task.FromResult(_bets.GetValueOrDefault((userId, chatId)));

    public Task<bool> InsertAsync(DiceCubeBet bet, CancellationToken ct)
    {
        if (_bets.ContainsKey((bet.UserId, bet.ChatId))) return Task.FromResult(false);
        _bets[(bet.UserId, bet.ChatId)] = bet;
        return Task.FromResult(true);
    }

    public Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        _bets.Remove((userId, chatId));
        return Task.CompletedTask;
    }
}

sealed class InMemoryHorseBetStore : IHorseBetStore
{
    private readonly List<HorseBetRow> _bets = [];

    public Task<IReadOnlyList<HorseBetRow>> ListByRaceDateAsync(string raceDate, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HorseBetRow>>(_bets.Where(b => b.RaceDate == raceDate).ToList());

    public Task<IReadOnlyList<HorseBetRow>> ListByRaceDateAndScopeAsync(
        string raceDate, long balanceScopeId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HorseBetRow>>(_bets
            .Where(b => b.RaceDate == raceDate && b.BalanceScopeId == balanceScopeId).ToList());

    public Task InsertAsync(HorseBetRow bet, CancellationToken ct)
    {
        _bets.Add(bet);
        return Task.CompletedTask;
    }

    public Task DeleteByRaceDateAsync(string raceDate, CancellationToken ct)
    {
        _bets.RemoveAll(b => b.RaceDate == raceDate);
        return Task.CompletedTask;
    }

    public Task DeleteByRaceDateAndScopeAsync(string raceDate, long balanceScopeId, CancellationToken ct)
    {
        _bets.RemoveAll(b => b.RaceDate == raceDate && b.BalanceScopeId == balanceScopeId);
        return Task.CompletedTask;
    }
}

sealed class InMemoryHorseResultStore : IHorseResultStore
{
    private readonly Dictionary<(string RaceDate, long Scope), HorseResultRow> _results = new();

    public Task<HorseResultRow?> FindAsync(string raceDate, long balanceScopeId, CancellationToken ct) =>
        Task.FromResult(_results.GetValueOrDefault((raceDate, balanceScopeId)));

    public Task UpsertAsync(HorseResultRow result, CancellationToken ct)
    {
        _results[(result.RaceDate, result.BalanceScopeId)] = result;
        return Task.CompletedTask;
    }

    public Task SaveFileIdAsync(string raceDate, long balanceScopeId, string fileId, CancellationToken ct)
    {
        var key = (raceDate, balanceScopeId);
        if (_results.TryGetValue(key, out var r))
            _results[key] = r with { FileId = fileId };
        return Task.CompletedTask;
    }
}

// ── Leaderboard ──────────────────────────────────────────────────────────────

sealed class InMemoryLeaderboardStore : ILeaderboardStore
{
    private readonly List<(long UserId, long ScopeId, string Name, int Coins, long UpdatedAtMs)> _users = [];

    public void Seed(long userId, long scopeId, string name, int coins, long updatedAtMs) =>
        _users.Add((userId, scopeId, name, coins, updatedAtMs));

    public Task<IReadOnlyList<LeaderboardUser>> ListActiveAsync(
        long sinceUnixMs, long balanceScopeId, CancellationToken ct)
    {
        var active = _users
            .Where(u => u.ScopeId == balanceScopeId && u.UpdatedAtMs >= sinceUnixMs)
            .OrderByDescending(u => u.Coins)
            .Select(u => new LeaderboardUser(u.UserId, u.ScopeId, u.Name, u.Coins, u.UpdatedAtMs))
            .ToList();
        return Task.FromResult<IReadOnlyList<LeaderboardUser>>(active);
    }

    public Task<(int Coins, long UpdatedAtUnixMs)?> FindAsync(
        long userId, long balanceScopeId, CancellationToken ct)
    {
        var u = _users.LastOrDefault(x => x.UserId == userId && x.ScopeId == balanceScopeId);
        if (u == default) return Task.FromResult<(int, long)?>(null);
        return Task.FromResult<(int, long)?>((u.Coins, u.UpdatedAtMs));
    }
}

// ── Admin ─────────────────────────────────────────────────────────────────────

sealed class InMemoryAdminStore(FakeEconomicsService? econ = null) : IAdminStore
{
    private readonly Dictionary<(long UserId, long ScopeId), UserSummary> _users = new();
    private readonly Dictionary<string, string> _overrides = new();

    public void Seed(UserSummary user) => _users[(user.TelegramUserId, user.BalanceScopeId)] = user;

    public Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UserSummary>>([.. _users.Values.OrderByDescending(u => u.Coins)]);

    public Task<UserSummary?> FindUserAsync(long userId, long balanceScopeId, CancellationToken ct)
    {
        if (!_users.TryGetValue((userId, balanceScopeId), out var u)) return Task.FromResult<UserSummary?>(null);
        if (econ != null) u = u with { Coins = econ.GetCurrentBalance(userId, balanceScopeId) };
        return Task.FromResult<UserSummary?>(u);
    }

    public Task<string?> GetOverrideAsync(string originalName, CancellationToken ct) =>
        Task.FromResult(_overrides.GetValueOrDefault(originalName));

    public Task UpsertOverrideAsync(string originalName, string newName, CancellationToken ct)
    {
        _overrides[originalName] = newName;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteOverrideAsync(string originalName, CancellationToken ct)
    {
        var removed = _overrides.Remove(originalName);
        return Task.FromResult(removed);
    }
}

// ── Redeem ───────────────────────────────────────────────────────────────────

sealed class InMemoryRedeemStore : IRedeemStore
{
    private readonly Dictionary<Guid, RedeemCode> _codes = new();

    public Task<RedeemCode?> FindAsync(Guid code, CancellationToken ct) =>
        Task.FromResult(_codes.GetValueOrDefault(code));

    public Task InsertAsync(RedeemCode code, CancellationToken ct)
    {
        _codes[code.Code] = code;
        return Task.CompletedTask;
    }

    public Task<bool> MarkRedeemedAsync(Guid code, long redeemedBy, long redeemedAt, CancellationToken ct)
    {
        if (!_codes.TryGetValue(code, out var c) || !c.Active)
            return Task.FromResult(false);
        c.Active = false;
        c.RedeemedBy = redeemedBy;
        c.RedeemedAt = redeemedAt;
        return Task.FromResult(true);
    }
}
