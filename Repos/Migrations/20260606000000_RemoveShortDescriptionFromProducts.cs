using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repos.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShortDescriptionFromProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShortDescription",
                table: "Products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortDescription",
                table: "Products",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
