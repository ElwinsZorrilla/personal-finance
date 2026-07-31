# Brief — Fase 3 · Motor de presupuesto

## Qué se construye

La respuesta a la única pregunta del producto: **¿cuánto puedo gastar sin
afectar mis compromisos?**

Lógica pura. Sin HTTP, sin base de datos, sin reloj del sistema. Un proyecto que
recibe una fotografía del estado y devuelve otra fotografía con las cifras
resueltas. Todo lo que necesita entra por parámetro.

Esa restricción no es purismo: es lo que hace que las pruebas de esta fase valgan
algo. Un motor que consulta la base solo se puede probar levantando una base, y
entonces cada caso de borde cuesta un contenedor. Aquí un caso de borde cuesta
tres líneas, y por eso se pueden escribir los cuarenta que hacen falta.

## Por qué esta fase importa más que las otras

Es la parte con más riesgo de error silencioso del sistema entero.

Un widget mal alineado se ve. Un endpoint caído se ve. Una división que pierde
un centavo por categoría durante seis meses no se ve: produce una cifra que
parece razonable, que nadie puede refutar de memoria, y sobre la que alguien
toma decisiones de dinero real. Por eso la cobertura mínima aquí es 90 % y no
80 %.

## Las decisiones

**El período va de ingreso a ingreso.** No del 1 al 30. Una persona asalariada
cobra el 25, y del 25 al 24 es su ciclo real: es cuando el dinero entra y cuando
se acaba. Un período de mes natural parte la quincena en dos y produce un
«disponible diario» que no se parece a nada.

**`DineroSeguro` se calcula restando, en un orden fijo.**

```text
DineroSeguro = líquido
             − obligaciones pendientes
             − reserva de tarjetas
             − ahorro comprometido
             − fondo de seguridad
             − retenciones
```

Cada resta tiene un nombre y se puede enseñar por separado en pantalla. Un solo
número sin desglose es un número que el usuario no puede discutir, y cuando no
lo puede discutir deja de creerlo.

**Puede dar negativo, y se dice.** Redondear a cero el resultado sería la mentira
más cara del producto: significa «no te pases» cuando la verdad es «ya te
pasaste, y por cuánto». El tipo que lo devuelve distingue los dos casos.

**El disponible diario nunca divide por cero.** El último día del período quedan
1 días, no 0. Ya está resuelto así en `BudgetPeriod` del cliente y aquí se
repite con prueba propia.

**La base histórica pesa 50 / 30 / 20** —último período, anterior, tercero— y se
reparte con `Money.Prorate`, que usa el método del resto mayor. La suma de las
partes es exactamente el total. Con menos períodos que tres, los pesos se
recalculan sobre los que hay en lugar de asumir cero: un usuario nuevo tiene un
período, no una base histórica de cero pesos.

**La redistribución no toca prioridad 1 ni 2.** No es una recomendación ni un
aviso: la función devuelve un rechazo cuando se le pide recortar alquiler. Lo
comprueba una prueba.

**Una devolución resta del gasto de su categoría original**, no suma como
ingreso. **Un pago de tarjeta mueve saldo entre cuentas propias y no crea
gasto.** Las dos cosas ya están en `Transaction.SpendingEffect` desde la Fase 2;
esta fase las ejerce y las prueba de punta a punta.

## Alcance

Dentro:

- Proyecto `Margen.Budget` y su proyecto de pruebas.
- Cálculo del período, del dinero seguro, del disponible diario.
- Base histórica, proyección de cierre y desviación de ritmo.
- Redistribución con prioridades intocables.
- Cobertura ≥ 90 % en `Margen.Budget`.

Fuera, y a propósito:

- Cualquier endpoint que exponga esto. Fase 4.
- Cualquier consulta a la base. El motor recibe datos, no los busca.
- Cualquier decisión de presentación: formato, color, redacción. El motor
  devuelve centavos y hechos.

## Cómo se comprueba

| Criterio | Prueba |
|---|---|
| Período de ingreso a ingreso | `PeriodTests` — un período del 25 al 24 y otro de quincena |
| Fórmula del dinero seguro | `SafeToSpendTests` — cada resta por separado y el total |
| Disponible diario sin división por cero | `SafeToSpendTests.el_ultimo_dia_no_divide_por_cero` |
| Base histórica 50 / 30 / 20 | `HistoryTests` — con tres períodos, con dos, con uno |
| Proyección de cierre y ritmo | `PaceTests` |
| Redistribución no toca 1 ni 2 | `RedistributionTests.recortar_el_alquiler_falla` |
| Devolución reduce su categoría | `SpendingTests.una_devolucion_resta_de_su_categoria` |
| Pago de tarjeta no crea gasto | `SpendingTests.un_pago_de_tarjeta_no_es_gasto` |
| Cobertura ≥ 90 % | `dotnet test --collect:"XPlat Code Coverage"` |

## Los siete riesgos, aplicados a esta fase

1. **Aritmética de dinero.** Aplica, y es el riesgo central. Ninguna operación
   del motor toma un `double`. Los porcentajes son fracciones de enteros; los
   repartos usan `Prorate`; toda prueba de reparto afirma que la suma de las
   partes es exactamente el total.
2. **Idempotencia.** No aplica. El motor no escribe nada.
3. **El modelo no toca cifras.** No aplica. No hay modelo en esta fase.
4. **Fallo cerrado.** Aplica. Sin datos suficientes para una cifra —menos de un
   período, un período sin días—, el motor devuelve «no se puede calcular» y no
   un cero. Un cero por defecto es una mentira con formato de número.
5. **Zona horaria.** Aplica. El motor no llama al reloj: la fecha de hoy entra
   por parámetro, ya convertida a hora de Santo Domingo. Un `DateTime.Now`
   dentro del motor lo haría dependiente de la zona del servidor y de la hora a
   la que corra la prueba.
6. **Superficie expuesta.** No aplica. Sin red ni configuración.
7. **Accesibilidad.** No aplica. Sin interfaz.

## Terminado es

Los nueve criterios marcados, cada uno con prueba en verde, cobertura ≥ 90 % en
`Margen.Budget`, compuerta 1 en verde y CR-003 sin Blockers ni Majors abiertos.
