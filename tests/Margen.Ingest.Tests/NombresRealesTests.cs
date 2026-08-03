namespace Margen.Ingest.Tests;

/// <summary>
/// Las tres formas de nombre que se colaron en la segunda captura real.
/// </summary>
/// <remarks>
/// Los nombres de abajo son inventados, pero **la forma es la que traían los
/// correos**: el banco escribe el nombre del titular con un apellido de más, lo
/// repite en otro orden dentro del cuerpo HTML, y en una transferencia añade el
/// del beneficiario, que es el dato personal de un tercero.
///
/// La primera versión buscaba la frase exacta que se le daba, así que las tres
/// pasaron enteras.
/// </remarks>
public sealed class NombresRealesTests
{
    /// <summary>Lo que alguien pondría en `Muestras__DatosPersonales`.</summary>
    private static readonly Redactor Sujeto = new(RedactionSettings.Of("Fulano Mengano"));

    [Fact]
    public void un_apellido_de_mas_no_sobrevive()
    {
        // El banco escribe «SR FULANO MENGANO ZUTANO»; se le dio «Fulano
        // Mengano». La frase exacta no está en el texto, y la versión anterior
        // dejaba el tercer apellido intacto.
        string limpio = Sujeto.Redact("Estimado (a) SR FULANO MENGANO ZUTANO");

        Assert.DoesNotContain("FULANO", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MENGANO", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ZUTANO", limpio, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void el_nombre_en_otro_orden_dentro_de_html_tampoco()
    {
        // La forma del cuerpo HTML: apellidos primero e inicial al final.
        const string html =
            "<p style='text-align:justify;'><b>Estimado (a)&nbsp;MENGANO ZUTANO F&nbsp;</b><br>";

        string limpio = Sujeto.Redact(html);

        Assert.DoesNotContain("MENGANO", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ZUTANO", limpio, StringComparison.OrdinalIgnoreCase);

        // Y las etiquetas HTML siguen en pie: son formato, no dato.
        Assert.Contains("<b>", limpio, StringComparison.Ordinal);
        Assert.Contains("<br>", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void el_nombre_de_un_tercero_tampoco_sobrevive()
    {
        // El caso más serio: una transferencia lleva el nombre del
        // beneficiario. Ninguna lista de términos lo prevé —quien captura
        // conoce su nombre, no el de a quién le transfirió dinero— así que lo
        // que lo quita es la etiqueta que lo precede.
        string limpio = Sujeto.Redact("Beneficiario: CARMEN MILAGROS PERALTA DE SANTOS");

        Assert.DoesNotContain("CARMEN", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PERALTA", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Beneficiario: NOMBRE APELLIDO", limpio, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Titular: JUAN PEREZ")]
    [InlineData("Destinatario: JUAN PEREZ")]
    [InlineData("Ordenante: JUAN PEREZ")]
    [InlineData("A nombre de: JUAN PEREZ")]
    public void las_demas_etiquetas_de_persona_tambien(string linea)
    {
        Assert.DoesNotContain("PEREZ", Sujeto.Redact(linea), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void el_comercio_no_se_confunde_con_un_nombre()
    {
        // «Comercio» no es etiqueta de persona. Si lo fuera, el parser se
        // quedaría sin el único dato que tiene que extraer de esa línea.
        Assert.Contains(
            "SUPERMERCADO NACIONAL CHARLES",
            Sujeto.Redact("Comercio: SUPERMERCADO NACIONAL CHARLES"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void las_particulas_cortas_no_destrozan_el_texto()
    {
        // Con «de» o «la» en la lista, cualquier frase quedaría ilegible y la
        // muestra no serviría para escribir nada. El mínimo de cuatro letras
        // las deja fuera.
        var conParticulas = new Redactor(RedactionSettings.Of("Juan de la Cruz"));

        Assert.Contains(
            "DE LA SALUD",
            conParticulas.Redact("Comercio: FARMACIA DE LA SALUD"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void redactar_dos_veces_sigue_dando_lo_mismo()
    {
        const string cuerpo =
            "Estimado (a) SR FULANO MENGANO ZUTANO\nBeneficiario: ANA PERALTA";

        string unaVez = Sujeto.Redact(cuerpo);

        Assert.Equal(unaVez, Sujeto.Redact(unaVez));
    }
}
