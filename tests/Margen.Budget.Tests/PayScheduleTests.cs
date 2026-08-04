using System.Collections.Immutable;
using Margen.Budget;
using Margen.Domain;

namespace Margen.Budget.Tests;

/// <summary>
/// El calendario de cobros: uno, dos o más al mes.
/// </summary>
/// <remarks>
/// Es la clase de código que se equivoca sin avisar. Un ciclo mal calculado no
/// lanza nada: enseña un «hoy puedes gastar» repartido entre los días
/// equivocados, y esa cifra parece tan correcta como la buena.
/// </remarks>
public class PayScheduleTests
{
    private static PaySchedule Quincenal() =>
        PaySchedule.Of([15, 31]).Value;

    private static DateOnly D(int año, int mes, int dia) => new(año, mes, dia);

    [Fact]
    public void un_solo_cobro_da_el_ciclo_de_siempre()
    {
        // La Fase 3 solo contemplaba esto, y tiene que seguir dando lo mismo:
        // cambiar el comportamiento de quien cobra una vez al mes no era parte
        // del encargo.
        PaySchedule calendario = PaySchedule.Of([25]).Value;

        BudgetCycle ciclo = calendario.CycleAround(D(2026, 8, 3));

        Assert.Equal(D(2026, 7, 25), ciclo.Start);
        Assert.Equal(D(2026, 8, 24), ciclo.End);
    }

    [Fact]
    public void dos_cobros_parten_el_mes_en_dos_ciclos()
    {
        PaySchedule calendario = Quincenal();

        BudgetCycle primero = calendario.CycleAround(D(2026, 8, 20));
        BudgetCycle segundo = calendario.CycleAround(D(2026, 9, 2));

        Assert.Equal(D(2026, 8, 15), primero.Start);
        Assert.Equal(D(2026, 8, 30), primero.End);

        Assert.Equal(D(2026, 8, 31), segundo.Start);
        Assert.Equal(D(2026, 9, 14), segundo.End);
    }

    [Fact]
    public void el_dia_del_cobro_abre_ciclo_y_no_cierra_el_anterior()
    {
        // El día que entra el dinero pertenece al ciclo que empieza. Si
        // perteneciera al que acaba, el ingreso se sumaría al presupuesto del
        // período que ya se gastó.
        PaySchedule calendario = Quincenal();

        BudgetCycle ciclo = calendario.CycleAround(D(2026, 8, 15));

        Assert.Equal(D(2026, 8, 15), ciclo.Start);
    }

    [Fact]
    public void antes_del_primer_cobro_del_mes_sigue_abierto_el_del_mes_pasado()
    {
        // El 3 de agosto, con cobros el 15 y el 31, el ciclo abierto empezó el
        // 31 de julio. Sin esta rama, buscar «el último cobro de agosto que no
        // sea posterior a hoy» no encuentra ninguno.
        PaySchedule calendario = Quincenal();

        BudgetCycle ciclo = calendario.CycleAround(D(2026, 8, 3));

        Assert.Equal(D(2026, 7, 31), ciclo.Start);
        Assert.Equal(D(2026, 8, 14), ciclo.End);
    }

    [Fact]
    public void fin_de_mes_en_febrero_es_el_ultimo_dia_que_existe()
    {
        // «Fin de mes» se escribe 31. En febrero no hay 31, y sin correrlo al
        // último día ese cobro no existiría: febrero entero quedaría dentro del
        // ciclo que abrió el 15, con el doble de días y la mitad de gasto
        // diario.
        PaySchedule calendario = Quincenal();

        // El ciclo que abrió el 15 **termina el 27**: el 28 ya es el cobro de
        // fin de mes y abre el suyo. Escribir aquí 28 fue el primer intento, y
        // es el error de un día que en pantalla se ve como un peso de más en el
        // gasto diario.
        BudgetCycle quincena = calendario.CycleAround(D(2026, 2, 26));

        Assert.Equal(D(2026, 2, 15), quincena.Start);
        Assert.Equal(D(2026, 2, 27), quincena.End);

        BudgetCycle finDeMes = calendario.CycleAround(D(2026, 3, 1));

        Assert.Equal(D(2026, 2, 28), finDeMes.Start);
        Assert.Equal(D(2026, 3, 14), finDeMes.End);
    }

    [Fact]
    public void en_un_año_bisiesto_febrero_llega_al_29()
    {
        PaySchedule calendario = Quincenal();

        DateOnly[] fechas = [.. calendario.PayDatesIn(2028, 2)];

        Assert.Equal([D(2028, 2, 15), D(2028, 2, 29)], fechas);
    }

    [Fact]
    public void dos_cobros_que_caen_el_mismo_dia_no_producen_un_ciclo_vacio()
    {
        // Con cobros el 30 y el 31, en febrero los dos caen en el 28. Si no se
        // quitaran los repetidos **después** de ajustar, saldría un ciclo de
        // cero días, y todo lo que divide entre días —el disponible diario, el
        // ritmo, la proyección— reventaría o daría infinito.
        PaySchedule calendario = PaySchedule.Of([30, 31]).Value;

        ImmutableArray<DateOnly> fechas = calendario.PayDatesIn(2026, 2);

        Assert.Single(fechas);
        Assert.Equal(D(2026, 2, 28), fechas[0]);

        BudgetCycle ciclo = calendario.CycleAround(D(2026, 2, 28));

        Assert.True(ciclo.TotalDays > 0);
    }

    [Fact]
    public void los_ciclos_de_un_año_cubren_todos_los_dias_sin_hueco_ni_solape()
    {
        // La propiedad que importa de verdad: **todo día pertenece a exactamente
        // un ciclo**. Un hueco deja un día sin presupuesto; un solape lo cuenta
        // dos veces. Ninguna de las dos cosas se ve mirando una fecha suelta.
        PaySchedule calendario = Quincenal();

        var dia = D(2026, 1, 1);
        BudgetCycle ciclo = calendario.CycleAround(dia);

        while (dia < D(2027, 1, 1))
        {
            if (!ciclo.Contains(dia))
            {
                BudgetCycle siguiente = calendario.CycleAround(dia);

                Assert.Equal(ciclo.End.AddDays(1), siguiente.Start);
                ciclo = siguiente;
            }

            Assert.True(ciclo.Contains(dia), $"{dia} no cae en ningún ciclo");
            dia = dia.AddDays(1);
        }
    }

    [Fact]
    public void el_ciclo_siguiente_empieza_donde_termina_el_actual()
    {
        PaySchedule calendario = Quincenal();

        BudgetCycle actual = calendario.CycleAround(D(2026, 8, 20));
        BudgetCycle siguiente = calendario.NextCycleAfter(D(2026, 8, 20));

        Assert.Equal(actual.End.AddDays(1), siguiente.Start);
    }

    [Fact]
    public void el_orden_en_que_se_escriben_los_dias_no_cambia_nada()
    {
        BudgetCycle a = PaySchedule.Of([31, 15]).Value.CycleAround(D(2026, 8, 20));
        BudgetCycle b = PaySchedule.Of([15, 31]).Value.CycleAround(D(2026, 8, 20));

        Assert.Equal(a, b);
    }

    [Fact]
    public void un_dia_repetido_no_duplica_el_cobro()
    {
        PaySchedule calendario = PaySchedule.Of([15, 15, 31]).Value;

        Assert.Equal<int[]>([15, 31], [.. calendario.Days]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(-1)]
    public void un_dia_que_no_existe_se_rechaza(int dia)
    {
        // Se rechaza en vez de acotarlo callando: un 0 o un 45 casi siempre es
        // un error de quien lo escribió, y «corregirlo» produce un ciclo que
        // nadie pidió y que además parece correcto.
        Outcome<PaySchedule> resultado = PaySchedule.Of([dia]);

        Assert.False(resultado.IsComputed);
    }

    [Fact]
    public void sin_ningun_dia_no_hay_calendario()
    {
        Outcome<PaySchedule> resultado = PaySchedule.Of([]);

        Assert.False(resultado.IsComputed);
    }

    [Fact]
    public void con_dos_cobros_el_reparto_diario_es_sobre_media_quincena()
    {
        // Es la consecuencia que se ve en pantalla y el motivo de toda esta
        // clase: con un ciclo mensual, «hoy puedes gastar» repartía el dinero
        // de una quincena entre treinta días.
        PaySchedule quincenal = Quincenal();
        PaySchedule mensual = PaySchedule.Of([15]).Value;

        int dias = quincenal.CycleAround(D(2026, 8, 20)).TotalDays;

        Assert.InRange(dias, 14, 17);
        Assert.True(dias < mensual.CycleAround(D(2026, 8, 20)).TotalDays);
    }
}
