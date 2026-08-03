# Plan — Fase 8: clasificación y anomalías

## Dónde vive

Proyecto nuevo **`src/Margen.Classify/`**, con la misma regla que
`Margen.Budget`: lógica pura, sin HTTP y sin base de datos. Referencia
únicamente a `Margen.Domain`.

No va dentro de `Margen.Ingest` aunque la ingesta sea quien lo llame. Ingest
lee correos; Classify decide categorías. El día que entre efectivo por el Atajo
—Fase 9— o un CSV —Fase 10—, esos también se clasifican y no pasan por ningún
correo.

`Outcome<T>` se mueve de `Margen.Budget` a `Margen.Domain`. Es un concepto del
dominio —«no se pudo calcular»— y ahora lo necesitan dos motores; dejarlo donde
está obligaría a Classify a referenciar el motor de presupuesto para pedirle
prestado un tipo que no tiene nada que ver con presupuestos.

## Los archivos

| Archivo | Qué hace |
|---|---|
| `Classification.cs` | El resultado: categoría, confianza en puntos básicos, de qué escalón salió y por qué |
| `Cascade.cs` | Los seis escalones en orden. Es el único que decide |
| `RuleMatcher.cs` | Compara un comercio con una regla. Exacta, contiene, expresión regular con tiempo límite |
| `MerchantHistory.cs` | La categoría dominante de un comercio y su peso |
| `LocalClassifier.cs` | Tabla de palabras a nombre de categoría. Sin red |
| `ICategorySuggester.cs` | El puerto del modelo. La firma es la barrera |
| `Statistics.cs` | Mediana y desviación absoluta mediana, enteras |
| `NormalRange.cs` | El rango de lo normal para un comercio |
| `Anomalies.cs` | Las cuatro detecciones |
| `Corrections.cs` | La corrección del usuario a regla |

## El orden de construcción

Cada paso deja pruebas antes de pasar al siguiente.

1. **Mover `Outcome<T>` a `Margen.Domain`.** Es refactor puro: si algo se rompe
   se ve en la compilación, no en producción. Va primero para que no contamine
   los diffs de lo demás.

2. **`Statistics` y `NormalRange`.** La aritmética antes que nada, porque es
   donde está el riesgo 1. Mediana de lista par e impar, desviación absoluta
   mediana, y el corte por debajo de cinco muestras.

3. **`RuleMatcher`.** Los tres modos. El tiempo límite de la expresión regular
   con una prueba que use un patrón catastrófico de verdad.

4. **`MerchantHistory`.** Categoría dominante, mínimo de muestras y mínimo de
   dominancia. Un empate no es una respuesta.

5. **`LocalClassifier`.** Sembrado con los comercios que el banco escribió de
   verdad en la Fase 7.

6. **`ICategorySuggester` y `Cascade`.** El orden, el umbral, y que el escalón 5
   nunca cierre la clasificación solo.

7. **`Anomalies`.** Las cuatro, cada una con su clave de deduplicación.

8. **`Corrections`.** Crear o actualizar la regla, sin apilar.

9. **Enchufar.** La ingesta clasifica al crear el movimiento; el `PUT` de
   movimiento crea la regla al corregir; un servicio de anomalías escribe
   alertas. Contrato regenerado si cambia la superficie.

## Lo que hay que decidir al escribir

### El umbral de auto-asignación

**8000 puntos básicos** —80 %—. Por encima, el movimiento queda `Posted` con su
categoría. Por debajo, `NeedsReview` con la sugerencia puesta.

Los escalones dan:

| Escalón | Confianza |
|---|---|
| Regla exacta del usuario | 10000 |
| Regla por patrón del usuario | 9500 |
| Historial | la dominancia real, tope 9000 |
| Clasificador local | 7000 |
| Modelo | 5000, **tope duro** |

El clasificador local queda a propósito **por debajo del umbral**: acierta con
lo que conoce y no tiene manera de saber que no conoce algo. `SM NACIONAL` es
un supermercado y `SM` a secas puede ser cualquier cosa. Con 7000 sugiere y
pide confirmación, y la primera confirmación crea la regla que lo sube a 10000
para siempre.

El modelo con tope duro a 5000 es la decisión 1 del brief escrita en un número:
aunque alguien suba el umbral, el modelo sigue sin poder cerrar una
clasificación.

### El factor del rango

**Mediana ± 3 × desviación absoluta mediana**, con un suelo: si la desviación
sale cero —cinco cargos idénticos, que es lo normal en una suscripción—, el
rango sería un punto y el sexto cargo de un peso más saldría como anómalo. El
suelo es el **10 % de la mediana**, que en una suscripción de 1 200 pesos deja
pasar hasta 120 de diferencia sin decir nada.

### La clave de deduplicación

Por anomalía y por lo que la causó, no por instante:

| Anomalía | Clave |
|---|---|
| Gasto inusual | `unusual:<transactionId>` |
| Suscripción que subió | `subprice:<recurringId>:<centavosNuevos>` |
| Recurrente que no llegó | `missing:<recurringId>:<vencimientoLocal>` |

El importe entra en la clave de la suscripción a propósito: si sube otra vez,
es una alerta nueva y no la misma. El vencimiento entra en la de la factura por
lo mismo —la de agosto no es la de julio— y en **fecha local**, que es como se
vencen las facturas.

## Lo que puede salir mal

- **El historial se muerde la cola.** Si el clasificador local pone diez
  movimientos en una categoría equivocada y el historial los lee, se confirma a
  sí mismo. El historial cuenta **solo movimientos confirmados**: los que salen
  de una regla de usuario, de una corrección o de una confirmación en Revisión.
  Nunca los que puso el propio automatismo sin confirmar.
- **La expresión regular del usuario.** Tope de longitud y tiempo límite, y el
  agotamiento del tiempo cuenta como «no casa».
- **Alertas a montones el primer día.** Al importar historia vieja, cada cargo
  antiguo podría disparar su alerta. La detección de gasto inusual solo mira
  movimientos de los últimos días.
