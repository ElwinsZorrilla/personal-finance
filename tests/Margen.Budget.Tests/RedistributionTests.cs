using Margen.Budget;
using Margen.Domain;

namespace Margen.Budget.Tests;

public sealed class RedistributionTests
{
    private static CategoryAllocation Alquiler(long asignado = 2_500_000, long gastado = 0)
        => new(Guid.NewGuid(), Priority.Essential, new Money(asignado), new Money(gastado));

    private static CategoryAllocation Transporte(long asignado = 800_000, long gastado = 0)
        => new(Guid.NewGuid(), Priority.Important, new Money(asignado), new Money(gastado));

    private static CategoryAllocation Restaurantes(long asignado = 1_000_000, long gastado = 0)
        => new(Guid.NewGuid(), Priority.Flexible, new Money(asignado), new Money(gastado));

    private static CategoryAllocation Suscripciones(long asignado = 500_000, long gastado = 0)
        => new(Guid.NewGuid(), Priority.Optional, new Money(asignado), new Money(gastado));

    [Fact]
    public void recortar_el_alquiler_falla()
    {
        // El criterio de la fase. No es una advertencia que se pueda ignorar:
        // la función devuelve un rechazo con motivo.
        RedistributionResult resultado = Redistribution.Trim(Alquiler(), new Money(100_000));

        Assert.False(resultado.Succeeded);
        Assert.Equal(RedistributionRefusal.ProtectedPriority, resultado.Refusal);
        Assert.Null(resultado.Source);
    }

    [Fact]
    public void recortar_una_categoria_importante_tambien_falla()
    {
        // Prioridad 2 es igual de intocable que la 1.
        RedistributionResult resultado = Redistribution.Trim(Transporte(), new Money(100_000));

        Assert.False(resultado.Succeeded);
        Assert.Equal(RedistributionRefusal.ProtectedPriority, resultado.Refusal);
    }

    [Fact]
    public void ninguna_cantidad_sortea_la_prioridad_protegida()
    {
        // Ni un centavo, ni cero, ni una cantidad imposible: la prioridad se
        // comprueba antes que nada.
        foreach (long centavos in new long[] { -1, 0, 1, 100_000, long.MaxValue / 2 })
        {
            RedistributionResult resultado = Redistribution.Trim(Alquiler(), new Money(centavos));

            Assert.Equal(RedistributionRefusal.ProtectedPriority, resultado.Refusal);
        }
    }

    [Fact]
    public void una_prioridad_fuera_del_enum_queda_del_lado_protegido()
    {
        // `(Priority)99` es un valor perfectamente construible en C#. Con un
        // umbral `>= 3` pasaría por recortable; con la lista blanca de las dos
        // prioridades que sí se pueden tocar, cae del lado seguro.
        var rara = new CategoryAllocation(
            Guid.NewGuid(), (Priority)99, new Money(1_000_000), Money.Zero);

        Assert.False(rara.CanBeTrimmed);
        Assert.Equal(
            RedistributionRefusal.ProtectedPriority,
            Redistribution.Trim(rara, new Money(1000)).Refusal);
    }

    [Fact]
    public void una_prioridad_cero_tambien_queda_protegida()
    {
        var sinPrioridad = new CategoryAllocation(
            Guid.NewGuid(), default, new Money(1_000_000), Money.Zero);

        Assert.False(sinPrioridad.CanBeTrimmed);
    }

    [Fact]
    public void el_recortable_total_ignora_las_prioridades_desconocidas()
    {
        var categorias = new[]
        {
            new CategoryAllocation(
                Guid.NewGuid(), (Priority)99, new Money(9_000_000), Money.Zero),
            Restaurantes(asignado: 1_000_000, gastado: 400_000),
        };

        Assert.Equal(new Money(600_000), Redistribution.TrimmableTotal(categorias));
    }

    [Theory]
    [InlineData(Priority.Flexible)]
    [InlineData(Priority.Optional)]
    public void una_categoria_flexible_o_prescindible_si_se_recorta(Priority prioridad)
    {
        var categoria = new CategoryAllocation(
            Guid.NewGuid(), prioridad, new Money(1_000_000), Money.Zero);

        RedistributionResult resultado = Redistribution.Trim(categoria, new Money(300_000));

        Assert.True(resultado.Succeeded);
        Assert.Equal(new Money(700_000), resultado.Source!.Allocated);
    }

    [Fact]
    public void no_se_puede_recortar_mas_de_lo_disponible()
    {
        // El límite es lo disponible, no lo asignado: recortar por debajo de lo
        // ya gastado dejaría la categoría en negativo el día del recorte, y ese
        // gasto ya ocurrió.
        CategoryAllocation categoria = Restaurantes(asignado: 1_000_000, gastado: 800_000);

        RedistributionResult resultado = Redistribution.Trim(categoria, new Money(300_000));

        Assert.False(resultado.Succeeded);
        Assert.Equal(RedistributionRefusal.NotEnoughAvailable, resultado.Refusal);
    }

    [Fact]
    public void se_puede_recortar_exactamente_lo_disponible()
    {
        CategoryAllocation categoria = Restaurantes(asignado: 1_000_000, gastado: 800_000);

        RedistributionResult resultado = Redistribution.Trim(categoria, new Money(200_000));

        Assert.True(resultado.Succeeded);
        Assert.Equal(new Money(800_000), resultado.Source!.Allocated);
        Assert.Equal(Money.Zero, resultado.Source.Available);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void recortar_cero_o_menos_no_es_recortar(long centavos)
    {
        RedistributionResult resultado = Redistribution.Trim(Restaurantes(), new Money(centavos));

        Assert.False(resultado.Succeeded);
        Assert.Equal(RedistributionRefusal.NonPositiveAmount, resultado.Refusal);
    }

    [Fact]
    public void mover_dinero_entre_dos_flexibles_cuadra_por_los_dos_lados()
    {
        CategoryAllocation origen = Restaurantes(asignado: 1_000_000);
        CategoryAllocation destino = Suscripciones(asignado: 500_000);

        RedistributionResult resultado = Redistribution.Move(origen, destino, new Money(200_000));

        Assert.True(resultado.Succeeded);
        Assert.Equal(new Money(800_000), resultado.Source!.Allocated);
        Assert.Equal(new Money(700_000), resultado.Target!.Allocated);

        // Lo que sale de uno entra en el otro: el total no cambia.
        Assert.Equal(
            origen.Allocated + destino.Allocated,
            resultado.Source.Allocated + resultado.Target.Allocated);
    }

    [Fact]
    public void al_alquiler_si_se_le_puede_dar_mas()
    {
        // Que no se pueda recortar no significa que no se le pueda dar. El
        // destino no tiene restricción de prioridad.
        CategoryAllocation origen = Restaurantes(asignado: 1_000_000);
        CategoryAllocation destino = Alquiler(asignado: 2_500_000);

        RedistributionResult resultado = Redistribution.Move(origen, destino, new Money(300_000));

        Assert.True(resultado.Succeeded);
        Assert.Equal(new Money(2_800_000), resultado.Target!.Allocated);
    }

    [Fact]
    public void un_movimiento_desde_una_categoria_protegida_no_toca_el_destino()
    {
        // Si el recorte falla, el destino no se modifica: no hay estado
        // intermedio en el que el dinero exista dos veces.
        CategoryAllocation origen = Alquiler();
        CategoryAllocation destino = Restaurantes();

        RedistributionResult resultado = Redistribution.Move(origen, destino, new Money(100_000));

        Assert.False(resultado.Succeeded);
        Assert.Null(resultado.Source);
        Assert.Null(resultado.Target);
    }

    [Fact]
    public void un_movimiento_que_excede_lo_disponible_no_toca_el_destino()
    {
        CategoryAllocation origen = Restaurantes(asignado: 1_000_000, gastado: 900_000);
        CategoryAllocation destino = Suscripciones();

        RedistributionResult resultado = Redistribution.Move(origen, destino, new Money(500_000));

        Assert.False(resultado.Succeeded);
        Assert.Equal(RedistributionRefusal.NotEnoughAvailable, resultado.Refusal);
        Assert.Null(resultado.Target);
    }

    [Fact]
    public void mover_dinero_a_la_misma_categoria_se_rechaza()
    {
        CategoryAllocation categoria = Restaurantes();

        RedistributionResult resultado = Redistribution.Move(
            categoria, categoria, new Money(100_000));

        Assert.False(resultado.Succeeded);
        Assert.Equal(RedistributionRefusal.SameCategory, resultado.Refusal);
    }

    [Fact]
    public void el_recortable_total_ignora_lo_protegido()
    {
        // Es lo que la pantalla necesita para decir «puedes reasignar hasta X»
        // antes de que el usuario elija de dónde.
        var categorias = new[]
        {
            Alquiler(asignado: 2_500_000),
            Transporte(asignado: 800_000),
            Restaurantes(asignado: 1_000_000, gastado: 400_000),
            Suscripciones(asignado: 500_000, gastado: 100_000),
        };

        // 600,000 de Restaurantes + 400,000 de Suscripciones. Ni un centavo de
        // Alquiler ni de Transporte, aunque estén enteros sin gastar.
        Assert.Equal(new Money(1_000_000), Redistribution.TrimmableTotal(categorias));
    }

    [Fact]
    public void una_categoria_flexible_ya_sobregirada_no_aporta_al_recortable()
    {
        var categorias = new[]
        {
            Restaurantes(asignado: 1_000_000, gastado: 1_500_000),
            Suscripciones(asignado: 500_000, gastado: 100_000),
        };

        Assert.Equal(new Money(400_000), Redistribution.TrimmableTotal(categorias));
    }

    [Fact]
    public void sin_categorias_el_recortable_es_cero()
    {
        Assert.Equal(Money.Zero, Redistribution.TrimmableTotal([]));
    }

    [Fact]
    public void el_recortable_total_coincide_con_lo_que_de_verdad_se_puede_recortar()
    {
        // La invariante: si TrimmableTotal dice X, recortar X repartido entre
        // las categorías no protegidas tiene que salir bien en todas.
        var categorias = new[]
        {
            Alquiler(asignado: 2_500_000),
            Restaurantes(asignado: 1_000_000, gastado: 400_000),
            Suscripciones(asignado: 500_000, gastado: 100_000),
        };

        Money total = Redistribution.TrimmableTotal(categorias);
        Money recortado = Money.Zero;

        foreach (CategoryAllocation categoria in categorias.Where(c => c.CanBeTrimmed))
        {
            RedistributionResult resultado = Redistribution.Trim(categoria, categoria.Available);

            Assert.True(resultado.Succeeded);
            recortado += categoria.Available;
        }

        Assert.Equal(total, recortado);
    }
}
