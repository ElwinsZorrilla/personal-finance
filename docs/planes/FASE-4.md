# Plan — Fase 4 · Endpoints y contrato

## Árbol

```text
src/Margen.Api/
  Budget/
    LocalTime.cs           la única conversión UTC ↔ Santo Domingo del sistema
    BudgetAssembler.cs     de la base a las entradas del motor
    DashboardView.cs       lo que sale por el cable
  Endpoints/
    DashboardEndpoints.cs  GET /dashboard
    TransactionEndpoints.cs
    BudgetEndpoints.cs
    ReviewEndpoints.cs
    RuleEndpoints.cs
    EmailEndpoints.cs
    ReconciliationEndpoints.cs
    NotificationEndpoints.cs
    CashEndpoints.cs       deja de ser 501
  Contracts/               registros de petición y respuesta
  RateLimits.cs
  CurrentDevice.cs         paga m12: extraer el dispositivo en un solo sitio

docs/api/openapi.json      contrato versionado

tests/Margen.Api.Tests/
  Infra/Seed.cs            sembrado de datos para las pruebas de endpoint
  DashboardTests.cs  TransactionTests.cs  BudgetTests.cs
  ReviewTests.cs  RuleTests.cs  ContractTests.cs
  ProblemDetailsTests.cs  RateLimitTests.cs
  AssemblerTimeZoneTests.cs
```

## La pieza que importa

`BudgetAssembler` es lo único nuevo con lógica. Todo lo demás es transporte.

El motor ya está probado al 100 %. Lo que puede salir mal aquí es **traerle
datos equivocados** y que calcule impecablemente sobre ellos:

- Un `join` que multiplique filas y le pase el mismo movimiento dos veces. El
  motor ya lo rechaza desde CR-003; el agregador no debe provocarlo.
- Un recorte de ciclo hecho en UTC en vez de en hora local, que dejaría fuera
  las compras de después de las 8 de la noche del último día.
- Una devolución cuya compra original cae fuera de la ventana consultada, que
  se imputaría a la categoría equivocada.

## La conversión de zona, en un solo sitio

`LocalTime` es la única clase del sistema que convierte. Dos operaciones:

```text
DateOnly LocalDateOf(DateTime utc)        para clasificar un movimiento
(DateTime desde, DateTime hasta) RangeOf(BudgetCycle)   para consultar
```

El rango se calcula como `[inicio 00:00 local → UTC, (fin+1) 00:00 local → UTC)`,
medio abierto. Consultar con `<= fin` en UTC dejaría fuera las cuatro últimas
horas del último día del ciclo, que en hora local son las de más gasto.

La ventana de consulta se **amplía** hacia atrás para traer las compras que
alguna devolución del ciclo referencia. Sin eso, `ResolveCategory` no encuentra
el original y lo imputa mal.

## Qué significa cada resta

El motor recibe cifras; quién las define es este archivo. Se escribe aquí porque
son decisiones de producto, no de cálculo:

| Resta | De dónde sale |
|---|---|
| `Liquid` | Suma de saldos de cuentas activas que no son de crédito |
| `PendingObligations` | Pagos recurrentes activos que vencen dentro del ciclo y aún no se pagaron |
| `CardReserve` | Deuda de las tarjetas: saldo negativo de las cuentas de crédito, en valor absoluto |
| `CommittedSavings` | Del período |
| `SafetyFund` | Del período |
| `Withholdings` | Movimientos en estado `Pending`: autorizados y todavía no descontados del saldo |

`Withholdings` y el gasto del ciclo se solapan a propósito: el saldo del banco
todavía no refleja lo pendiente, así que hay que restarlo del líquido aunque ya
cuente como gasto. Si no se restara, el dinero seguro sería mayor de lo real
durante los dos o tres días que el banco tarda en asentar.

## Sin cálculo en el cliente

El panel sale resuelto: cifras finales, no ingredientes. Es la regla del
proyecto y la razón por la que `DashboardView` no lleva listas de las que haya
que sumar nada en pantalla.

Un `Outcome` que no es `Computed` se serializa como campo **nulo con su motivo**,
nunca como cero. La pantalla decide qué decir; lo que no puede es recibir un
cero que parezca una cifra.

## Deuda que se paga

| # | Qué |
|---|---|
| m9 | Alta simultánea del mismo teléfono devuelve el dispositivo en vez de 500 |
| m10 | Límite de peticiones en autenticación y en el registro rápido |
| m11 | `/transactions/cash` deja de ser 501; la interpretación de texto sigue siendo la Fase 9 |
| m12 | `CurrentDevice` extrae el identificador en un sitio, sin `Guid.Parse(...!)` repetido |

## Orden

1. `LocalTime` y sus pruebas. Es la base de todo lo demás.
2. `BudgetAssembler` y `GET /dashboard`, con prueba de las 23:30 de punta a punta.
3. Endpoints de lectura: transacciones, presupuesto, revisión, reglas, correos,
   conciliación, notificaciones.
4. Endpoints de escritura: efectivo, corrección de categoría, redistribución.
5. ProblemDetails, límite de peticiones, deuda m9 y m12.
6. OpenAPI generado y versionado, con prueba que falla si diverge.
7. Compuerta 1 y CR-004.
