using BotFramework.Sdk.Execution.Boards;
using BotFramework.Sdk.Execution.Cards;
using BotFramework.Sdk.Execution;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class CardAndBoardPrimitivesTests
{
    [Fact]
    public void Deck_DrawsFromTopAndHandsKeepCardIdentityUnique()
    {
        var ace = new Card<string, string>("AS", "ace of spades");
        var king = new Card<string, string>("KS", "king of spades");
        var queen = new Card<string, string>("QS", "queen of spades");
        var deck = new Deck<string, string>([ace, king, queen]);

        var draw = deck.Draw(2);
        var hand = new Hand<string, string>(draw.Cards).Add([queen]);
        var shuffled = deck.Shuffle(
            new EntropyGameRandom(new EntropyValue(
            [KeyValuePair.Create("shuffle:0", 0.4), KeyValuePair.Create("shuffle:1", 0.1)])),
            "shuffle");

        Assert.Equal(["AS", "KS"], draw.Cards.Select(card => card.Id));
        Assert.Equal(["QS"], draw.RemainingDeck.Cards.Select(card => card.Id));
        Assert.Equal(["AS", "KS", "QS"], hand.Cards.Select(card => card.Id));
        Assert.Equal(["AS", "QS"], hand.Remove("KS").Cards.Select(card => card.Id));
        Assert.Equal(["QS", "AS", "KS"], shuffled.Cards.Select(card => card.Id));
        Assert.Throws<ArgumentException>(() => hand.Add([ace]));
        Assert.Throws<InvalidOperationException>(() => deck.Draw(4));
    }

    [Fact]
    public void GridBoard_RejectsStaleAndBlockedMovesAndSupportsExplicitCapture()
    {
        var bounds = new GridBounds(3, 3);
        var knight = new BoardPiece<string, string>("white-knight", "knight", new(0, 0));
        var pawn = new BoardPiece<string, string>("black-pawn", "pawn", new(1, 2));
        var board = new GridBoard<string, string>(bounds, []).WithPiece(knight).WithPiece(pawn);
        var move = new BoardMove<string>("white-knight", new(0, 0), new(1, 2));

        var blocked = board.TryMove(move);
        var captured = board.TryMove(move, BoardMoveMode.CaptureDestination);
        var stale = captured.Board.TryMove(new("white-knight", new(0, 0), new(2, 1)));

        Assert.Equal(BoardMoveStatus.DestinationOccupied, blocked.Status);
        Assert.Same(board, blocked.Board);
        Assert.True(captured.Applied);
        Assert.Equal("black-pawn", captured.CapturedPiece!.Id);
        Assert.True(captured.Board.TryGetPieceAt(new(1, 2), out var moved));
        Assert.Equal("white-knight", moved!.Id);
        Assert.False(captured.Board.TryGetPiece("black-pawn", out _));
        Assert.Equal(BoardMoveStatus.SourceMismatch, stale.Status);
    }

    [Fact]
    public void GridPositionAndBounds_ModelGridGeometry()
    {
        var origin = new GridPosition(1, 1);
        var northEast = origin.Move(GridDirection.NorthEast);
        var bounds = new GridBounds(3, 3);

        Assert.Equal(new GridPosition(0, 2), northEast);
        Assert.True(origin.IsAdjacentTo(northEast));
        Assert.True(origin.IsOrthogonallyAdjacentTo(new GridPosition(2, 1)));
        Assert.Equal(2, origin.ManhattanDistanceTo(new GridPosition(2, 2)));
        Assert.True(bounds.Contains(northEast));
        Assert.False(bounds.Contains(new GridPosition(-1, 0)));
        Assert.Equal(9, bounds.Positions().Count());
    }
}
