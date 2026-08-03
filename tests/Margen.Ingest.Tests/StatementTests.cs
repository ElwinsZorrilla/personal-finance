using Margen.Domain;
using Margen.Ingest.Statements;

namespace Margen.Ingest.Tests;

/// <summary>
/// La lectura de un estado de cuenta en CSV.
/// </summary>
/// <remarks>
/// El mapeo de columnas existe para **no tener que conocer el formato de
/// antemano**: el humano dice qué columna es cuál y ve una vista previa antes de
/// importar. Con varios bancos es la única forma que escala, y por eso estas
/// pruebas usan formatos distintos a propósito.
/// </remarks>
public sealed class StatementTests
{
    private static StatementMapping Mapeo(
        int? monto = 3,
        int? cargo = null,
        int? abono = null,
        DecimalStyle decimales = DecimalStyle.Point,
        bool invertir = false,
        char? separador = null,
        int saltar = 1,
        string formato = "dd/MM/yyyy") =>
        new(separador, saltar, 0, formato, 1, monto, cargo, abono, decimales, invertir);

    // ---------- El separador ----------

    [Fact]
    public void detecta_la_coma()
    {
        Assert.Equal(',', Csv.DetectDelimiter("a,b,c\n1,2,3"));
    }

    [Fact]
    public void detecta_el_punto_y_coma()
    {
        Assert.Equal(';', Csv.DetectDelimiter("a;b;c\n1;2;3"));
    }

    [Fact]
    public void una_descripcion_con_comas_no_gana_al_separador_de_verdad()
    {
        // La coma aparece más veces que el punto y coma, y aun así el archivo
        // está separado por punto y coma. Contar apariciones lo leería corrido.
        const string csv = """
            Fecha;Descripcion;Monto
            01/02/2026;SUPERMERCADO NACIONAL, SANTIAGO, RD;-1200.00
            02/02/2026;UBER EATS, SANTO DOMINGO, RD;-450.00
            """;

        Assert.Equal(';', Csv.DetectDelimiter(csv));
    }

    [Fact]
    public void las_comillas_protegen_el_separador()
    {
        List<string[]> filas = Csv.Parse("a,\"uno, dos\",c", ',');

        Assert.Equal(3, filas[0].Length);
        Assert.Equal("uno, dos", filas[0][1]);
    }

    [Fact]
    public void dos_comillas_seguidas_son_una_comilla()
    {
        List<string[]> filas = Csv.Parse("a,\"dijo \"\"hola\"\"\",c", ',');

        Assert.Equal("dijo \"hola\"", filas[0][1]);
    }

    // ---------- La codificación ----------

    [Fact]
    public void un_archivo_en_utf8_se_lee()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("RODRÍGUEZ");

        Assert.Equal("RODRÍGUEZ", Csv.Decode(bytes));
    }

    [Fact]
    public void un_archivo_con_marca_de_orden_no_arrastra_el_caracter_invisible()
    {
        // La marca al principio se pega a la primera celda de la cabecera, y a
        // partir de ahí el nombre de esa columna no coincide con nada.
        byte[] bytes = [.. new byte[] { 0xEF, 0xBB, 0xBF },
            .. System.Text.Encoding.UTF8.GetBytes("Fecha")];

        Assert.Equal("Fecha", Csv.Decode(bytes));
    }

    [Fact]
    public void un_archivo_en_latin1_no_se_lee_como_basura()
    {
        // Los bancos exportan con frecuencia en la página de códigos de Windows.
        // Leer esos bytes como UTF-8 lanza o produce caracteres de reemplazo.
        byte[] bytes = System.Text.Encoding.Latin1.GetBytes("RODRÍGUEZ");

        Assert.Equal("RODRÍGUEZ", Csv.Decode(bytes));
    }

    // ---------- El monto ----------

    [Theory]
    [InlineData("1,234.56", DecimalStyle.Point, 123_456)]
    [InlineData("RD$ 1,234.56", DecimalStyle.Point, 123_456)]
    [InlineData("-1,234.56", DecimalStyle.Point, -123_456)]
    [InlineData("(1,234.56)", DecimalStyle.Point, -123_456)]
    [InlineData("1.234,56", DecimalStyle.Comma, 123_456)]
    [InlineData("1234", DecimalStyle.Point, 123_400)]
    [InlineData("0.05", DecimalStyle.Point, 5)]
    [InlineData("1234.5", DecimalStyle.Point, 123_450)]
    public void el_monto_se_lee_entero(string texto, DecimalStyle estilo, long centavos)
    {
        // Sin coma flotante en ningún punto: unidades por cien más centavos.
        Assert.Equal(new Money(centavos), StatementReader.ReadCents(texto, estilo));
    }

    [Fact]
    public void el_estilo_decimal_cambia_lo_que_significa_una_coma()
    {
        // `1.234,56` leído con el estilo equivocado da 1,23. Es el error que
        // convierte un cargo de mil pesos en uno de uno.
        Assert.Equal(new Money(123_456), StatementReader.ReadCents("1.234,56", DecimalStyle.Comma));
        Assert.NotEqual(new Money(123_456), StatementReader.ReadCents("1.234,56", DecimalStyle.Point));
    }

    // ---------- Leer el archivo ----------

    [Fact]
    public void lee_un_estado_de_cuenta_de_una_columna_con_signo()
    {
        const string csv = """
            Fecha,Descripcion,Referencia,Monto
            26/07/2026,SM NACIONAL CHARLES,A1,-1234.56
            27/07/2026,DEPOSITO NOMINA,A2,45000.00
            """;

        StatementParse parse = StatementReader.Read(csv, Mapeo());

        Assert.Empty(parse.Rejected);
        Assert.Equal(2, parse.Lines.Count);

        Assert.Equal(new Money(123_456), parse.Lines[0].Amount);
        Assert.Equal(TxDirection.Outflow, parse.Lines[0].Direction);
        Assert.Equal(new DateOnly(2026, 7, 26), parse.Lines[0].Date);

        Assert.Equal(TxDirection.Inflow, parse.Lines[1].Direction);
    }

    [Fact]
    public void lee_un_estado_de_cuenta_de_dos_columnas()
    {
        // Cargos en una, abonos en otra. Es lo más frecuente aquí y no tiene
        // ninguna ambigüedad de signo.
        const string csv = """
            Fecha;Concepto;Cargo;Abono
            26/07/2026;SM NACIONAL;1,234.56;
            27/07/2026;DEPOSITO;;45,000.00
            """;

        StatementParse parse = StatementReader.Read(
            csv, Mapeo(monto: null, cargo: 2, abono: 3));

        Assert.Empty(parse.Rejected);
        Assert.Equal(TxDirection.Outflow, parse.Lines[0].Direction);
        Assert.Equal(new Money(123_456), parse.Lines[0].Amount);
        Assert.Equal(TxDirection.Inflow, parse.Lines[1].Direction);
        Assert.Equal(new Money(4_500_000), parse.Lines[1].Amount);
    }

    [Fact]
    public void el_signo_invertido_no_convierte_los_gastos_en_ingresos()
    {
        // Hay bancos que escriben los cargos en positivo. Sin la opción, un
        // estado de cuenta entero entraría con todo al revés y el saldo saldría
        // al doble.
        const string csv = """
            Fecha,Descripcion,Referencia,Monto
            26/07/2026,SM NACIONAL,A1,1234.56
            """;

        StatementParse parse = StatementReader.Read(csv, Mapeo(invertir: true));

        Assert.Equal(TxDirection.Outflow, parse.Lines[0].Direction);
    }

    [Fact]
    public void el_monto_es_siempre_positivo_venga_como_venga()
    {
        // La dirección la lleva `Direction`. Guardar el signo en el monto
        // obliga a recordar el convenio en cada consulta.
        const string csv = """
            Fecha,Descripcion,Referencia,Monto
            26/07/2026,SM NACIONAL,A1,-1234.56
            """;

        Assert.True(StatementReader.Read(csv, Mapeo()).Lines[0].Amount.Cents > 0);
    }

    // ---------- Lo que no se entiende se dice ----------

    [Fact]
    public void una_fila_con_la_fecha_rota_se_devuelve_con_su_numero_y_su_motivo()
    {
        // Un importador que se come en silencio las filas raras deja un estado
        // de cuenta que parece cuadrado y no lo está.
        const string csv = """
            Fecha,Descripcion,Referencia,Monto
            26/07/2026,SM NACIONAL,A1,-1234.56
            SALDO ANTERIOR,,,,
            28/07/2026,UBER,A3,-450.00
            """;

        StatementParse parse = StatementReader.Read(csv, Mapeo());

        Assert.Equal(2, parse.Lines.Count);
        Assert.Single(parse.Rejected);
        Assert.Equal(3, parse.Rejected[0].LineNumber);
        Assert.Contains("fecha", parse.Rejected[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void una_fila_con_cargo_y_abono_a_la_vez_se_rechaza()
    {
        const string csv = """
            Fecha;Concepto;Cargo;Abono
            26/07/2026;RARO;100.00;200.00
            """;

        StatementParse parse = StatementReader.Read(
            csv, Mapeo(monto: null, cargo: 2, abono: 3));

        Assert.Empty(parse.Lines);
        Assert.Single(parse.Rejected);
    }

    [Fact]
    public void el_formato_de_fecha_es_explicito_y_no_se_adivina()
    {
        // `01/02/2026` es el 1 de febrero o el 2 de enero según el banco, y las
        // dos lecturas son plausibles. Adivinar mete movimientos en el período
        // equivocado.
        const string csv = """
            Fecha,Descripcion,Referencia,Monto
            01/02/2026,ALGO,A1,-100.00
            """;

        Assert.Equal(
            new DateOnly(2026, 2, 1),
            StatementReader.Read(csv, Mapeo(formato: "dd/MM/yyyy")).Lines[0].Date);

        Assert.Equal(
            new DateOnly(2026, 1, 2),
            StatementReader.Read(csv, Mapeo(formato: "MM/dd/yyyy")).Lines[0].Date);
    }

    [Fact]
    public void un_mapeo_sin_columna_de_monto_no_es_usable()
    {
        Assert.False(Mapeo(monto: null).IsUsable);
        Assert.True(Mapeo().IsUsable);
    }

    [Fact]
    public void los_argumentos_nulos_lanzan()
    {
        Assert.Throws<ArgumentNullException>(() => StatementReader.Read(null!, Mapeo()));
        Assert.Throws<ArgumentNullException>(() => StatementReader.Read("a", null!));
        Assert.Throws<ArgumentNullException>(() => Csv.DetectDelimiter(null!));
        Assert.Throws<ArgumentNullException>(() => Csv.Decode(null!));
    }
}
