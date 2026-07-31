using System.Collections.Immutable;
using Margen.Budget;
using Margen.Domain;

namespace Margen.Budget.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void con_tres_periodos_pesa_cincuenta_treinta_veinte()
    {
        // 30,000 × 0.5 + 20,000 × 0.3 + 10,000 × 0.2 = 15,000 + 6,000 + 2,000
        Money promedio = HistoricalBaseline.Average([
            new Money(3_000_000),
            new Money(2_000_000),
            new Money(1_000_000),
        ]).Value;

        Assert.Equal(new Money(2_300_000), promedio);
    }

    [Fact]
    public void con_dos_periodos_los_pesos_se_renormalizan_sobre_los_que_hay()
    {
        // 50/80 y 30/80, no 50/100 y 30/100. Tratar el tercero como cero haría
        // que un usuario con dos meses viera una base un 20 % menor de lo que
        // gasta, y esa cifra propone el presupuesto del período siguiente.
        Money promedio = HistoricalBaseline.Average([
            new Money(4_000_000),
            new Money(4_000_000),
        ]).Value;

        Assert.Equal(new Money(4_000_000), promedio);
    }

    [Fact]
    public void con_dos_periodos_distintos_el_reciente_pesa_cinco_octavos()
    {
        // (8,000 × 50 + 0 × 30) / 80 = 5,000
        Money promedio = HistoricalBaseline.Average([
            new Money(800_000),
            Money.Zero,
        ]).Value;

        Assert.Equal(new Money(500_000), promedio);
    }

    [Fact]
    public void con_un_solo_periodo_el_promedio_es_ese_periodo()
    {
        Money promedio = HistoricalBaseline.Average([new Money(1_234_567)]).Value;

        Assert.Equal(new Money(1_234_567), promedio);
    }

    [Fact]
    public void sin_periodos_cerrados_no_hay_respuesta_en_lugar_de_cero()
    {
        // Devolver cero sería inventar una historia que no existe, y sobre esa
        // cifra se propone el presupuesto del período siguiente.
        Outcome<Money> resultado = HistoricalBaseline.Average([]);

        Assert.Equal(OutcomeKind.Insufficient, resultado.Kind);
        Assert.NotNull(resultado.Reason);
        Assert.Throws<InvalidOperationException>(() => resultado.Value);
    }

    [Fact]
    public void los_periodos_mas_alla_del_tercero_se_ignoran()
    {
        Money conTres = HistoricalBaseline.Average([
            new Money(3_000_000),
            new Money(2_000_000),
            new Money(1_000_000),
        ]).Value;

        Money conCinco = HistoricalBaseline.Average([
            new Money(3_000_000),
            new Money(2_000_000),
            new Money(1_000_000),
            new Money(9_999_999),
            new Money(8_888_888),
        ]).Value;

        Assert.Equal(conTres, conCinco);
    }

    [Fact]
    public void un_periodo_con_gasto_negativo_es_invalido()
    {
        // Un ciclo cuyo gasto neto salió negativo —más devoluciones que
        // compras— es un dato real, pero no describe ningún hábito y
        // arrastraría hacia abajo el presupuesto propuesto.
        Outcome<Money> resultado = HistoricalBaseline.Average([
            new Money(1_000_000),
            new Money(-500_000),
        ]);

        Assert.Equal(OutcomeKind.Invalid, resultado.Kind);
    }

    [Fact]
    public void el_promedio_redondea_una_vez_y_no_tres()
    {
        // Cada término por separado daría 3 + 2 + 2 = 7 centavos. La división
        // única da (5×50 + 5×30 + 5×20) / 100 = 5. Con el reparto por términos
        // el promedio de tres valores iguales no sería ese valor.
        Money promedio = HistoricalBaseline.Average([
            new Money(5),
            new Money(5),
            new Money(5),
        ]).Value;

        Assert.Equal(new Money(5), promedio);
    }

    [Fact]
    public void el_promedio_de_valores_iguales_es_ese_valor()
    {
        // Vale para cualquier cifra, no solo para las redondas. Es la
        // comprobación que rompería un redondeo por término.
        for (long centavos = 1; centavos < 500; centavos++)
        {
            Money promedio = HistoricalBaseline.Average([
                new Money(centavos),
                new Money(centavos),
                new Money(centavos),
            ]).Value;

            Assert.Equal(new Money(centavos), promedio);
        }
    }

    [Fact]
    public void el_promedio_queda_entre_el_menor_y_el_mayor()
    {
        Money promedio = HistoricalBaseline.Average([
            new Money(1_000_000),
            new Money(7_000_000),
            new Money(3_000_000),
        ]).Value;

        Assert.InRange(promedio.Cents, 1_000_000, 7_000_000);
    }

    [Fact]
    public void los_pesos_no_se_pueden_modificar_desde_fuera()
    {
        // Con un `long[]`, el `readonly` protege la referencia y no el
        // contenido: `Weights[0] = 999` habría cambiado en silencio todas las
        // recomendaciones de presupuesto del sistema. Con ImmutableArray el
        // compilador no deja ni escribirlo.
        Assert.Equal([50L, 30L, 20L], HistoricalBaseline.Weights.ToArray());

        // El tipo es parte de la garantía: con `long[]` esta prueba pasaría
        // igual y la protección no existiría.
        Assert.IsType<ImmutableArray<long>>(HistoricalBaseline.Weights);
    }

    [Fact]
    public void los_periodos_considerados_se_pueden_consultar()
    {
        // La pantalla lo necesita para decir «según tus últimos dos meses» y no
        // fingir tres.
        Assert.Equal(0, HistoricalBaseline.PeriodsConsidered(0));
        Assert.Equal(1, HistoricalBaseline.PeriodsConsidered(1));
        Assert.Equal(3, HistoricalBaseline.PeriodsConsidered(3));
        Assert.Equal(3, HistoricalBaseline.PeriodsConsidered(12));
    }
}
