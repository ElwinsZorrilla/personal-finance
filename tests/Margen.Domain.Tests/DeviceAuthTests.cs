using System.Text;
using Margen.Domain;

namespace Margen.Domain.Tests;

/// <summary>
/// El mensaje que se firma es un contrato entre tres partes: quien firma en el
/// iPhone, quien verifica en el API y quien escribe la prueba de integración.
/// Estas pruebas fijan el formato byte a byte, porque el cliente Flutter tendrá
/// que construirlo igual y allí no hay forma de compartir este código.
/// </summary>
public sealed class DeviceAuthTests
{
    private static readonly Guid Dispositivo =
        Guid.Parse("0198f3c1-0000-7000-8000-abcdefabcdef");

    [Fact]
    public void el_mensaje_firmado_lleva_contexto_dispositivo_y_reto_en_ese_orden()
    {
        byte[] carga = DeviceAuth.BuildSigningPayload(Dispositivo, "reto-de-prueba");

        Assert.Equal(
            "margen-device-auth-v1\n0198f3c1-0000-7000-8000-abcdefabcdef\nreto-de-prueba",
            Encoding.UTF8.GetString(carga));
    }

    [Fact]
    public void el_identificador_va_en_minusculas_con_guiones()
    {
        // Formato «D». Si el cliente usara «N» —sin guiones— la firma sería
        // válida criptográficamente y el servidor la rechazaría, y el error
        // diría «firma inválida», que es lo único que no fue.
        byte[] carga = DeviceAuth.BuildSigningPayload(Dispositivo, "x");

        Assert.Contains(
            "0198f3c1-0000-7000-8000-abcdefabcdef",
            Encoding.UTF8.GetString(carga),
            StringComparison.Ordinal);
    }

    [Fact]
    public void dos_dispositivos_distintos_producen_mensajes_distintos_con_el_mismo_reto()
    {
        // Es lo que impide presentar la firma de un dispositivo en nombre de
        // otro.
        byte[] uno = DeviceAuth.BuildSigningPayload(Guid.Parse("11111111-1111-1111-1111-111111111111"), "reto");
        byte[] otro = DeviceAuth.BuildSigningPayload(Guid.Parse("22222222-2222-2222-2222-222222222222"), "reto");

        Assert.NotEqual(uno, otro);
    }

    [Fact]
    public void dos_retos_distintos_producen_mensajes_distintos_para_el_mismo_dispositivo()
    {
        byte[] uno = DeviceAuth.BuildSigningPayload(Dispositivo, "reto-a");
        byte[] otro = DeviceAuth.BuildSigningPayload(Dispositivo, "reto-b");

        Assert.NotEqual(uno, otro);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void un_reto_vacio_se_rechaza_en_lugar_de_firmar_una_cadena_a_medias(string? reto)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => DeviceAuth.BuildSigningPayload(Dispositivo, reto!));
    }

    [Fact]
    public void la_etiqueta_de_contexto_lleva_version()
    {
        // Cambiar el formato mañana tiene que ser una versión nueva y no una
        // ambigüedad entre dos clientes que firman distinto.
        Assert.EndsWith("-v1", DeviceAuth.Context, StringComparison.Ordinal);
    }
}

public sealed class ScopesTests
{
    [Fact]
    public void los_alcances_conocidos_son_exactamente_dos()
    {
        Assert.Equal([Scopes.Full, Scopes.CashCreate], Scopes.All);
    }

    [Fact]
    public void un_alcance_inventado_no_es_conocido()
    {
        // Emitir un token con un alcance que ninguna política mira produciría
        // un token que parece restringido y no restringe nada.
        Assert.True(Scopes.IsKnown(Scopes.Full));
        Assert.True(Scopes.IsKnown(Scopes.CashCreate));
        Assert.False(Scopes.IsKnown("admin"));
        Assert.False(Scopes.IsKnown(""));
    }
}
