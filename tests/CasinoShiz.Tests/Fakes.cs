using BotFramework.Host;
using BotFramework.Sdk;
using Games.Admin;
using Games.Blackjack;
using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.DiceCube;
using Games.Dice;
using Games.Horse;
using Games.Leaderboard;
using Games.Redeem;

namespace CasinoShiz.Tests;

sealed class FakeEconomicsService : IEconomicsService
{
    private readonly Dictionary<long, int> _balances = new();
    public int StartingBalance { get; init; } = 1_000;
    public List<(long UserId, int Amount, string Reason)> Debits { get; } = [];
    public List<(long UserId, int Amount, string Reason)> Credits { get; } = [];

    public int GetCurrentBalance(long userId) => _balances.GetValueOrDefault(userId, StartingBalance);

    public Task EnsureUserAsync(long userId, string displayName, CancellationToken ct) => Task.CompletedTask;

    public Task<int> GetBalanceAsync(long userId, CancellationToken ct) =>
        Task.FromResult(_balances.GetValueOrDefault(userId, StartingBalance));

    public Task<bool> TryDebitAsync(long userId, int amount, string reason, CancellationToken ct)
    {
        var bal = _balances.GetValueOrDefault(userId, StartingBalance);
        if (amount > bal) return Task.FromResult(false);
        _balances[userId] = bal - amount;
        Debits.Add((userId, amount, reason));
        return Task.FromResult(true);
    }

    public Task DebitAsync(long userId, int amount, string reason, CancellationToken ct)
    {
        _balances[userId] = _balances.GetValueOrDefault(userId, StartingBalance) - amount;
        Debits.Add((userId, amount, reason));
        return Task.CompletedTask;
    }

    public Task CreditAsync(long userId, int amount, string reason, CancellationToken ct)
    {
        _balances[userId] = _balances.GetValueOrDefault(userId, StartingBalance) + amount;
        Credits.Add((userId, amount, reason));
        return Task.CompletedTask;
    }
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

    public Task InsertAsync(BlackjackHandRow hand, CancellationToken ct)
    {
        _hands[hand.UserId] = hand;
        return Task.CompletedTask;
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

    public Task InsertAsync(BasketballBet bet, CancellationToken ct)
    {
        _bets[(bet.UserId, bet.ChatId)] = bet;
        return Task.CompletedTask;
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

    public Task InsertAsync(BowlingBet bet, CancellationToken ct)
    {
        _bets[(bet.UserId, bet.ChatId)] = bet;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        _bets.Remove((userId, chatId));
        return Task.CompletedTask;
    }
}

sealed class InMemoryDartsBetStore : IDartsBetStore
{
    private readonly Dictionary<(long, long), DartsBet> _bets = new();

    public Task<DartsBet?> FindAsync(long userId, long chatId, CancellationToken ct) =>
        Task.FromResult(_bets.GetValueOrDefault((userId, chatId)));

    public Task InsertAsync(DartsBet bet, CancellationToken ct)
    {
        _bets[(bet.UserId, bet.ChatId)] = bet;
        return Task.CompletedTask;
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

    public Task InsertAsync(DiceCubeBet bet, CancellationToken ct)
    {
        _bets[(bet.UserId, bet.ChatId)] = bet;
        return Task.CompletedTask;
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
}

sealed class InMemoryHorseResultStore : IHorseResultStore
{
    private readonly Dictionary<string, HorseResultRow> _results = new();

    public Task<HorseResultRow?> FindAsync(string raceDate, CancellationToken ct) =>
        Task.FromResult(_results.GetValueOrDefault(raceDate));

    public Task UpsertAsync(HorseResultRow result, CancellationToken ct)
    {
        _results[result.RaceDate] = result;
        return Task.CompletedTask;
    }
}

// ── Leaderboard ──────────────────────────────────────────────────────────────

sealed class InMemoryLeaderboardStore : ILeaderboardStore
{
    private readonly List<(long UserId, string Name, int Coins, long UpdatedAtMs)> _users = [];

    public void Seed(long userId, string name, int coins, long updatedAtMs) =>
        _users.Add((userId, name, coins, updatedAtMs));

    public Task<IReadOnlyList<LeaderboardUser>> ListActiveAsync(long sinceUnixMs, CancellationToken ct)
    {
        var active = _users
            .Where(u => u.UpdatedAtMs >= sinceUnixMs)
            .OrderByDescending(u => u.Coins)
            .Select(u => new LeaderboardUser(u.UserId, u.Name, u.Coins, u.UpdatedAtMs))
            .ToList();
        return Task.FromResult<IReadOnlyList<LeaderboardUser>>(active);
    }

    public Task<(int Coins, long UpdatedAtUnixMs)?> FindAsync(long userId, CancellationToken ct)
    {
        var u = _users.LastOrDefault(x => x.UserId == userId);
        if (u == default) return Task.FromResult<(int, long)?>(null);
        return Task.FromResult<(int, long)?>((u.Coins, u.UpdatedAtMs));
    }
}

// ── Admin ─────────────────────────────────────────────────────────────────────

sealed class InMemoryAdminStore(FakeEconomicsService? econ = null) : IAdminStore
{
    private readonly Dictionary<long, UserSummary> _users = new();
    private readonly Dictionary<string, string> _overrides = new();

    public void Seed(UserSummary user) => _users[user.TelegramUserId] = user;

    public Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UserSummary>>([.. _users.Values.OrderByDescending(u => u.Coins)]);

    public Task<UserSummary?> FindUserAsync(long userId, CancellationToken ct)
    {
        if (!_users.TryGetValue(userId, out var u)) return Task.FromResult<UserSummary?>(null);
        // Reflect latest balance from the economics service if linked
        if (econ != null) u = u with { Coins = econ.GetCurrentBalance(userId) };
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
