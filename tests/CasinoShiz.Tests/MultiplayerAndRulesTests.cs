using BotFramework.Presentation;
using BotFramework.Sdk.Execution;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class MultiplayerAndRulesTests
{
    [Fact]
    public void Lobby_ManagesSeatsReadinessTeamsRolesAndStart()
    {
        var lobby = new LobbyEngine<long>(new(2, 2, maximumSpectators: 1));
        var empty = lobby.Create();
        var alice = lobby.JoinPlayer(empty, 10, requestedSeat: 0, teamId: "red");
        var bob = lobby.JoinPlayer(alice.State, 20, requestedSeat: 1, teamId: "blue");
        var spectator = lobby.JoinSpectator(bob.State, 30);
        var readyAlice = lobby.SetReady(spectator.State, 10);
        var readyBob = lobby.SetReady(readyAlice.State, 20);
        var started = lobby.Start(readyBob.State);
        var role = lobby.GrantRole(started.State, 10, "seer");

        Assert.True(started.Applied);
        Assert.Equal(LobbyStatus.Started, started.State.Status);
        Assert.Equal([0, 1], started.State.Players.Select(player => player.Seat).ToArray());
        Assert.Single(started.State.Spectators);
        Assert.True(role.Applied);
        Assert.Equal(["seer"], role.State.Members.Single(member => member.PlayerId == 10).Roles);

        var resolver = new LobbyAudienceResolver<long>(static id => id.ToString());
        Assert.Equal([10L], resolver.Resolve(role.State, GameAudience.Team("red")).Recipients);
        Assert.Equal([10L], resolver.Resolve(role.State, GameAudience.Role("seer")).Recipients);
        Assert.Equal([30L], resolver.Resolve(role.State, GameAudience.Spectators).Recipients);
        Assert.True(resolver.CanView(role.State, 10, GameAudience.Role("seer")));
        Assert.False(resolver.CanView(role.State, 20, GameAudience.Role("seer")));

        var target = new PresentationAddress("table:1", audience: GameAudience.Role("seer"));
        Assert.Equal(GameAudienceKind.Role, target.Audience!.Kind);
        Assert.Null(target.RecipientId);
    }

    [Fact]
    public void Lobby_RejectsEarlyStartCapacityOverflowAndMutationAfterStart()
    {
        var lobby = new LobbyEngine<string>(new(2, 2, requireAllPlayersReady: true, maximumSpectators: 0));
        var one = lobby.JoinPlayer(lobby.Create(), "alice");
        var early = lobby.Start(one.State);
        var two = lobby.JoinPlayer(one.State, "bob");
        var unready = lobby.Start(two.State);
        var ready = lobby.SetReady(lobby.SetReady(two.State, "alice").State, "bob");
        var started = lobby.Start(ready.State);
        var overflow = lobby.JoinSpectator(two.State, "viewer");
        var leave = lobby.Leave(started.State, "alice");

        Assert.Equal("insufficient_players", early.Rejection!.Code);
        Assert.Equal("players_not_ready", unready.Rejection!.Code);
        Assert.True(started.Applied);
        Assert.Equal("spectator_limit_reached", overflow.Rejection!.Code);
        Assert.Equal("lobby_not_open", leave.Rejection!.Code);
    }

    [Fact]
    public void Matchmaking_FormsTheEarliestCompatibleFifoMatchAndKeepsOtherQueues()
    {
        var matchmaking = new MatchmakingEngine<string, string>();
        var queued = matchmaking.Enqueue(matchmaking.Create(), "alice", "casual", DateTimeOffset.UnixEpoch);
        queued = matchmaking.Enqueue(queued.State, "viewer", "ranked", DateTimeOffset.UnixEpoch.AddSeconds(1));
        queued = matchmaking.Enqueue(queued.State, "bob", "casual", DateTimeOffset.UnixEpoch.AddSeconds(2));
        queued = matchmaking.Enqueue(queued.State, "carol", "casual", DateTimeOffset.UnixEpoch.AddSeconds(3));

        var formed = matchmaking.TryFormMatch(queued.State, minimumPlayers: 2, maximumPlayers: 2);
        var duplicate = matchmaking.Enqueue(formed.State, "viewer", "ranked", DateTimeOffset.UnixEpoch.AddSeconds(4));

        Assert.True(formed.Applied);
        Assert.Equal("casual", formed.Match!.MatchKey);
        Assert.Equal(["alice", "bob"], formed.Match.Players);
        Assert.Equal(["viewer", "carol"], formed.State.Tickets.Select(ticket => ticket.PlayerId));
        Assert.Equal("player_already_queued", duplicate.Rejection!.Code);
        Assert.Equal(MatchmakingTransitionStatus.NoMatch,
            matchmaking.TryFormMatch(formed.State, minimumPlayers: 2, maximumPlayers: 2).Status);
    }

    [Fact]
    public void RuleSet_ReturnsTheFirstStableRejectionInDeclaredOrder()
    {
        var rules = new GameRuleSet<RuleContext>(
        [
            GameRule.Require<RuleContext>("active", static context => context.Active, GameRuleRejection.GameNotActive),
            GameRule.Require<RuleContext>("member", static context => context.Member, GameRuleRejection.PlayerNotJoined),
            GameRule.Require<RuleContext>("turn", static context => context.Turn, GameRuleRejection.NotYourTurn),
        ]);

        var inactive = rules.Evaluate(new(false, false, false));
        var missing = rules.Evaluate(new(true, false, false));
        var turn = rules.Evaluate(new(true, true, false));
        var allowed = rules.Evaluate(new(true, true, true));

        Assert.Equal("game_not_active", inactive.Rejection!.Code);
        Assert.Equal("player_not_joined", missing.Rejection!.Code);
        Assert.Equal("not_your_turn", turn.Rejection!.Code);
        Assert.True(allowed.Allowed);
        Assert.Equal("table_locked", GameRuleRejection.Custom("table_locked").Code);
    }

    [Fact]
    public void PresentationAddress_RejectsAmbiguousDirectRecipientAndAudience()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new PresentationAddress("table:1", "alice", GameAudience.Participants));

        Assert.Contains("recipient", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RuleContext(bool Active, bool Member, bool Turn);
}
