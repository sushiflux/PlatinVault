using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Companion.Api.Data.Migrations;

/// <inheritdoc />
public partial class AddIgdbUserRating : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "IgdbUserRating",
            table: "games",
            type: "INTEGER",
            nullable: true);

        // IgdbRating used to hold aggregated_rating ?? total_rating, and the user rating is new: reset the card facts timestamp so the
        // next card refresh (a list request refreshes cards whose facts are null or older than 30 min) rewrites both values
        migrationBuilder.Sql("UPDATE games SET FactsUpdatedAt = NULL WHERE SteamAppId IS NOT NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IgdbUserRating",
            table: "games");
    }
}
