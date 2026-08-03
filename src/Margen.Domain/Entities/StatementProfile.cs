namespace Margen.Domain.Entities;

/// <summary>
/// Cómo se lee el CSV de un banco. Uno por banco, escrito una vez.
/// </summary>
/// <remarks>
/// **Es lo que hace que no haga falta conocer el formato de antemano.** El
/// humano mira su archivo, dice qué columna es cuál, ve una vista previa de lo
/// que se entendió y solo entonces importa.
///
/// Se guarda porque el estado de cuenta llega todos los meses y volver a
/// mapearlo cada vez es la clase de fricción que hace que se deje de conciliar.
/// Y va por banco porque cada uno exporta distinto: con tres bancos hay tres
/// perfiles, y añadir el cuarto no toca a los otros.
/// </remarks>
public class StatementProfile
{
    public Guid Id { get; set; }

    /// <summary>Cómo se llama para quien lo elige: «Popular, cuenta de nómina».</summary>
    public required string Name { get; set; }

    /// <summary>A qué cuenta entran los movimientos que salgan de aquí.</summary>
    public Guid AccountId { get; set; }

    public Account? Account { get; set; }

    /// <summary>
    /// El separador, o nulo para detectarlo.
    /// </summary>
    /// <remarks>
    /// Es texto y no un carácter porque la tabulación no se escribe en una
    /// celda: se guarda vacío para «detectar» y con el carácter cuando el
    /// humano lo fija.
    /// </remarks>
    public string? Delimiter { get; set; }

    /// <summary>Filas de membrete que se saltan antes de los datos.</summary>
    public int SkipRows { get; set; } = 1;

    public int DateColumn { get; set; }

    /// <summary>
    /// El formato exacto de la fecha, por ejemplo <c>dd/MM/yyyy</c>.
    /// </summary>
    /// <remarks>
    /// Explícito y no una lista de candidatos: `01/02/2026` es el 1 de febrero o
    /// el 2 de enero según el banco, y las dos lecturas son plausibles.
    /// Adivinar mete movimientos en el período equivocado.
    /// </remarks>
    public required string DateFormat { get; set; }

    public int DescriptionColumn { get; set; }

    /// <summary>Columna del monto con signo, si el banco usa una sola.</summary>
    public int? AmountColumn { get; set; }

    /// <summary>Columna de cargos, si usa dos.</summary>
    public int? DebitColumn { get; set; }

    /// <summary>Columna de abonos, si usa dos.</summary>
    public int? CreditColumn { get; set; }

    /// <summary>`Point` para `1,234.56`; `Comma` para `1.234,56`.</summary>
    public string Decimals { get; set; } = "Point";

    /// <summary>
    /// Si en la columna única los cargos vienen en positivo.
    /// </summary>
    /// <remarks>
    /// Sin esto, un banco que escribe los cargos en positivo importaría el
    /// estado de cuenta entero al revés: todos los gastos como ingresos y el
    /// saldo al doble.
    /// </remarks>
    public bool InvertSign { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
