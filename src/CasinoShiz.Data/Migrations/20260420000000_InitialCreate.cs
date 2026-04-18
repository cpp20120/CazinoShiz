using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasinoShiz.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BlackjackHands",
            columns: table => new
            {
                UserId = table.Column<long>(type: "bigint", nullable: false),
                Bet = table.Column<int>(type: "integer", nullable: false),
                PlayerCards = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                DealerCards = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                DeckState = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                ChatId = table.Column<long>(type: "bigint", nullable: false),
                StateMessageId = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_BlackjackHands", x => x.UserId));

        migrationBuilder.CreateTable(
            name: "Chats",
            columns: table => new
            {
                ChatId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "text", maxLength: 255, nullable: false),
                Username = table.Column<string>(type: "text", maxLength: 32, nullable: true),
                NotificationsEnabled = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Chats", x => x.ChatId));

        migrationBuilder.CreateTable(
            name: "DisplayNameOverrides",
            columns: table => new
            {
                OriginalName = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                NewName = table.Column<string>(type: "text", maxLength: 64, nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_DisplayNameOverrides", x => x.OriginalName));

        migrationBuilder.CreateTable(
            name: "FreespinCodes",
            columns: table => new
            {
                Code = table.Column<System.Guid>(type: "uuid", nullable: false),
                Active = table.Column<bool>(type: "boolean", nullable: false),
                IssuedBy = table.Column<long>(type: "bigint", nullable: false),
                IssuedAt = table.Column<long>(type: "bigint", nullable: false),
                ChatId = table.Column<long>(type: "bigint", nullable: true),
                MessageId = table.Column<int>(type: "integer", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_FreespinCodes", x => x.Code));

        migrationBuilder.CreateTable(
            name: "HorseBets",
            columns: table => new
            {
                Id = table.Column<System.Guid>(type: "uuid", nullable: false),
                RaceDate = table.Column<string>(type: "text", maxLength: 10, nullable: false),
                HorseId = table.Column<int>(type: "integer", nullable: false),
                Amount = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_HorseBets", x => x.Id));

        migrationBuilder.CreateTable(
            name: "HorseResults",
            columns: table => new
            {
                RaceDate = table.Column<string>(type: "text", maxLength: 10, nullable: false),
                Winner = table.Column<int>(type: "integer", nullable: false),
                ImageData = table.Column<byte[]>(type: "bytea", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_HorseResults", x => x.RaceDate));

        migrationBuilder.CreateTable(
            name: "PokerSeats",
            columns: table => new
            {
                InviteCode = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<long>(type: "bigint", nullable: false),
                DisplayName = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                Stack = table.Column<int>(type: "integer", nullable: false),
                HoleCards = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CurrentBet = table.Column<int>(type: "integer", nullable: false),
                HasActedThisRound = table.Column<bool>(type: "boolean", nullable: false),
                ChatId = table.Column<long>(type: "bigint", nullable: false),
                StateMessageId = table.Column<int>(type: "integer", nullable: true),
                JoinedAt = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_PokerSeats", x => new { x.InviteCode, x.Position }));

        migrationBuilder.CreateTable(
            name: "PokerTables",
            columns: table => new
            {
                InviteCode = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                HostUserId = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Phase = table.Column<int>(type: "integer", nullable: false),
                SmallBlind = table.Column<int>(type: "integer", nullable: false),
                BigBlind = table.Column<int>(type: "integer", nullable: false),
                Pot = table.Column<int>(type: "integer", nullable: false),
                CommunityCards = table.Column<string>(type: "text", maxLength: 32, nullable: false),
                DeckState = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                ButtonSeat = table.Column<int>(type: "integer", nullable: false),
                CurrentSeat = table.Column<int>(type: "integer", nullable: false),
                CurrentBet = table.Column<int>(type: "integer", nullable: false),
                MinRaise = table.Column<int>(type: "integer", nullable: false),
                LastActionAt = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_PokerTables", x => x.InviteCode));

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                DisplayName = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                Coins = table.Column<int>(type: "integer", nullable: false),
                LastDayUtc = table.Column<long>(type: "bigint", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                ExtraAttempts = table.Column<int>(type: "integer", nullable: false),
                BlackjackHandsPlayed = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.TelegramUserId));

        migrationBuilder.CreateIndex(
            name: "IX_FreespinCodes_Active",
            table: "FreespinCodes",
            column: "Active");

        migrationBuilder.CreateIndex(
            name: "IX_HorseBets_RaceDate_UserId",
            table: "HorseBets",
            columns: ["RaceDate", "UserId"]);

        migrationBuilder.CreateIndex(
            name: "IX_PokerSeats_UserId",
            table: "PokerSeats",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_PokerTables_Status",
            table: "PokerTables",
            column: "Status");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BlackjackHands");
        migrationBuilder.DropTable(name: "Chats");
        migrationBuilder.DropTable(name: "DisplayNameOverrides");
        migrationBuilder.DropTable(name: "FreespinCodes");
        migrationBuilder.DropTable(name: "HorseBets");
        migrationBuilder.DropTable(name: "HorseResults");
        migrationBuilder.DropTable(name: "PokerSeats");
        migrationBuilder.DropTable(name: "PokerTables");
        migrationBuilder.DropTable(name: "Users");
    }
}
