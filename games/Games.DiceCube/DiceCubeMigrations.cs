// ─────────────────────────────────────────────────────────────────────────────
// DiceCube schema — one pending-bet row per (user, chat). A bet is placed via
// /dice bet <amount>, then resolved when the same user throws a 🎲 in the same
// chat. The chat dimension matters because the same user can have parallel
// bets in different group chats.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace Games.DiceCube;

public sealed class DiceCubeMigrations : IModuleMigrations
{
    public string ModuleId => "dicecube";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE dicecube_bets (
                user_id     BIGINT      NOT NULL,
                chat_id     BIGINT      NOT NULL,
                amount      INTEGER     NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, chat_id)
            );
            """),
    ];
}
