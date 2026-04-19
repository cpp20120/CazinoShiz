using System.Data;
using Dapper;

namespace BotFramework.Host;

// Npgsql maps TIMESTAMPTZ to DateTime (Kind=Utc) by default. Row DTOs that
// type the column as DateTimeOffset would otherwise fail Dapper's ctor-match
// with "no matching signature" because DateTime is not assignable to
// DateTimeOffset. This handler bridges the gap for both reads and writes.
internal sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        => parameter.Value = value.UtcDateTime;

    public override DateTimeOffset Parse(object value) => value switch
    {
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        string s => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateTimeOffset"),
    };
}

internal static class DapperTypeHandlers
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
    }
}
