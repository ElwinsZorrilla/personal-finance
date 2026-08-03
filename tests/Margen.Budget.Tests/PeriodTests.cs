using Margen.Budget;
using Margen.Domain;

namespace Margen.Budget.Tests;

public sealed class PeriodTests
{
    /// <summary>Cobra el 25. Su ciclo es del 25 al 24, no del 1 al 31.</summary>
    private static BudgetCycle Quincenal()
        => BudgetCycle.Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 25)).Value;

    [Fact]
    public void el_ciclo_va_de_un_ingreso_al_dia_anterior_del_siguiente()
    {
        BudgetCycle ciclo = Quincenal();

        Assert.Equal(new DateOnly(2026, 6, 25), ciclo.Start);
        Assert.Equal(new DateOnly(2026, 7, 24), ciclo.End);
        Assert.Equal(30, ciclo.TotalDays);
    }

    [Fact]
    public void el_dia_del_ingreso_siguiente_no_pertenece_a_este_ciclo()
    {
        // Si perteneciera a los dos, el ingreso se contaría dos veces.
        BudgetCycle ciclo = Quincenal();

        Assert.True(ciclo.Contains(new DateOnly(2026, 7, 24)));
        Assert.False(ciclo.Contains(new DateOnly(2026, 7, 25)));
    }

    [Fact]
    public void un_ciclo_de_quincena_dura_quince_dias()
    {
        BudgetCycle ciclo = BudgetCycle
            .Between(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 25)).Value;

        Assert.Equal(15, ciclo.TotalDays);
    }

    [Fact]
    public void nunca_reporta_cero_dias_restantes()
    {
        // El último día, disponible / díasRestantes daría infinito con cero.
        BudgetCycle ciclo = Quincenal();

        Assert.Equal(1, ciclo.DaysRemainingFrom(new DateOnly(2026, 7, 24)));
        Assert.Equal(1, ciclo.DaysRemainingFrom(new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void el_primer_dia_quedan_todos_los_dias()
    {
        BudgetCycle ciclo = Quincenal();

        Assert.Equal(30, ciclo.DaysRemainingFrom(new DateOnly(2026, 6, 25)));
        Assert.Equal(1, ciclo.ElapsedDaysAt(new DateOnly(2026, 6, 25)));
    }

    [Fact]
    public void una_fecha_anterior_al_ciclo_se_acota_al_principio()
    {
        BudgetCycle ciclo = Quincenal();

        Assert.Equal(30, ciclo.DaysRemainingFrom(new DateOnly(2026, 1, 1)));
        Assert.Equal(1, ciclo.ElapsedDaysAt(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void transcurridos_mas_restantes_siempre_suman_los_totales_mas_uno()
    {
        BudgetCycle ciclo = Quincenal();

        for (int dia = 0; dia < ciclo.TotalDays; dia++)
        {
            DateOnly hoy = ciclo.Start.AddDays(dia);
            Assert.Equal(
                ciclo.TotalDays + 1,
                ciclo.ElapsedDaysAt(hoy) + ciclo.DaysRemainingFrom(hoy));
        }
    }

    [Fact]
    public void el_progreso_es_un_par_de_enteros_y_no_un_double()
    {
        // El cliente usa un double para dibujar una barra y allí está bien.
        // Aquí este número multiplica dinero.
        BudgetCycle ciclo = Quincenal();

        (int transcurridos, int totales) = ciclo.ProgressAt(new DateOnly(2026, 7, 9));

        Assert.Equal(15, transcurridos);
        Assert.Equal(30, totales);
    }

    [Fact]
    public void un_ingreso_siguiente_anterior_al_primero_es_invalido()
    {
        Outcome<BudgetCycle> resultado = BudgetCycle
            .Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 1));

        Assert.Equal(OutcomeKind.Invalid, resultado.Kind);
        Assert.NotNull(resultado.Reason);
    }

    [Fact]
    public void dos_ingresos_el_mismo_dia_son_invalidos()
    {
        // Daría un ciclo de cero días y todo lo que divide entre días.
        Outcome<BudgetCycle> resultado = BudgetCycle
            .Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 25));

        Assert.Equal(OutcomeKind.Invalid, resultado.Kind);
    }

    [Fact]
    public void un_ciclo_de_un_solo_dia_es_valido()
    {
        BudgetCycle ciclo = BudgetCycle
            .Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 26)).Value;

        Assert.Equal(1, ciclo.TotalDays);
        Assert.Equal(1, ciclo.DaysRemainingFrom(new DateOnly(2026, 6, 25)));
    }

    [Fact]
    public void inclusive_rechaza_un_fin_anterior_al_inicio()
    {
        Outcome<BudgetCycle> resultado = BudgetCycle
            .Inclusive(new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 1));

        Assert.Equal(OutcomeKind.Invalid, resultado.Kind);
    }

    [Fact]
    public void inclusive_acepta_los_dos_extremos_iguales()
    {
        BudgetCycle ciclo = BudgetCycle
            .Inclusive(new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 25)).Value;

        Assert.Equal(1, ciclo.TotalDays);
    }

    [Fact]
    public void leer_la_cifra_de_un_resultado_invalido_lanza()
    {
        // Es el punto entero de Outcome: no hay valor por defecto silencioso.
        Outcome<BudgetCycle> resultado = BudgetCycle
            .Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 1));

        Assert.Throws<InvalidOperationException>(() => resultado.Value);
    }

    [Fact]
    public void value_or_fallback_devuelve_la_alternativa_cuando_no_hay_cifra()
    {
        Outcome<BudgetCycle> resultado = BudgetCycle
            .Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 1));

        BudgetCycle alternativa = Quincenal();

        Assert.Same(alternativa, resultado.ValueOrFallback(alternativa));

        // Y cuando sí hay cifra, la alternativa se ignora.
        BudgetCycle calculado = Quincenal();
        Assert.Same(calculado, Outcome.Computed(calculado).ValueOrFallback(alternativa));
    }
}
