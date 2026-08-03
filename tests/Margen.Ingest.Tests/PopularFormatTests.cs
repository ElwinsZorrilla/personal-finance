namespace Margen.Ingest.Tests;

/// <summary>
/// El redactor contra el formato real del Banco Popular Dominicano.
/// </summary>
/// <remarks>
/// El cuerpo de abajo tiene la estructura de una notificación real, con los
/// datos ya inventados. Se escribió **después** de leer las primeras cuarenta
/// muestras capturadas, y esa lectura encontró que el redactor dejaba pasar los
/// cuatro dígitos de la tarjeta en los cuarenta archivos: el Popular no
/// enmascara con asteriscos, escribe «terminada en 2074».
///
/// La lección no es que faltara un patrón. Es que un redactor solo cubre los
/// formatos que ha visto, y por eso las muestras se leen antes de versionarlas
/// en lugar de confiar en que la herramienta las dejó limpias.
/// </remarks>
public sealed class PopularFormatTests
{
    private static readonly Redactor Sujeto = new(RedactionSettings.Of("Fulano De Tal"));

    private const string Consumo = """
        Estimado (a) Fulano De Tal

        Gracias por utilizar su VISA ISI, terminada en 2074.

        A continuación le informamos el detalle de su transacción:

        Monto 	Moneda 	Fecha 	Comercio	Estatus
        RD$2,450.75 	Peso dominicano 	26/07/2026 	SM NACIONAL
        CHARLES 	Aprobada

        En caso de requerir mayor información, puede comunicarse con nosotros
        llamando al 809-544-5555.
        """;

    [Fact]
    public void los_cuatro_digitos_escritos_con_palabras_se_quitan()
    {
        string limpio = Sujeto.Redact(Consumo);

        Assert.DoesNotContain("2074", limpio, StringComparison.Ordinal);
        Assert.Contains("terminada en 1234", limpio, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("terminada en 9876")]
    [InlineData("terminado en 9876")]
    [InlineData("Terminada En 9876")]
    [InlineData("finalizada en 9876")]
    [InlineData("número 9876")]
    public void las_variantes_de_la_misma_frase_tambien(string frase)
    {
        Assert.DoesNotContain(
            "9876",
            Sujeto.Redact($"Su tarjeta {frase}."),
            StringComparison.Ordinal);
    }

    [Fact]
    public void el_nombre_del_titular_se_quita()
    {
        string limpio = Sujeto.Redact(Consumo);

        Assert.DoesNotContain("Fulano", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Estimado (a) NOMBRE APELLIDO", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void el_monto_cambia_pero_conserva_sus_separadores()
    {
        // Es lo que el parser tiene que aprender a leer: la coma de miles y el
        // punto decimal, cada uno en su sitio. Una muestra con `RD$XXX` no
        // sirve para escribir nada.
        string limpio = Sujeto.Redact(Consumo);

        Assert.DoesNotContain("2,450.75", limpio, StringComparison.Ordinal);
        Assert.Contains("RD$1,111.11", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void la_fecha_no_se_toca()
    {
        // El formato de la fecha es justo lo que hay que aprender, y una
        // muestra que miente sobre él produce un parser que falla con el correo
        // real. Redactar de más parece la opción segura y no lo es.
        Assert.Contains("26/07/2026", Sujeto.Redact(Consumo), StringComparison.Ordinal);
    }

    [Fact]
    public void el_comercio_y_el_estatus_no_se_tocan()
    {
        string limpio = Sujeto.Redact(Consumo);

        Assert.Contains("SM NACIONAL", limpio, StringComparison.Ordinal);
        Assert.Contains("Aprobada", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void los_encabezados_de_la_tabla_se_conservan()
    {
        // Son la referencia para saber qué columna es cuál cuando la tabla
        // llega aplanada a texto y el comercio parte de línea.
        string limpio = Sujeto.Redact(Consumo);

        Assert.Contains("Monto", limpio, StringComparison.Ordinal);
        Assert.Contains("Comercio", limpio, StringComparison.Ordinal);
        Assert.Contains("Estatus", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void los_guiones_invisibles_no_esconden_un_correo()
    {
        // El Popular siembra el texto de U+00AD: doscientos en cuarenta
        // archivos. Sin quitarlos, una dirección escrita así sobrevive a la
        // redacción porque el patrón no la reconoce.
        const string conGuiones = "escriba a voz­delcliente­@bpd­.com";

        string limpio = Sujeto.Redact(conGuiones);

        Assert.DoesNotContain("bpd", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finanzas@ejemplo.do", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void redactar_dos_veces_da_lo_mismo()
    {
        // Hace falta para poder volver a pasar el redactor sobre muestras ya
        // capturadas cuando se le añade un patrón, sin que el resultado se
        // degrade en cada pasada.
        string unaVez = Sujeto.Redact(Consumo);

        Assert.Equal(unaVez, Sujeto.Redact(unaVez));
    }

    [Fact]
    public void sin_terminos_personales_las_reglas_por_patron_siguen_actuando()
    {
        var sinNombre = new Redactor(RedactionSettings.Of(null));

        Assert.DoesNotContain("2074", sinNombre.Redact(Consumo), StringComparison.Ordinal);
    }
}
