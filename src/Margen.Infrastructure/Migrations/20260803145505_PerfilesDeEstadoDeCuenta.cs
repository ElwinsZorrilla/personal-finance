using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Margen.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PerfilesDeEstadoDeCuenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StatementProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Delimiter = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    SkipRows = table.Column<int>(type: "integer", nullable: false),
                    DateColumn = table.Column<int>(type: "integer", nullable: false),
                    DateFormat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DescriptionColumn = table.Column<int>(type: "integer", nullable: false),
                    AmountColumn = table.Column<int>(type: "integer", nullable: true),
                    DebitColumn = table.Column<int>(type: "integer", nullable: true),
                    CreditColumn = table.Column<int>(type: "integer", nullable: true),
                    Decimals = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InvertSign = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementProfiles", x => x.Id);
                    table.CheckConstraint("CK_StatementProfiles_TieneMonto", "\"AmountColumn\" IS NOT NULL OR \"DebitColumn\" IS NOT NULL OR \"CreditColumn\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_StatementProfiles_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatementProfiles_AccountId",
                table: "StatementProfiles",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_StatementProfiles_Name",
                table: "StatementProfiles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StatementProfiles");
        }
    }
}
