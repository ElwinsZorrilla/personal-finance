using Margen.Budget;
using Margen.Domain;

namespace Margen.Budget.Tests;

public sealed class SafeToSpendTests
{
    private static SafeToSpendInputs Entradas(
        long liquido = 5_000_000,
        long obligaciones = 0,
        long tarjetas = 0,
        long ahorro = 0,
        long seguridad = 0,
        long retenciones = 0)
        => new(
            new Money(liquido),
            new Money(obligaciones),
            new Money(tarjetas),
            new Money(ahorro),
            new Money(seguridad),
            new Money(retenciones));

    private static BudgetCycle Ciclo()
        => BudgetCycle.Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 25)).Value;

    [Fact]
    public void sin_compromisos_el_dinero_seguro_es_todo_el_liquido()
    {
        SafeToSpendBreakdown desglose = SafeToSpend.Compute(Entradas()).Value;

        Assert.Equal(new Money(5_000_000), desglose.Total);
        Assert.False(desglose.IsOverdrawn);
    }

    [Fact]
    public void cada_resta_baja_el_total_en_su_cuantia()
    {
        SafeToSpendBreakdown desglose = SafeToSpend.Compute(Entradas(
            liquido: 10_000_000,
            obligaciones: 1_000_000,
            tarjetas: 2_000_000,
            ahorro: 500_000,
            seguridad: 1_500_000,
            retenciones: 250_000)).Value;

        // 100,000 − 10,000 − 20,000 − 5,000 − 15,000 − 2,500 = 47,500
        Assert.Equal(new Money(4_750_000), desglose.Total);
        Assert.Equal(new Money(5_250_000), desglose.TotalDeducted);
    }

    [Fact]
    public void el_desglose_lleva_las_cinco_restas_por_separado()
    {
        // Un número sin desglose es un número que el usuario no puede discutir,
        // y cuando no lo puede discutir deja de creerlo.
        SafeToSpendBreakdown desglose = SafeToSpend.Compute(Entradas(
            obligaciones: 100_000,
            tarjetas: 200_000,
            ahorro: 300_000,
            seguridad: 400_000,
            retenciones: 500_000)).Value;

        Assert.Equal(5, desglose.Deductions.Count);

        Assert.Equal(
            new Money(100_000),
            desglose.Deductions.Single(d => d.Kind == DeductionKind.PendingObligations).Amount);
        Assert.Equal(
            new Money(200_000),
            desglose.Deductions.Single(d => d.Kind == DeductionKind.CardReserve).Amount);
        Assert.Equal(
            new Money(300_000),
            desglose.Deductions.Single(d => d.Kind == DeductionKind.CommittedSavings).Amount);
        Assert.Equal(
            new Money(400_000),
            desglose.Deductions.Single(d => d.Kind == DeductionKind.SafetyFund).Amount);
        Assert.Equal(
            new Money(500_000),
            desglose.Deductions.Single(d => d.Kind == DeductionKind.Withholdings).Amount);
    }

    [Fact]
    public void el_liquido_menos_el_total_deducido_es_exactamente_el_total()
    {
        // Invariante: ninguna resta se pierde ni se cuenta dos veces.
        SafeToSpendBreakdown desglose = SafeToSpend.Compute(Entradas(
            liquido: 3_333_333,
            obligaciones: 111_111,
            tarjetas: 222_222,
            ahorro: 333_333,
            seguridad: 444_444,
            retenciones: 555_555)).Value;

        Assert.Equal(desglose.Liquid - desglose.TotalDeducted, desglose.Total);
    }

    [Fact]
    public void el_total_puede_ser_negativo_y_se_dice()
    {
        // Redondearlo a cero cambiaría «ya te pasaste por 30,000» por «no te
        // queda nada», que son dos situaciones distintas.
        SafeToSpendBreakdown desglose = SafeToSpend.Compute(Entradas(
            liquido: 1_000_000,
            obligaciones: 4_000_000)).Value;

        Assert.True(desglose.IsOverdrawn);
        Assert.Equal(new Money(-3_000_000), desglose.Total);
        Assert.Equal(new Money(3_000_000), desglose.Shortfall);
    }

    [Fact]
    public void sin_sobregiro_el_faltante_es_cero()
    {
        SafeToSpendBreakdown desglose = SafeToSpend.Compute(Entradas()).Value;

        Assert.Equal(Money.Zero, desglose.Shortfall);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, -1, 0)]
    [InlineData(0, 0, 0, 0, -1)]
    public void una_resta_negativa_se_rechaza_en_vez_de_sumar(
        long obligaciones,
        long tarjetas,
        long ahorro,
        long seguridad,
        long retenciones)
    {
        // «Obligaciones pendientes de −5,000» no significa nada, y si se dejara
        // pasar sumaría al dinero seguro. El error más caro posible en esta
        // pantalla es uno que hace la cifra mayor.
        Outcome<SafeToSpendBreakdown> resultado = SafeToSpend.Compute(
            Entradas(
                obligaciones: obligaciones,
                tarjetas: tarjetas,
                ahorro: ahorro,
                seguridad: seguridad,
                retenciones: retenciones));

        Assert.Equal(OutcomeKind.Invalid, resultado.Kind);
    }

    [Fact]
    public void un_liquido_negativo_si_se_admite()
    {
        // La cuenta en descubierto es un hecho, no un dato inválido. Lo que no
        // puede ser negativo es una resta.
        SafeToSpendBreakdown desglose = SafeToSpend.Compute(
            Entradas(liquido: -500_000)).Value;

        Assert.True(desglose.IsOverdrawn);
        Assert.Equal(new Money(-500_000), desglose.Total);
    }

    // ---------- Disponible diario ----------

    [Fact]
    public void el_disponible_diario_reparte_entre_los_dias_que_quedan()
    {
        BudgetCycle ciclo = Ciclo();

        DailyAllowance diario = DailyAllowanceCalculator
            .For(new Money(3_000_000), ciclo, new DateOnly(2026, 6, 25)).Value;

        Assert.Equal(30, diario.DaysRemaining);
        Assert.Equal(new Money(100_000), diario.PerDay);
    }

    [Fact]
    public void el_ultimo_dia_no_divide_por_cero()
    {
        BudgetCycle ciclo = Ciclo();

        DailyAllowance diario = DailyAllowanceCalculator
            .For(new Money(150_000), ciclo, new DateOnly(2026, 7, 24)).Value;

        Assert.Equal(1, diario.DaysRemaining);
        Assert.Equal(new Money(150_000), diario.PerDay);
    }

    [Fact]
    public void despues_del_cierre_tampoco_divide_por_cero()
    {
        BudgetCycle ciclo = Ciclo();

        DailyAllowance diario = DailyAllowanceCalculator
            .For(new Money(150_000), ciclo, new DateOnly(2026, 12, 31)).Value;

        Assert.Equal(1, diario.DaysRemaining);
    }

    [Fact]
    public void el_diario_se_redondea_en_contra_de_quien_pregunta()
    {
        // 100 centavos entre 3 días son 33.33. Gastar 34 los tres días son 102
        // y el ciclo se pasa. La respuesta es 33.
        BudgetCycle ciclo = BudgetCycle
            .Inclusive(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)).Value;

        DailyAllowance diario = DailyAllowanceCalculator
            .For(new Money(100), ciclo, new DateOnly(2026, 6, 1)).Value;

        Assert.Equal(new Money(33), diario.PerDay);
    }

    [Fact]
    public void gastar_el_diario_todos_los_dias_nunca_supera_el_total()
    {
        // La invariante que justifica el redondeo hacia abajo, comprobada sobre
        // un rango de cifras en vez de sobre un caso escogido.
        BudgetCycle ciclo = Ciclo();

        for (long centavos = 0; centavos < 3000; centavos += 7)
        {
            DailyAllowance diario = DailyAllowanceCalculator
                .For(new Money(centavos), ciclo, ciclo.Start).Value;

            Assert.True(
                diario.PerDay.Cents * diario.DaysRemaining <= centavos,
                $"Con {centavos} centavos, {diario.PerDay.Cents} al día se pasa.");
        }
    }

    [Fact]
    public void con_el_dinero_seguro_en_negativo_el_diario_es_cero_y_lo_dice()
    {
        BudgetCycle ciclo = Ciclo();

        DailyAllowance diario = DailyAllowanceCalculator
            .For(new Money(-450_000), ciclo, new DateOnly(2026, 7, 1)).Value;

        Assert.Equal(Money.Zero, diario.PerDay);
        Assert.True(diario.IsOverdrawn);

        // El cero no es una mentira porque el faltante viaja al lado: la
        // pantalla puede decir «ya te pasaste por 4,500».
        Assert.Equal(new Money(450_000), diario.Shortfall);
    }

    [Fact]
    public void con_cero_el_diario_es_cero_sin_marcarlo_como_sobregiro()
    {
        BudgetCycle ciclo = Ciclo();

        DailyAllowance diario = DailyAllowanceCalculator
            .For(Money.Zero, ciclo, ciclo.Start).Value;

        Assert.Equal(Money.Zero, diario.PerDay);
        Assert.False(diario.IsOverdrawn);
        Assert.Equal(Money.Zero, diario.Shortfall);
    }
}
