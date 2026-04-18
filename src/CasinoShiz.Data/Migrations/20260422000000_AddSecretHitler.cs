using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasinoShiz.Migrations;

public partial class AddSecretHitler : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SecretHitlerGames",
            columns: table => new
            {
                InviteCode = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                HostUserId = table.Column<long>(type: "bigint", nullable: false),
                ChatId = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Phase = table.Column<int>(type: "integer", nullable: false),
                LiberalPolicies = table.Column<int>(type: "integer", nullable: false),
                FascistPolicies = table.Column<int>(type: "integer", nullable: false),
                ElectionTracker = table.Column<int>(type: "integer", nullable: false),
                CurrentPresidentPosition = table.Column<int>(type: "integer", nullable: false),
                NominatedChancellorPosition = table.Column<int>(type: "integer", nullable: false),
                LastElectedPresidentPosition = table.Column<int>(type: "integer", nullable: false),
                LastElectedChancellorPosition = table.Column<int>(type: "integer", nullable: false),
                DeckState = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                DiscardState = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                PresidentDraw = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                ChancellorReceived = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                Winner = table.Column<int>(type: "integer", nullable: false),
                WinReason = table.Column<int>(type: "integer", nullable: false),
                BuyIn = table.Column<int>(type: "integer", nullable: false),
                Pot = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                LastActionAt = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_SecretHitlerGames", x => x.InviteCode));

        migrationBuilder.CreateIndex(
            name: "IX_SecretHitlerGames_Status",
            table: "SecretHitlerGames",
            column: "Status");

        migrationBuilder.CreateTable(
            name: "SecretHitlerPlayers",
            columns: table => new
            {
                InviteCode = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<long>(type: "bigint", nullable: false),
                DisplayName = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                ChatId = table.Column<long>(type: "bigint", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                IsAlive = table.Column<bool>(type: "boolean", nullable: false),
                LastVote = table.Column<int>(type: "integer", nullable: false),
                StateMessageId = table.Column<int>(type: "integer", nullable: true),
                JoinedAt = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_SecretHitlerPlayers", x => new { x.InviteCode, x.Position }));

        migrationBuilder.CreateIndex(
            name: "IX_SecretHitlerPlayers_UserId",
            table: "SecretHitlerPlayers",
            column: "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SecretHitlerPlayers");
        migrationBuilder.DropTable(name: "SecretHitlerGames");
    }
}
