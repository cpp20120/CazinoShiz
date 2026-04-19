using System.Security.Cryptography;

namespace Games.SecretHitler.Domain;

public static class ShRoleDealer
{
    public const int MinPlayers = 5;
    public const int MaxPlayers = 10;

    public static ShRole[] BuildRoleDeck(int playerCount)
    {
        var (liberals, fascists) = playerCount switch
        {
            5 => (3, 1),
            6 => (4, 1),
            7 => (4, 2),
            8 => (5, 2),
            9 => (5, 3),
            10 => (6, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(playerCount), $"Secret Hitler supports {MinPlayers}-{MaxPlayers} players"),
        };

        var roles = new List<ShRole>(playerCount);
        for (int i = 0; i < liberals; i++) roles.Add(ShRole.Liberal);
        for (int i = 0; i < fascists; i++) roles.Add(ShRole.Fascist);
        roles.Add(ShRole.Hitler);

        Shuffle(roles);
        return [.. roles];
    }

    public static void DealRoles(List<SecretHitlerPlayer> players)
    {
        var ordered = players.OrderBy(p => p.Position).ToList();
        var deck = BuildRoleDeck(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Role = deck[i];
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
