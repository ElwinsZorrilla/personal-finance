using Margen.Classify;
using Margen.Domain;

namespace Margen.Classify.Tests;

public sealed class AnomaliesTests
{
    private static readonly Guid Movimiento = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid Recurrente = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

    private static Outcome<NormalRange> Rango(params long[] historia) =>
        NormalRanges.Of([.. historia.Select(c => new Money(c))]);

    // ---------- Gasto inusual ----------

    [Fact]
    public void un_cargo_muy_por_encima_de_lo_normal_avisa()
    {
        Anomaly? a = Anomalies.UnusualAmount(
            Movimiento,
            "SM NACIONAL CHARLES",
            new Money(4_000_000),
            Rango(30_000, 30_000, 30_000, 30_000, 30_000));

        Assert.NotNull(a);
        Assert.Equal(AlertKind.UnusualAmount, a.Value.Kind);
        Assert.Contains("SM NACIONAL CHARLES", a.Value.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void un_cargo_normal_no_avisa()
    {
        Assert.Null(Anomalies.UnusualAmount(
            Movimiento, "SM NACIONAL", new Money(31_000),
            Rango(30_000, 30_000, 30_000, 30_000, 30_000)));
    }

    [Fact]
    public void sin_historial_suficiente_no_avisa()
    {
        // El camino más frecuente los primeros meses. Barato y silencioso.
        Assert.Null(Anomalies.UnusualAmount(
            Movimiento, "SM NACIONAL", new Money(4_000_000), Rango(30_000, 30_000)));
    }

    [Fact]
    public void un_cargo_mas_barato_de_lo_normal_no_avisa()
    {
        // Gastar menos no es un problema de nadie, y avisarlo enseña a ignorar
        // la lista de alertas.
        Assert.Null(Anomalies.UnusualAmount(
            Movimiento, "SM NACIONAL", new Money(1),
            Rango(30_000, 30_000, 30_000, 30_000, 30_000)));
    }

    [Fact]
    public void la_clave_del_gasto_inusual_es_el_movimiento()
    {
        // Mirar dos veces el mismo movimiento da la misma clave, y el índice
        // único de alertas sin resolver hace el resto.
        Anomaly a = Anomalies.UnusualAmount(
            Movimiento, "X", new Money(4_000_000),
            Rango(30_000, 30_000, 30_000, 30_000, 30_000))!.Value;

        Anomaly b = Anomalies.UnusualAmount(
            Movimiento, "X", new Money(4_000_000),
            Rango(30_000, 30_000, 30_000, 30_000, 30_000))!.Value;

        Assert.Equal(a.DedupeKey, b.DedupeKey);
    }

    [Fact]
    public void un_rango_nulo_lanza()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Anomalies.UnusualAmount(Movimiento, "X", Money.Zero, null!));
        Assert.Throws<ArgumentNullException>(() =>
            Anomalies.UnusualAmount(Movimiento, null!, Money.Zero, Rango(1, 2, 3, 4, 5)));
    }

    // ---------- Suscripción que cambia de precio ----------

    [Fact]
    public void una_suscripcion_que_sube_avisa_y_es_urgente()
    {
        Anomaly a = Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(120_000), new Money(150_000))!.Value;

        Assert.Equal(AlertKind.SubscriptionChange, a.Kind);
        Assert.True(a.IsUrgent);
        Assert.Contains("subió", a.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void una_suscripcion_que_baja_avisa_y_no_es_urgente()
    {
        // Una bajada es una buena noticia que puede esperar.
        Anomaly a = Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(150_000), new Money(120_000))!.Value;

        Assert.False(a.IsUrgent);
        Assert.Contains("bajó", a.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void el_mismo_precio_no_avisa()
    {
        Assert.Null(Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(120_000), new Money(120_000)));
    }

    [Fact]
    public void una_variacion_pequena_no_avisa()
    {
        // Un cargo en dólares que cambia de tasa parece una subida y no lo es.
        // 120 000 -> 122 000 es un 1,67 %.
        Assert.Null(Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(120_000), new Money(122_000)));
    }

    [Fact]
    public void un_esperado_de_cero_no_avisa()
    {
        // No existe «cuánto subió» respecto de nada, y un infinito por ciento
        // comparado contra un umbral avisa siempre.
        Assert.Null(Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", Money.Zero, new Money(120_000)));
    }

    [Fact]
    public void una_segunda_subida_es_una_alerta_distinta()
    {
        // El importe entra en la clave. Sin eso, la segunda subida sería
        // invisible por culpa de la primera.
        Anomaly primera = Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(120_000), new Money(150_000))!.Value;

        Anomaly segunda = Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(150_000), new Money(180_000))!.Value;

        Assert.NotEqual(primera.DedupeKey, segunda.DedupeKey);
    }

    [Fact]
    public void la_misma_subida_vista_dos_veces_es_la_misma_alerta()
    {
        Anomaly a = Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(120_000), new Money(150_000))!.Value;

        Anomaly b = Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(120_000), new Money(150_000))!.Value;

        Assert.Equal(a.DedupeKey, b.DedupeKey);
    }

    [Fact]
    public void la_variacion_sale_en_el_detalle()
    {
        Anomaly a = Anomalies.SubscriptionPriceChange(
            Recurrente, "ChatGPT", new Money(120_000), new Money(132_000))!.Value;

        Assert.Contains("+10 %", a.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void una_etiqueta_nula_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => Anomalies.SubscriptionPriceChange(
            Recurrente, null!, new Money(1), new Money(2)));
    }

    // ---------- Recurrente que no llegó ----------

    [Fact]
    public void una_factura_vencida_hace_dias_avisa()
    {
        Anomaly a = Anomalies.MissingRecurring(
            Recurrente,
            "Alquiler",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 10),
            alreadyPaid: false)!.Value;

        Assert.Equal(AlertKind.MissingRecurring, a.Kind);
        Assert.True(a.IsUrgent);
    }

    [Fact]
    public void una_factura_ya_pagada_no_avisa()
    {
        Assert.Null(Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), alreadyPaid: true));
    }

    [Fact]
    public void el_dia_del_vencimiento_no_avisa()
    {
        // El banco no notifica el mismo día. Avisar el día del vencimiento es
        // avisar en falso casi siempre.
        Assert.Null(Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), alreadyPaid: false));
    }

    [Fact]
    public void dentro_de_los_dias_de_cortesia_no_avisa()
    {
        Assert.Null(Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1).AddDays(Anomalies.GraceDays),
            alreadyPaid: false));
    }

    [Fact]
    public void justo_pasada_la_cortesia_avisa()
    {
        Assert.NotNull(Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1).AddDays(Anomalies.GraceDays + 1),
            alreadyPaid: false));
    }

    [Fact]
    public void una_factura_futura_no_avisa()
    {
        Assert.Null(Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 10), alreadyPaid: false));
    }

    [Fact]
    public void la_factura_de_cada_mes_es_una_alerta_distinta()
    {
        // El vencimiento entra en la clave: la de agosto no es la de julio.
        Anomaly julio = Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 10), false)!.Value;

        Anomaly agosto = Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), false)!.Value;

        Assert.NotEqual(julio.DedupeKey, agosto.DedupeKey);
    }

    [Fact]
    public void mirar_el_reloj_dos_veces_no_genera_dos_alertas()
    {
        // Mismo vencimiento, dos días distintos de observación: misma clave.
        Anomaly hoy = Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), false)!.Value;

        Anomaly manana = Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 11), false)!.Value;

        Assert.Equal(hoy.DedupeKey, manana.DedupeKey);
    }

    [Fact]
    public void la_clave_lleva_la_fecha_local_del_vencimiento()
    {
        // La decisión de zona horaria de esta detección: el día que ve el
        // usuario, no el que sale en UTC.
        Anomaly a = Anomalies.MissingRecurring(
            Recurrente, "Alquiler",
            new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 10), false)!.Value;

        Assert.Contains("2026-08-31", a.DedupeKey, StringComparison.Ordinal);
    }

    [Fact]
    public void una_etiqueta_nula_en_el_recurrente_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => Anomalies.MissingRecurring(
            Recurrente, null!, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), false));
    }
}
