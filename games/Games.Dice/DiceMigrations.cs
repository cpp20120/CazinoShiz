// ─────────────────────────────────────────────────────────────────────────────
// Dice module schema — one audit table for the rolls history. Per-user
// attempts, bank-tax windowing, and freespin-code issuance lived on the shared
// UserState row in the monolith; they'll come back as a follow-up during the
// full port (#15) once the ownership boundary between "economy" and "dice
// state" has been designed.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace Games.Dice;

public sealed class DiceMigrations : IModuleMigrations
{
    public string ModuleId => "dice";

    public IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration("001_initial", """
            CREATE TABLE dice_rolls (
                id           UUID         PRIMARY KEY,
                user_id      BIGINT       NOT NULL,
                dice_value   SMALLINT     NOT NULL,
                prize        INTEGER      NOT NULL,
                loss         INTEGER      NOT NULL,
                rolled_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_dice_rolls_user_time ON dice_rolls (user_id, rolled_at DESC);
            """),
    ];
}
