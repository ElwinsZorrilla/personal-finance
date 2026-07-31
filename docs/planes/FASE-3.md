# Plan — Fase 3 · Motor de presupuesto

## Árbol

```text
src/Margen.Budget/
  BudgetCycle.cs         el período de ingreso a ingreso
  SafeToSpend.cs         la fórmula y su desglose
  SafeToSpendInputs.cs   todo lo que entra, sin buscar nada
  DailyAllowance.cs      el disponible diario
  SpendingLedger.cs      suma de movimientos por categoría, con signo
  HistoricalBaseline.cs  50 / 30 / 20 con menos de tres períodos
  Pace.cs                proyección de cierre y desviación de ritmo
  Redistribution.cs      recortes con prioridades intocables
  Outcome.cs             resultado que puede no existir, sin caer a cero

tests/Margen.Budget.Tests/
  PeriodTests.cs  SafeToSpendTests.cs  SpendingTests.cs
  HistoryTests.cs  PaceTests.cs  RedistributionTests.cs
```

`Margen.Budget` referencia solo a `Margen.Domain`. Nada de EF Core, nada de
ASP.NET, nada de `DateTime.Now`. Si mañana alguien añade una de esas
referencias, la compilación no falla pero la fase pierde su razón de ser; queda
dicho aquí y comprobado en la revisión.

## Decisiones de tipos

**`Outcome<T>`** en lugar de devolver `T` con un valor por defecto. Tres estados:
calculado, insuficiente —faltan datos— e inválido —los datos se contradicen—.
El llamador tiene que mirar cuál es antes de leer la cifra.

La alternativa habitual es devolver `Money.Zero` cuando no se puede calcular.
Eso convierte «no sé» en «cero pesos», que en esta pantalla significa «no gastes
nada» y es una respuesta concreta a una pregunta que no se pudo responder.

**`SafeToSpendInputs`** es un `record` con todos los sumandos por separado. No
recibe una lista de movimientos ni un `DbContext`: recibe cifras ya agregadas.
Quién las agrega es la Fase 4.

**Ningún porcentaje es `double`.** El ritmo esperado, la base histórica y los
recortes se expresan como fracciones de enteros y se aplican con
`Money.Scale(num, den)` o `Money.Prorate(pesos)`.

El progreso del período —qué fracción va consumida— sí se expresa como par de
enteros `(díasTranscurridos, díasTotales)`, no como `double`. Es el único lugar
donde la Fase 1 usó `double` en el cliente, y allí está bien porque dibuja una
barra. Aquí multiplica dinero.

## El período

```text
inicio = día del ingreso que abre
fin    = día anterior al ingreso siguiente
```

`DíasTotales = fin − inicio + 1`. `DíasRestantes(hoy)` nunca baja de 1: el
último día, `disponible / díasRestantes` daría infinito y la pantalla se rompe.
Es la misma regla que `BudgetPeriod.daysRemainingFrom` en el cliente, y aquí
lleva su propia prueba porque el cálculo vive en el servidor.

Hoy entra por parámetro como `DateOnly` ya en hora de Santo Domingo. El motor no
llama al reloj.

## La fórmula

```text
DineroSeguro = líquido
             − obligacionesPendientes
             − reservaDeTarjetas
             − ahorroComprometido
             − fondoDeSeguridad
             − retenciones
```

Se devuelve el total **y** cada resta, para que la pantalla pueda enseñar el
desglose. El resultado puede ser negativo y se dice: redondearlo a cero
cambiaría «ya te pasaste por RD$3,000» por «no te queda nada», que son dos
situaciones distintas y piden dos decisiones distintas.

## La base histórica

Pesos 50 / 30 / 20 sobre los tres períodos cerrados más recientes.

Con menos de tres, los pesos se recalculan sobre los que hay: con dos períodos,
50 y 30 se normalizan a 5/8 y 3/8. Asumir cero para los que faltan haría que un
usuario con un solo período viera una base histórica de la mitad de lo que
realmente gastó.

Con cero períodos cerrados, `Outcome.Insufficient`. No hay historia que
promediar y decir «cero» sería inventarla.

## La redistribución

```text
Recortar(categoría, monto):
  si categoría.prioridad ≤ 2  → rechazo, sin excepción
  si monto > disponible       → rechazo
  si no                       → ajuste negativo en esa categoría
```

Prioridad 1 y 2 son intocables. No es una advertencia que se pueda ignorar: la
función devuelve un rechazo con motivo. El alquiler no se recorta a mitad de mes
porque el algoritmo lo vea conveniente.

El destino del recorte se aplica por separado, para que mover dinero de una
categoría a otra sean dos operaciones y no una: si la segunda falla, la primera
no debe haber ocurrido.

## Qué se puede romper

| Riesgo | Señal | Qué hago |
|---|---|---|
| Un `double` se cuela por un porcentaje | ninguna, y ese es el problema | La revisión lo busca explícitamente; ninguna firma pública del motor acepta `double` |
| El reparto histórico pierde un centavo | la suma de partes ≠ total | Toda prueba de reparto lo afirma |
| Alguien llama a `DateTime.Now` dentro del motor | la prueba pasa hoy y falla a medianoche | `hoy` es parámetro obligatorio en toda función que lo necesite |
| La cobertura del 90 % se cumple con pruebas que no afirman nada | cobertura verde, defectos vivos | Cada prueba afirma una cifra concreta, no que no lanzó excepción |

## Orden

1. Proyecto, referencia a `Margen.Domain`, `Outcome<T>`.
2. `BudgetCycle` y sus pruebas. Es la base de todo lo demás.
3. `SpendingLedger`: devolución que resta, pago de tarjeta que no cuenta.
4. `SafeToSpend` y `DailyAllowance`.
5. `HistoricalBaseline`.
6. `Pace`.
7. `Redistribution`.
8. Cobertura y compuerta 1.
