using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Margen.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DosFechasDeCobro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "PayDays",
                table: "BudgetPeriods",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            // **Relleno de lo que ya existe.** Un período abierto con la lista
            // vacía no tiene calendario, y `PaySchedule.Of([])` lo rechaza: la
            // app se quedaría sin poder abrir el siguiente sin decir por qué.
            //
            // El día se deduce de la fecha en que empezó, que es exactamente el
            // día de cobro con el que se creó. Un período que empezó un 31 se
            // guarda como 31, no como el día que resultó en ese mes: es la
            // diferencia entre «cobro fin de mes» y «cobro el 30».
            migrationBuilder.Sql("""
                UPDATE "BudgetPeriods"
                SET "PayDays" = ARRAY[EXTRACT(DAY FROM "StartDate")::integer]
                WHERE cardinality("PayDays") = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayDays",
                table: "BudgetPeriods");
        }
    }
}
