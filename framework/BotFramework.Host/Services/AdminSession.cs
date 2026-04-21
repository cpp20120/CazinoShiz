using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace BotFramework.Host.Services;

public enum AdminRole { None, ReadOnly, SuperAdmin }

public sealed record AdminSession(long UserId, string Name, AdminRole Role);

public static class AdminSessionExtensions
{
    private const string Key = "admin_sess";
    private static readonly JsonSerializerOptions JsonOpts = new();

    public static AdminSession? GetAdminSession(this ISession session)
    {
        var bytes = session.Get(Key);
        if (bytes is null or { Length: 0 }) return null;
        try { return JsonSerializer.Deserialize<AdminSession>(bytes, JsonOpts); }
        catch { return null; }
    }

    public static void SetAdminSession(this ISession session, AdminSession admin)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(admin, JsonOpts);
        session.Set(Key, bytes);
    }

    public static void ClearAdminSession(this ISession session) => session.Remove(Key);
}
