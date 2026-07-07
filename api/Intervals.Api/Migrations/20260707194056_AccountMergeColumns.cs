using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intervals.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountMergeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoUserId",
                table: "AppUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MergedUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_MergedIntoUserId",
                table: "AppUsers",
                column: "MergedIntoUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_MergedIntoUserId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "MergedIntoUserId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "MergedUtc",
                table: "AppUsers");
        }
    }
}
