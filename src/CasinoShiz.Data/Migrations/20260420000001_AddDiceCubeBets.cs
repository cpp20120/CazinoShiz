using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasinoShiz.Migrations;

public partial class AddDiceCubeBets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DiceCubeBets",
            columns: table => new
            {
                UserId = table.Column<long>(type: "bigint", nullable: false),
                ChatId = table.Column<long>(type: "bigint", nullable: false),
                Amount = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_DiceCubeBets", x => new { x.UserId, x.ChatId }));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DiceCubeBets");
    }
}
