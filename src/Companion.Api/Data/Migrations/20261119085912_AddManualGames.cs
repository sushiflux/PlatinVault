using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Companion.Api.Data.Migrations;

/// <inheritdoc />
public partial class AddManualGames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Developer",
            table: "games",
            type: "TEXT",
            maxLength: 240,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FactsSource",
            table: "games",
            type: "TEXT",
            maxLength: 8,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FirstReleaseAt",
            table: "games",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "IgdbId",
            table: "games",
            type: "INTEGER",
            nullable: true);

        // Every existing game (Steam library tiles and imported guides without an AppID) becomes "steam" through the column default
        migrationBuilder.AddColumn<string>(
            name: "Platform",
            table: "games",
            type: "TEXT",
            maxLength: 16,
            nullable: false,
            defaultValue: "steam");

        migrationBuilder.AddColumn<string>(
            name: "PlatformVariant",
            table: "games",
            type: "TEXT",
            maxLength: 8,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Publisher",
            table: "games",
            type: "TEXT",
            maxLength: 240,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Summary",
            table: "games",
            type: "TEXT",
            maxLength: 3000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TrophyListResearchedAt",
            table: "games",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TrophyListSourcesJson",
            table: "games",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "game_covers",
            columns: table => new
            {
                GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                ContentType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_game_covers", x => x.GameId);
                table.ForeignKey(
                    name: "FK_game_covers_games_GameId",
                    column: x => x.GameId,
                    principalTable: "games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "game_trophies",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                TrophyKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                OriginalName = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                Grade = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                Points = table.Column<int>(type: "INTEGER", nullable: true),
                RarityPercent = table.Column<double>(type: "REAL", nullable: true),
                IsHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                IsMissable = table.Column<bool>(type: "INTEGER", nullable: false),
                GroupName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                Category = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Tip = table.Column<string>(type: "TEXT", maxLength: 1500, nullable: false),
                UnlockedAt = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_game_trophies", x => x.Id);
                table.ForeignKey(
                    name: "FK_game_trophies_games_GameId",
                    column: x => x.GameId,
                    principalTable: "games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "game_trophy_translations",
            columns: table => new
            {
                GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                ItemsJson = table.Column<string>(type: "TEXT", nullable: false),
                SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_game_trophy_translations", x => new { x.GameId, x.Language });
                table.ForeignKey(
                    name: "FK_game_trophy_translations_games_GameId",
                    column: x => x.GameId,
                    principalTable: "games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_games_IgdbId",
            table: "games",
            column: "IgdbId");

        migrationBuilder.CreateIndex(
            name: "IX_game_trophies_GameId_TrophyKey",
            table: "game_trophies",
            columns: new[] { "GameId", "TrophyKey" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "game_covers");

        migrationBuilder.DropTable(
            name: "game_trophies");

        migrationBuilder.DropTable(
            name: "game_trophy_translations");

        migrationBuilder.DropIndex(
            name: "IX_games_IgdbId",
            table: "games");

        migrationBuilder.DropColumn(
            name: "Developer",
            table: "games");

        migrationBuilder.DropColumn(
            name: "FactsSource",
            table: "games");

        migrationBuilder.DropColumn(
            name: "FirstReleaseAt",
            table: "games");

        migrationBuilder.DropColumn(
            name: "IgdbId",
            table: "games");

        migrationBuilder.DropColumn(
            name: "Platform",
            table: "games");

        migrationBuilder.DropColumn(
            name: "PlatformVariant",
            table: "games");

        migrationBuilder.DropColumn(
            name: "Publisher",
            table: "games");

        migrationBuilder.DropColumn(
            name: "Summary",
            table: "games");

        migrationBuilder.DropColumn(
            name: "TrophyListResearchedAt",
            table: "games");

        migrationBuilder.DropColumn(
            name: "TrophyListSourcesJson",
            table: "games");
    }
}
