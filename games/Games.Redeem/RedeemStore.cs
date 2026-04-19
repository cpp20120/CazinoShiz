using BotFramework.Host;
using Dapper;

namespace Games.Redeem;

public interface IRedeemStore
{
    Task<RedeemCode?> FindAsync(Guid code, CancellationToken ct);
    Task InsertAsync(RedeemCode code, CancellationToken ct);
    Task<bool> MarkRedeemedAsync(Guid code, long redeemedBy, long redeemedAt, CancellationToken ct);
}

public sealed class RedeemStore(INpgsqlConnectionFactory connections) : IRedeemStore
{
    public async Task<RedeemCode?> FindAsync(Guid code, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CodeRow>(new CommandDefinition(
            "SELECT code AS Code, active AS Active, issued_by AS IssuedBy, issued_at AS IssuedAt, " +
            "redeemed_by AS RedeemedBy, redeemed_at AS RedeemedAt " +
            "FROM redeem_codes WHERE code = @code",
            new { code },
            cancellationToken: ct));
        return row?.ToEntity();
    }

    public async Task InsertAsync(RedeemCode c, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO redeem_codes (code, active, issued_by, issued_at) " +
            "VALUES (@Code, @Active, @IssuedBy, @IssuedAt)",
            new { c.Code, c.Active, c.IssuedBy, c.IssuedAt },
            cancellationToken: ct));
    }

    public async Task<bool> MarkRedeemedAsync(Guid code, long redeemedBy, long redeemedAt, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE redeem_codes SET active = false, redeemed_by = @redeemedBy, redeemed_at = @redeemedAt " +
            "WHERE code = @code AND active = true",
            new { code, redeemedBy, redeemedAt },
            cancellationToken: ct));
        return rows > 0;
    }

    private sealed record CodeRow(
        Guid Code, bool Active, long IssuedBy, long IssuedAt, long? RedeemedBy, long? RedeemedAt)
    {
        public RedeemCode ToEntity() => new()
        {
            Code = Code,
            Active = Active,
            IssuedBy = IssuedBy,
            IssuedAt = IssuedAt,
            RedeemedBy = RedeemedBy,
            RedeemedAt = RedeemedAt,
        };
    }
}
