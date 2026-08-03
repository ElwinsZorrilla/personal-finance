namespace Margen.Domain;

/// <summary>
/// De qué tipo de operación se deduce cada dirección.
/// </summary>
/// <remarks>
/// Vive aquí y no dentro de <c>Transaction</c> porque la usan tres sitios: la
/// entidad al nacer, la ingesta al crear el movimiento desde un correo y las
/// pruebas. Tenerla escrita una vez es lo que impide que el correo y la
/// pantalla discrepen sobre si algo fue ingreso.
/// </remarks>
public static class Directions
{
    /// <summary>
    /// La dirección con la que nace un movimiento de este tipo.
    /// </summary>
    /// <remarks>
    /// **Un retiro es egreso.** El dinero sigue siendo tuyo, en el bolsillo,
    /// pero si no registras en qué lo gastaste, tratarlo como traspaso lo haría
    /// desaparecer del gasto del período y la pantalla diría que te queda más
    /// de lo que hay. Contar de más es el error barato; contar de menos es el
    /// que hace gastar dinero que no está.
    ///
    /// **Una transferencia enviada es egreso.** Por el mismo motivo: el correo
    /// dice que salió, no a dónde fue. Si fue a tu propia cuenta de ahorro, se
    /// corrige a <see cref="TxDirection.Internal"/> desde Revisión y deja de
    /// contar.
    ///
    /// **Un pago de tarjeta es traspaso.** Es el único caso que el correo sí
    /// resuelve solo: paga una deuda tuya con dinero tuyo. Contarlo duplicaría
    /// el gasto, una vez al comprar con la tarjeta y otra al pagarla.
    /// </remarks>
    public static TxDirection Of(TxKind kind) => kind switch
    {
        TxKind.Deposit => TxDirection.Inflow,
        TxKind.Refund => TxDirection.Inflow,
        TxKind.Payment => TxDirection.Internal,
        _ => TxDirection.Outflow,
    };

    /// <summary>Cómo se llama en pantalla. Una palabra, sin jerga.</summary>
    public static string Label(TxDirection direction) => direction switch
    {
        TxDirection.Inflow => "Ingreso",
        TxDirection.Outflow => "Egreso",
        _ => "Traspaso",
    };
}
