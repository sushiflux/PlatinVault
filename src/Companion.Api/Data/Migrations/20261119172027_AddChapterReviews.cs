using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Companion.Api.Data.Migrations;

/// <inheritdoc />
public partial class AddChapterReviews : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "guide_chapter_reviews",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                GameId = table.Column<Guid>(type: "TEXT", nullable: false),
                TabId = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                Language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                ResearcherProvider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ReviewerProvider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                OriginalSectionsJson = table.Column<string>(type: "TEXT", nullable: false),
                SecondSectionsJson = table.Column<string>(type: "TEXT", nullable: false),
                FindingsJson = table.Column<string>(type: "TEXT", nullable: false),
                CountsJson = table.Column<string>(type: "TEXT", nullable: false),
                ResultHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_guide_chapter_reviews", x => x.Id);
                table.ForeignKey(
                    name: "FK_guide_chapter_reviews_games_GameId",
                    column: x => x.GameId,
                    principalTable: "games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_guide_chapter_reviews_guide_tabs_TabId",
                    column: x => x.TabId,
                    principalTable: "guide_tabs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_guide_chapter_reviews_GameId",
            table: "guide_chapter_reviews",
            column: "GameId");

        migrationBuilder.CreateIndex(
            name: "IX_guide_chapter_reviews_TabId",
            table: "guide_chapter_reviews",
            column: "TabId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "guide_chapter_reviews");
    }
}
