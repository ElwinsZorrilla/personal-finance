using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Margen.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DireccionDelMovimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "Transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Outflow");

            // El generador propone cadena vacía, que no es ninguna dirección
            // válida: las filas que ya existen quedarían con un valor que no
            // convierte a enum y reventarían al leerse. Se deduce de lo que
            // cada fila ya dice ser, con la misma regla que `Directions.Of`.
            migrationBuilder.Sql("""
                UPDATE "Transactions"
                SET "Direction" = CASE
                    WHEN "Kind" IN ('Deposit', 'Refund') THEN 'Inflow'
                    WHEN "Kind" = 'Payment' THEN 'Internal'
                    ELSE 'Outflow'
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Direction_OccurredAt",
                table: "Transactions",
                columns: new[] { "Direction", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Direction_OccurredAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "Transactions");
        }
    }
}
