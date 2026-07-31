using Margen.Budget;
using Margen.Domain;

namespace Margen.Budget.Tests;

public sealed class PaceTests
{
    /// <summary>Treinta días, del 25 de junio al 24 de julio.</summary>
    private static BudgetCycle Ciclo()
        => BudgetCycle.Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 25)).Value;

    private static DateOnly Dia(int n) => new DateOnly(2026, 6, 25).AddDays(n - 1);

    [Fact]
    public void a_mitad_de_ciclo_lo_esperado_es_la_mitad_del_presupuesto()
    {
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(1_500_000),
            Ciclo(),
            Dia(15)).Value;

        Assert.Equal(new Money(1_500_000), ritmo.Expected);
        Assert.Equal(Money.Zero, ritmo.Deviation);

        // Lo gastado viaja en la lectura tal cual entró: la pantalla enseña las
        // dos cifras juntas y no tiene que volver a pedirla.
        Assert.Equal(new Money(1_500_000), ritmo.Actual);
    }

    [Fact]
    public void gastar_por_encima_del_ritmo_da_desviacion_positiva()
    {
        // 60 % del presupuesto con el 50 % del ciclo consumido. Es la señal
        // que la app vigila.
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(1_800_000),
            Ciclo(),
            Dia(15)).Value;

        Assert.Equal(new Money(300_000), ritmo.Deviation);
        Assert.False(ritmo.IsAheadOfPace);
    }

    [Fact]
    public void gastar_por_debajo_del_ritmo_da_desviacion_negativa()
    {
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(1_000_000),
            Ciclo(),
            Dia(15)).Value;

        Assert.Equal(new Money(-500_000), ritmo.Deviation);
        Assert.True(ritmo.IsAheadOfPace);
    }

    [Fact]
    public void la_proyeccion_de_cierre_extrapola_el_ritmo_actual()
    {
        // 18,000 en 15 días son 36,000 en 30.
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(1_800_000),
            Ciclo(),
            Dia(15)).Value;

        Assert.Equal(new Money(3_600_000), ritmo.ProjectedClose);
    }

    [Fact]
    public void un_ciclo_sano_no_reporta_fecha_de_agotamiento()
    {
        // Al ritmo actual el presupuesto aguanta hasta el cierre. El nulo es la
        // respuesta, no la fecha de cierre: son cosas distintas.
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(1_400_000),
            Ciclo(),
            Dia(15)).Value;

        Assert.Null(ritmo.ProjectedDepletion);
        Assert.False(ritmo.RunsOutEarly);
    }

    [Fact]
    public void al_doble_del_ritmo_el_dinero_se_acaba_a_mitad_de_lo_que_queda()
    {
        // 20,000 gastados en 15 días de un presupuesto de 30,000. Quedan
        // 10,000 y el ritmo es 1,333.33 al día: aguanta 7 días más.
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(2_000_000),
            Ciclo(),
            Dia(15)).Value;

        Assert.True(ritmo.RunsOutEarly);
        Assert.Equal(Dia(15).AddDays(7), ritmo.ProjectedDepletion);
    }

    [Fact]
    public void con_el_presupuesto_ya_agotado_la_fecha_es_hoy()
    {
        // No una fecha pasada inventada a partir de un ritmo que ya no
        // describe nada.
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(3_500_000),
            Ciclo(),
            Dia(20)).Value;

        Assert.Equal(Dia(20), ritmo.ProjectedDepletion);
    }

    [Fact]
    public void con_el_presupuesto_exactamente_agotado_la_fecha_es_hoy()
    {
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(3_000_000),
            Ciclo(),
            Dia(20)).Value;

        Assert.Equal(Dia(20), ritmo.ProjectedDepletion);
    }

    [Fact]
    public void sin_gasto_no_hay_ritmo_que_proyectar_ni_division_por_cero()
    {
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            Money.Zero,
            Ciclo(),
            Dia(10)).Value;

        Assert.Null(ritmo.ProjectedDepletion);
        Assert.Equal(Money.Zero, ritmo.ProjectedClose);
    }

    [Fact]
    public void el_primer_dia_no_divide_por_cero_al_extrapolar()
    {
        // Transcurrido 1 día de 30. La extrapolación multiplica por 30 y no
        // divide entre cero.
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(100_000),
            Ciclo(),
            Dia(1)).Value;

        Assert.Equal(new Money(3_000_000), ritmo.ProjectedClose);
        Assert.Equal(new Money(100_000), ritmo.Expected);
    }

    [Fact]
    public void el_ultimo_dia_lo_esperado_es_el_presupuesto_entero()
    {
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(2_900_000),
            Ciclo(),
            Dia(30)).Value;

        Assert.Equal(new Money(3_000_000), ritmo.Expected);
        Assert.Equal(new Money(-100_000), ritmo.Deviation);
    }

    [Fact]
    public void un_presupuesto_negativo_no_tiene_ritmo_que_medir()
    {
        Outcome<PaceReading> resultado = Pace.Read(
            new Money(-1),
            new Money(100_000),
            Ciclo(),
            Dia(5));

        Assert.Equal(OutcomeKind.Invalid, resultado.Kind);
    }

    [Fact]
    public void un_gasto_neto_negativo_por_devoluciones_no_proyecta_agotamiento()
    {
        // Más devoluciones que compras. No hay ritmo de agotamiento que
        // proyectar y dividir por un gasto negativo daría una fecha absurda.
        PaceReading ritmo = Pace.Read(
            new Money(3_000_000),
            new Money(-200_000),
            Ciclo(),
            Dia(10)).Value;

        Assert.Null(ritmo.ProjectedDepletion);
    }

    [Fact]
    public void lo_esperado_nunca_supera_el_presupuesto()
    {
        // Invariante a lo largo de todo el ciclo, incluidos los extremos.
        BudgetCycle ciclo = Ciclo();
        var presupuesto = new Money(1_234_567);

        for (int dia = 1; dia <= ciclo.TotalDays; dia++)
        {
            PaceReading ritmo = Pace.Read(presupuesto, Money.Zero, ciclo, Dia(dia)).Value;

            Assert.InRange(ritmo.Expected.Cents, 0, presupuesto.Cents);
        }
    }

    [Fact]
    public void la_fecha_de_agotamiento_nunca_cae_despues_del_cierre()
    {
        // Si cayera después, el ciclo no se agota antes y la respuesta correcta
        // es el nulo. Esta prueba impide que se cuele una fecha fuera de rango.
        BudgetCycle ciclo = Ciclo();

        for (int gastado = 100_000; gastado <= 5_000_000; gastado += 100_000)
        {
            PaceReading ritmo = Pace.Read(
                new Money(3_000_000),
                new Money(gastado),
                ciclo,
                Dia(15)).Value;

            if (ritmo.ProjectedDepletion is DateOnly fecha)
            {
                Assert.InRange(fecha, ciclo.Start, ciclo.End);
            }
        }
    }
}
