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

        new Migration("002_dicecube_bets_rule_snapshot", """
            ALTER TABLE dicecube_bets
                ADD COLUMN IF NOT EXISTS mult4 INTEGER NOT NULL DEFAULT 1,
                ADD COLUMN IF NOT EXISTS mult5 INTEGER NOT NULL DEFAULT 2,
                ADD COLUMN IF NOT EXISTS mult6 INTEGER NOT NULL DEFAULT 2;
            """),
    ];
}
