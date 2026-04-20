using Games.Poker;
using Xunit;

namespace CasinoShiz.Tests;

public class PokerCommandParserTests
{
    // ── ParseText ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/poker")]
    [InlineData("/poker ")]
    public void ParseText_NoVerb_ReturnsUsage(string text)
    {
        Assert.IsType<PokerCommand.Usage>(PokerCommandParser.ParseText(text));
    }

    [Fact]
    public void ParseText_Create_ReturnsCreate()
    {
        Assert.IsType<PokerCommand.Create>(PokerCommandParser.ParseText("/poker create"));
    }

    [Fact]
    public void ParseText_Join_WithCode_ReturnsJoin()
    {
        var cmd = PokerCommandParser.ParseText("/poker join abc123");
        var join = Assert.IsType<PokerCommand.Join>(cmd);
        Assert.Equal("ABC123", join.Code);
    }

    [Fact]
    public void ParseText_Join_CodeIsUppercased()
    {
        var cmd = PokerCommandParser.ParseText("/poker join xyzcode");
        var join = Assert.IsType<PokerCommand.Join>(cmd);
        Assert.Equal("XYZCODE", join.Code);
    }

    [Fact]
    public void ParseText_Join_MissingCode_ReturnsMissingCode()
    {
        Assert.IsType<PokerCommand.JoinMissingCode>(PokerCommandParser.ParseText("/poker join"));
    }

    [Fact]
    public void ParseText_Start_ReturnsStart()
    {
        Assert.IsType<PokerCommand.Start>(PokerCommandParser.ParseText("/poker start"));
    }

    [Fact]
    public void ParseText_Leave_ReturnsLeave()
    {
        Assert.IsType<PokerCommand.Leave>(PokerCommandParser.ParseText("/poker leave"));
    }

    [Fact]
    public void ParseText_Status_ReturnsStatus()
    {
        Assert.IsType<PokerCommand.Status>(PokerCommandParser.ParseText("/poker status"));
    }

    [Fact]
    public void ParseText_Raise_WithAmount_ReturnsRaise()
    {
        var cmd = PokerCommandParser.ParseText("/poker raise 500");
        var raise = Assert.IsType<PokerCommand.Raise>(cmd);
        Assert.Equal(500, raise.Amount);
    }

    [Fact]
    public void ParseText_Raise_MissingAmount_ReturnsMissing()
    {
        Assert.IsType<PokerCommand.RaiseMissingAmount>(PokerCommandParser.ParseText("/poker raise"));
    }

    [Fact]
    public void ParseText_Raise_NonNumericAmount_ReturnsMissing()
    {
        Assert.IsType<PokerCommand.RaiseMissingAmount>(PokerCommandParser.ParseText("/poker raise abc"));
    }

    [Fact]
    public void ParseText_Unknown_ReturnsUnknown()
    {
        var cmd = PokerCommandParser.ParseText("/poker foobar");
        var unknown = Assert.IsType<PokerCommand.Unknown>(cmd);
        Assert.Equal("foobar", unknown.Action);
    }

    [Fact]
    public void ParseText_VerbIsLowercased()
    {
        Assert.IsType<PokerCommand.Create>(PokerCommandParser.ParseText("/poker CREATE"));
    }

    // ── ParseCallback ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not:poker")]
    public void ParseCallback_InvalidData_ReturnsNull(string? data)
    {
        Assert.Null(PokerCommandParser.ParseCallback(data));
    }

    [Theory]
    [InlineData("poker:check")]
    [InlineData("poker:call")]
    [InlineData("poker:fold")]
    [InlineData("poker:allin")]
    public void ParseCallback_PlayerActions_ReturnsPlayerAction(string data)
    {
        var cmd = PokerCommandParser.ParseCallback(data);
        var action = Assert.IsType<PokerCommand.PlayerAction>(cmd);
        Assert.Equal(0, action.Amount);
    }

    [Fact]
    public void ParseCallback_Raise_WithAmount_ReturnsPlayerAction()
    {
        var cmd = PokerCommandParser.ParseCallback("poker:raise:250");
        var action = Assert.IsType<PokerCommand.PlayerAction>(cmd);
        Assert.Equal("raise", action.Action);
        Assert.Equal(250, action.Amount);
    }

    [Fact]
    public void ParseCallback_RaiseMenu_ReturnsRaiseMenu()
    {
        var cmd = PokerCommandParser.ParseCallback("poker:raise_menu");
        Assert.IsType<PokerCommand.RaiseMenu>(cmd);
    }

    [Fact]
    public void ParseCallback_Raise_NoAmount_ReturnsNull()
    {
        Assert.Null(PokerCommandParser.ParseCallback("poker:raise"));
    }

    [Fact]
    public void ParseCallback_UnknownVerb_ReturnsNull()
    {
        Assert.Null(PokerCommandParser.ParseCallback("poker:unknown_verb"));
    }
}
