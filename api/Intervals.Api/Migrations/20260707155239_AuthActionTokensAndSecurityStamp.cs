using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intervals.Api.Migrations
{
    /// <inheritdoc />
    public partial class AuthActionTokensAndSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerifiedAtUtc",
                table: "PasswordCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthActionTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    EmailNormalized = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthActionTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthActionTokens_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthActionTokens_Purpose_TokenHash",
                table: "AuthActionTokens",
                columns: new[] { "Purpose", "TokenHash" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthActionTokens_UserId_Purpose",
                table: "AuthActionTokens",
                columns: new[] { "UserId", "Purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthActionTokens");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAtUtc",
                table: "PasswordCredentials");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "AppUsers");
        }
    }
}
