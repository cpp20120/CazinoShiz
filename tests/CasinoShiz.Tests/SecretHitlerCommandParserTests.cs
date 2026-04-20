using Games.SecretHitler;
using Xunit;

namespace CasinoShiz.Tests;

public class SecretHitlerCommandParserTests
{
    // ── ParseText ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/sh")]
    [InlineData("/sh ")]
    public void ParseText_NoVerb_ReturnsUsage(string text)
    {
        Assert.IsType<SecretHitlerCommand.Usage>(SecretHitlerCommandParser.ParseText(text));
    }

    [Fact]
    public void ParseText_Create_ReturnsCreate()
    {
        Assert.IsType<SecretHitlerCommand.Create>(SecretHitlerCommandParser.ParseText("/sh create"));
    }

    [Fact]
    public void ParseText_Join_WithCode_ReturnsJoin()
    {
        var cmd = SecretHitlerCommandParser.ParseText("/sh join abc123");
        var join = Assert.IsType<SecretHitlerCommand.Join>(cmd);
        Assert.Equal("ABC123", join.Code);
    }

    [Fact]
    public void ParseText_Join_CodeIsUppercased()
    {
        var cmd = SecretHitlerCommandParser.ParseText("/sh join lowercase");
        var join = Assert.IsType<SecretHitlerCommand.Join>(cmd);
        Assert.Equal("LOWERCASE", join.Code);
    }

    [Fact]
    public void ParseText_Join_MissingCode_ReturnsMissingCode()
    {
        Assert.IsType<SecretHitlerCommand.JoinMissingCode>(SecretHitlerCommandParser.ParseText("/sh join"));
    }

    [Fact]
    public void ParseText_Start_ReturnsStart()
    {
        Assert.IsType<SecretHitlerCommand.Start>(SecretHitlerCommandParser.ParseText("/sh start"));
    }

    [Fact]
    public void ParseText_Leave_ReturnsLeave()
    {
        Assert.IsType<SecretHitlerCommand.Leave>(SecretHitlerCommandParser.ParseText("/sh leave"));
    }

    [Fact]
    public void ParseText_Status_ReturnsStatus()
    {
        Assert.IsType<SecretHitlerCommand.Status>(SecretHitlerCommandParser.ParseText("/sh status"));
    }

    [Fact]
    public void ParseText_Unknown_ReturnsUnknown()
    {
        var cmd = SecretHitlerCommandParser.ParseText("/sh foobar");
        var unknown = Assert.IsType<SecretHitlerCommand.Unknown>(cmd);
        Assert.Equal("foobar", unknown.Action);
    }

    [Fact]
    public void ParseText_VerbIsLowercased()
    {
        Assert.IsType<SecretHitlerCommand.Create>(SecretHitlerCommandParser.ParseText("/sh CREATE"));
    }

    // ── ParseCallback ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("poker:nominate:1")]
    public void ParseCallback_InvalidPrefix_ReturnsNull(string? data)
    {
        Assert.Null(SecretHitlerCommandParser.ParseCallback(data));
    }

    [Fact]
    public void ParseCallback_Nominate_ReturnsNominate()
    {
        var cmd = SecretHitlerCommandParser.ParseCallback("sh:nominate:3");
        var nominate = Assert.IsType<SecretHitlerCommand.Nominate>(cmd);
        Assert.Equal(3, nominate.ChancellorPosition);
    }

    [Fact]
    public void ParseCallback_Nominate_InvalidIndex_ReturnsNull()
    {
        Assert.Null(SecretHitlerCommandParser.ParseCallback("sh:nominate:abc"));
    }

    [Fact]
    public void ParseCallback_VoteJa_ReturnsVoteTrue()
    {
        var cmd = SecretHitlerCommandParser.ParseCallback("sh:vote:ja");
        var vote = Assert.IsType<SecretHitlerCommand.Vote>(cmd);
        Assert.True(vote.Ja);
    }

    [Fact]
    public void ParseCallback_VoteNein_ReturnsVoteFalse()
    {
        var cmd = SecretHitlerCommandParser.ParseCallback("sh:vote:nein");
        var vote = Assert.IsType<SecretHitlerCommand.Vote>(cmd);
        Assert.False(vote.Ja);
    }

    [Fact]
    public void ParseCallback_VoteInvalid_ReturnsNull()
    {
        Assert.Null(SecretHitlerCommandParser.ParseCallback("sh:vote:maybe"));
    }

    [Fact]
    public void ParseCallback_Discard_ReturnsPresidentDiscard()
    {
        var cmd = SecretHitlerCommandParser.ParseCallback("sh:discard:1");
        var discard = Assert.IsType<SecretHitlerCommand.PresidentDiscard>(cmd);
        Assert.Equal(1, discard.Index);
    }

    [Fact]
    public void ParseCallback_Discard_InvalidIndex_ReturnsNull()
    {
        Assert.Null(SecretHitlerCommandParser.ParseCallback("sh:discard:xyz"));
    }

    [Fact]
    public void ParseCallback_Enact_ReturnsChancellorEnact()
    {
        var cmd = SecretHitlerCommandParser.ParseCallback("sh:enact:2");
        var enact = Assert.IsType<SecretHitlerCommand.ChancellorEnact>(cmd);
        Assert.Equal(2, enact.Index);
    }

    [Fact]
    public void ParseCallback_Enact_InvalidIndex_ReturnsNull()
    {
        Assert.Null(SecretHitlerCommandParser.ParseCallback("sh:enact:nope"));
    }

    [Fact]
    public void ParseCallback_UnknownVerb_ReturnsNull()
    {
        Assert.Null(SecretHitlerCommandParser.ParseCallback("sh:unknown_verb"));
    }
}
