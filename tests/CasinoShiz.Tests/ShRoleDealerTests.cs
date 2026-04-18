using CasinoShiz.Data.Entities;
using CasinoShiz.Services.SecretHitler.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class ShRoleDealerTests
{
    [Theory]
    [InlineData(5, 3, 1)]
    [InlineData(6, 4, 1)]
    [InlineData(7, 4, 2)]
    [InlineData(8, 5, 2)]
    [InlineData(9, 5, 3)]
    [InlineData(10, 6, 3)]
    public void BuildRoleDeck_hasCorrectDistributionPlusOneHitler(int players, int liberals, int fascists)
    {
        var roles = ShRoleDealer.BuildRoleDeck(players);

        Assert.Equal(players, roles.Length);
        Assert.Equal(liberals, roles.Count(r => r == ShRole.Liberal));
        Assert.Equal(fascists, roles.Count(r => r == ShRole.Fascist));
        Assert.Equal(1, roles.Count(r => r == ShRole.Hitler));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(11)]
    [InlineData(-1)]
    public void BuildRoleDeck_outOfRange_throws(int players)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShRoleDealer.BuildRoleDeck(players));
    }

    [Fact]
    public void DealRoles_assignsOneRolePerPlayer()
    {
        var players = Enumerable.Range(0, 7)
            .Select(i => new SecretHitlerPlayer { Position = i })
            .ToList();

        ShRoleDealer.DealRoles(players);

        Assert.Equal(4, players.Count(p => p.Role == ShRole.Liberal));
        Assert.Equal(2, players.Count(p => p.Role == ShRole.Fascist));
        Assert.Equal(1, players.Count(p => p.Role == ShRole.Hitler));
    }
}
