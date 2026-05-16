using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repos.Migrations
{
    /// <inheritdoc />
    public partial class AddDisplayNameToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add DisplayName column as nullable temporarily
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Step 2: Backfill existing users with email prefix as DisplayName
            // This safely handles existing users before making the column non-nullable
            migrationBuilder.Sql(@"
                UPDATE ""Users""
                SET ""DisplayName"" = COALESCE(
                    SPLIT_PART(""Email"", '@', 1),
                    'User'
                )
                WHERE ""DisplayName"" IS NULL OR ""DisplayName"" = '';
            ");

            // Step 3: Add NOT NULL constraint
            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Users");
        }
    }
}
