using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;
using Games.Basketball.Application.Wagering;
using Games.Bowling.Application.Wagering;
using Games.Darts.Application.Wagering;
using Games.Dice.Application.Wagering;
using Games.DiceCube.Application.Wagering;
using Games.Football.Application.Wagering;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class WagerGameBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AllSimpleWagerGamesResolveWithoutEconomyInGameDecision()
    {
        var cases = new (string GameId, string Input)[]
        {
            ("dice", "{\"diceValue\":1}"),
            ("dicecube", "{\"face\":1}"),
            ("darts", "{\"face\":1}"),
            ("football", "{\"face\":1}"),
            ("basketball", "{\"face\":1}"),
            ("bowling", "{\"face\":1}"),
        };
        var action = new WagerGameAction(
        [
            new DiceWagerResolver(),
            new DiceCubeWagerResolver(),
            new DartsWagerResolver(),
            new FootballWagerResolver(),
            new BasketballWagerResolver(),
            new BowlingWagerResolver(),
        ]);

        foreach (var (gameId, gameInput) in cases)
        {
            var command = new WagerGameCommand(
                $"op-{gameId}", $"bet-{gameId}", gameId, "play", "player-1", 42,
                "player", gameInput, 0, $"command-{gameId}", Now);
            var state = new WagerGameState(0, gameId, command.BetId, command.PlayerId, null, null);
            var decision = action.Decide(new GameActionInput<WagerGameState, WagerGameCommand>(
                command,
                state,
                new WalletSnapshot(0),
                new Dictionary<string, QuotaSnapshot>(),
                EntropyValue.Empty,
                Now));

            Assert.Equal(DecisionStatus.Accepted, decision.Status);
            Assert.Empty(decision.Economy);
        Assert.Empty(decision.CustomEffects ?? []);
            Assert.Equal("played", decision.Result!.OutcomeCode);
            Assert.IsType<WagerGameOutcomeDeclared>(Assert.Single(decision.Events));
            Assert.DoesNotContain("Stake", JsonSerializer.Serialize(decision.NewState));
            Assert.DoesNotContain("Balance", JsonSerializer.Serialize(decision.NewState));
            Assert.DoesNotContain("Payout", JsonSerializer.Serialize(decision.NewState));
        }
    }
}
