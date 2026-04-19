namespace Games.Admin;

public sealed class AdminOptions
{
    public const string SectionName = "Games:admin";

    public List<long> Admins { get; init; } = [];
}
