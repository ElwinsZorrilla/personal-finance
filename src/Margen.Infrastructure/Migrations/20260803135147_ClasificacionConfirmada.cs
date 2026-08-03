using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Margen.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClasificacionConfirmada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CategoryConfirmedAt",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationSource",
                table: "Transactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_MerchantNormalized_CategoryId",
                table: "Transactions",
                columns: new[] { "MerchantNormalized", "CategoryId" },
                filter: "\"CategoryConfirmedAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_MerchantNormalized_CategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategoryConfirmedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClassificationSource",
                table: "Transactions");
        }
    }
}
