namespace Margen.Ingest.Tests;

/// <summary>
/// Lo que sale del redactor es lo que se versiona en `docs/muestras/`. Estas
/// pruebas son la garantía de que ahí no queda nada personal, y corren sin
/// conectarse a ningún buzón.
/// </summary>
public sealed class RedactorTests
{
    private static Redactor Con(params string[] terminos) =>
        new(new RedactionSettings(terminos));

    [Fact]
    public void el_nombre_desaparece()
    {
        string limpio = Con("Maria Perez")
            .Redact("Estimado Maria Perez, su compra fue aprobada.");

        Assert.DoesNotContain("Maria", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Perez", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOMBRE APELLIDO", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void el_saludo_informal_del_segundo_banco_tambien_lleva_nombre()
    {
        // Qik saluda con «¡Hola NOMBRE APELLIDO!» y no usa ninguna de las
        // etiquetas formales. La lista de términos personales cubrió dos de las
        // tres palabras del nombre real, y **la tercera se escribió tal cual en
        // el disco**: un apellido que nadie había puesto en la lista.
        string limpio = Con("Maria Perez")
            .Redact("<strong>¡Hola MARIA PEREZ GOMEZ!</strong>");

        Assert.DoesNotContain("GOMEZ", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PEREZ", limpio, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hola", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void el_saludo_con_coma_tambien()
    {
        // El mismo banco usa las dos formas en plantillas distintas.
        string limpio = Con("Maria").Redact("<td>¡Hola, MARIA PEREZ!</td>");

        Assert.DoesNotContain("PEREZ", limpio, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void el_saludo_no_se_come_el_texto_que_sigue()
    {
        // El tope existe para que un correo mal formado no borre el documento.
        string limpio = Con("Maria")
            .Redact("¡Hola MARIA!\nSe hizo una transacción en PEDIDOSYA");

        Assert.Contains("PEDIDOSYA", limpio, StringComparison.Ordinal);
        Assert.Contains("transacción", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void el_nombre_desaparece_sin_importar_las_mayusculas()
    {
        string limpio = Con("Maria Perez").Redact("MARIA PEREZ compró algo.");

        Assert.DoesNotContain("MARIA", limpio, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void las_direcciones_de_correo_se_sustituyen()
    {
        string limpio = Con().Redact("Enviado a maria.perez@gmail.com desde el banco.");

        Assert.DoesNotContain("gmail.com", limpio, StringComparison.Ordinal);
        Assert.Contains("finanzas@ejemplo.do", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void los_cuatro_digitos_de_la_tarjeta_se_cambian_conservando_la_mascara()
    {
        // La máscara es parte del formato que el parser tiene que reconocer.
        Assert.Contains("****1234", Con().Redact("Tarjeta: ****8842"), StringComparison.Ordinal);
        Assert.Contains("xxxx1234", Con().Redact("Tarjeta: xxxx8842"), StringComparison.Ordinal);
        Assert.DoesNotContain("8842", Con().Redact("Tarjeta: ****8842"), StringComparison.Ordinal);
    }

    [Fact]
    public void los_montos_conservan_sus_separadores_en_el_mismo_sitio()
    {
        // Es la propiedad que hace que la muestra sirva. La coma de miles y el
        // punto decimal son justo lo que el parser tiene que aprender a leer, y
        // son lo que ya se leyó mal tres veces en este proyecto.
        Assert.Equal("Monto: RD$ 1,111.11", Con().Redact("Monto: RD$ 2,450.00"));
        Assert.Equal("Monto: RD$ 111.11", Con().Redact("Monto: RD$ 450.75"));
        Assert.Equal("Monto: RD$ 1,111,111.11", Con().Redact("Monto: RD$ 1,234,567.89"));
    }

    [Fact]
    public void un_monto_al_estilo_europeo_tambien_conserva_su_forma()
    {
        // Si el banco escribe 2.450,00 el parser tendrá que leerlo así, y la
        // muestra tiene que decirlo.
        Assert.Equal("Monto: RD$ 1.111,11", Con().Redact("Monto: RD$ 2.450,00"));
    }

    [Fact]
    public void la_referencia_conserva_su_longitud_y_sus_clases()
    {
        string limpio = Con().Redact("Referencia: ABC-123456");

        Assert.Contains("Referencia: AAA-999999", limpio, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void un_monto_con_codigo_de_moneda_tampoco_sobrevive()
    {
        // Banreservas escribe `DOP 9,876.54` en vez de `RD$ 9,876.54`. El
        // patrón solo conocía los símbolos, y el importe real de una compra
        // quedó escrito en el disco.
        string limpio = Con().Redact("""
            Monto:
            DOP 9,876.54
            """);

        Assert.DoesNotContain("9,876.54", limpio, StringComparison.Ordinal);
        Assert.Contains("DOP 1,111.11", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void el_codigo_de_moneda_se_conserva_porque_es_formato()
    {
        // El parser tiene que leer «DOP» para saber que son pesos. Borrarlo
        // dejaría la muestra sin la moneda.
        Assert.Contains("USD", Con().Redact("USD 250.00"), StringComparison.Ordinal);
    }

    [Fact]
    public void el_numero_de_aprobacion_tampoco_sobrevive()
    {
        // Otra etiqueta que el patrón de referencia no conocía, y el número
        // real de una transacción quedó en el disco.
        string limpio = Con().Redact("""
            Número de aprobación:
            081234
            """);

        Assert.DoesNotContain("081234", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void una_referencia_partida_entre_celdas_tampoco_sobrevive()
    {
        // Banreservas escribe `<td>Número de aprobación:</td><td>081234</td>`.
        // Para una expresión regular sobre el HTML crudo eso no es «Número de
        // aprobación: 081234», y el número real de la transacción sobrevivía.
        //
        // Es el mismo caso que «terminada en» partido entre celdas, y por eso
        // hace falta la pasada sobre el texto sin etiquetas.
        string limpio = Con().Redact(
            "<tr><td>Número de aprobación:</td><td>081234</td></tr>");

        Assert.DoesNotContain("081234", limpio, StringComparison.Ordinal);
        Assert.Contains("999999", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void la_autorizacion_tambien_se_redacta()
    {
        Assert.DoesNotContain(
            "998877",
            Con().Redact("Autorización: 998877"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void una_cadena_larga_de_digitos_no_sobrevive()
    {
        // Un número de cuenta, una cédula, un teléfono: lo que el resto de
        // reglas no reconoció por su etiqueta.
        string limpio = Con().Redact("Cuenta 9601234567890 de NOMBRE");

        Assert.DoesNotContain("9601234567890", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void la_estructura_del_correo_se_conserva_entera()
    {
        // Lo que importa de la muestra es el formato: etiquetas, orden de las
        // líneas y espacios. Si el redactor los tocara, la muestra dejaría de
        // servir para escribir el parser.
        const string original = """
            Estimado NOMBRE,

            Monto:     RD$ 2,450.00
            Tarjeta:   ****8842
            Comercio:  SUPERMERCADO NACIONAL SUC 12
            Fecha:     15/03/2026 23:30
            Referencia: ABC-123456
            """;

        string limpio = Con().Redact(original);

        Assert.Equal(
            original.Split('\n').Length,
            limpio.Split('\n').Length);

        foreach (string etiqueta in new[]
        {
            "Monto:     RD$",
            "Tarjeta:   ****",
            "Comercio:  SUPERMERCADO NACIONAL SUC",
            "Fecha:",
            "Referencia:",
        })
        {
            Assert.Contains(etiqueta, limpio, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void la_fecha_no_se_estropea()
    {
        // La fecha es formato, no dato personal, y el parser tiene que
        // aprender a leerla tal cual.
        string limpio = Con().Redact("Fecha: 15/03/2026 23:30");

        Assert.Contains("15/03/2026 23:30", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void el_comercio_se_conserva()
    {
        // El nombre del comercio no es un dato personal y es lo que el parser
        // extrae. Borrarlo dejaría la muestra sin la mitad de lo que enseña.
        string limpio = Con("Maria").Redact("Comercio: SUPERMERCADO NACIONAL");

        Assert.Contains("SUPERMERCADO NACIONAL", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void sin_terminos_personales_sigue_redactando_lo_que_reconoce_por_patron()
    {
        // Quien captura puede olvidarse de dar su nombre. Las reglas por patrón
        // no dependen de eso.
        string limpio = Con().Redact("Correo: alguien@gmail.com, tarjeta ****8842");

        Assert.DoesNotContain("alguien@gmail.com", limpio, StringComparison.Ordinal);
        Assert.DoesNotContain("8842", limpio, StringComparison.Ordinal);
    }

    [Fact]
    public void un_cuerpo_vacio_no_revienta()
    {
        Assert.Equal(string.Empty, Con().Redact(string.Empty));
    }

    [Fact]
    public void los_terminos_se_leen_de_una_lista_separada_por_comas()
    {
        RedactionSettings settings = RedactionSettings.Of("Maria Perez, Juana Pérez ,");

        Assert.Equal(2, settings.PersonalTerms.Count);
        Assert.Contains("Juana Pérez", settings.PersonalTerms);
    }

    [Fact]
    public void sin_configuracion_la_lista_queda_vacia_y_no_lanza()
    {
        Assert.Empty(RedactionSettings.Of(null).PersonalTerms);
        Assert.Empty(RedactionSettings.Of(string.Empty).PersonalTerms);
    }
}
