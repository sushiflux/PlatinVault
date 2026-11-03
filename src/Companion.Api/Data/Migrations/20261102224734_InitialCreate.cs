using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Companion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_leases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_leases", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SteamAppId = table.Column<int>(type: "INTEGER", nullable: true),
                    CoverUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    HeroBannerUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AccentColor = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EstimatedHours = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    MissablesInfo = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    DifficultyInfo = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    GenreTags = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    ContentLanguage = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    TrophiesUnlocked = table.Column<int>(type: "INTEGER", nullable: false),
                    TrophiesTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    TrophiesUpdatedAt = table.Column<string>(type: "TEXT", nullable: true),
                    PlatinumAt = table.Column<string>(type: "TEXT", nullable: true),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlaytimeMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    LastPlayedAt = table.Column<string>(type: "TEXT", nullable: true),
                    MetacriticScore = table.Column<int>(type: "INTEGER", nullable: true),
                    ReviewScoreDesc = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ReviewPercent = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseDate = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CompletionHours = table.Column<double>(type: "REAL", nullable: true),
                    IgdbRating = table.Column<int>(type: "INTEGER", nullable: true),
                    SteamGenres = table.Column<string>(type: "TEXT", nullable: false),
                    SteamGenresLanguage = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    FactsUpdatedAt = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "player_count_daily",
                columns: table => new
                {
                    AppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    MinCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgCount = table.Column<double>(type: "REAL", nullable: false),
                    MaxCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_count_daily", x => new { x.AppId, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "player_count_samples",
                columns: table => new
                {
                    AppId = table.Column<int>(type: "INTEGER", nullable: false),
                    SampledAt = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_count_samples", x => new { x.AppId, x.SampledAt });
                });

            migrationBuilder.CreateTable(
                name: "playtime_snapshots",
                columns: table => new
                {
                    AppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PlaytimeMinutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playtime_snapshots", x => new { x.AppId, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "game_translations",
                columns: table => new
                {
                    GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EstimatedHours = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    MissablesInfo = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    DifficultyInfo = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    GenreTags = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_translations", x => new { x.GameId, x.Language });
                    table.ForeignKey(
                        name: "FK_game_translations_games_GameId",
                        column: x => x.GameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guide_tabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SectionsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guide_tabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_guide_tabs_games_GameId",
                        column: x => x.GameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "progress_checks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CheckedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_progress_checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_progress_checks_games_GameId",
                        column: x => x.GameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guide_tab_translations",
                columns: table => new
                {
                    TabId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    SectionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guide_tab_translations", x => new { x.TabId, x.Language });
                    table.ForeignKey(
                        name: "FK_guide_tab_translations_guide_tabs_TabId",
                        column: x => x.TabId,
                        principalTable: "guide_tabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_games_Slug",
                table: "games",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_games_SteamAppId",
                table: "games",
                column: "SteamAppId");

            migrationBuilder.CreateIndex(
                name: "IX_guide_tabs_GameId_Slug",
                table: "guide_tabs",
                columns: new[] { "GameId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_count_samples_SampledAt",
                table: "player_count_samples",
                column: "SampledAt");

            migrationBuilder.CreateIndex(
                name: "IX_progress_checks_GameId_ItemKey",
                table: "progress_checks",
                columns: new[] { "GameId", "ItemKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_leases");

            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "game_translations");

            migrationBuilder.DropTable(
                name: "guide_tab_translations");

            migrationBuilder.DropTable(
                name: "player_count_daily");

            migrationBuilder.DropTable(
                name: "player_count_samples");

            migrationBuilder.DropTable(
                name: "playtime_snapshots");

            migrationBuilder.DropTable(
                name: "progress_checks");

            migrationBuilder.DropTable(
                name: "guide_tabs");

            migrationBuilder.DropTable(
                name: "games");
        }
    }
}
