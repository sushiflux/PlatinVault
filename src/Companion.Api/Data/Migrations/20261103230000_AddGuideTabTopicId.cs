using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Companion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGuideTabTopicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TopicId",
                table: "guide_tabs",
                type: "TEXT",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TopicId",
                table: "guide_tabs");
        }
    }
}
