# Brief — Fase 4 · Endpoints y contrato

## Qué se construye

La superficie HTTP que une lo que ya existe. La Fase 2 dejó el esquema y la
autenticación; la Fase 3 dejó el motor. Esta fase es la que los conecta y los
publica: leer de la base, pasarle al motor lo que necesita, devolver el
resultado, y fijar ese formato en un contrato versionado que el cliente Flutter
pueda consumir en la Fase 5.

Es una fase de plomería, y eso tiene una consecuencia: **el riesgo no está en
los cálculos, está en las consultas**. El motor ya está probado. Lo que aquí
puede salir mal es traerle datos equivocados —duplicados por un join, un
período mal recortado, una fecha convertida dos veces— y que el motor calcule
impecablemente sobre entradas malas.

## Por qué así

**El agregador es lo único nuevo con lógica.** Entre la base y el motor hace
falta una capa que construya `SafeToSpendInputs`, `LedgerEntry` y
`CategoryAllocation`. Ahí es donde se decide qué es una «obligación pendiente» y
qué cae dentro del ciclo. Se prueba contra Postgres real, porque su trabajo es
precisamente la consulta.

**La conversión de zona ocurre una sola vez, aquí.** El motor trabaja en
`DateOnly` de hora local y la base guarda `timestamptz`. La traducción vive en
el agregador, en un solo sitio, y tiene prueba propia. Repartirla entre varias
consultas es garantizar que una se olvide.

**El contrato se versiona en el repositorio.** `docs/api/openapi.json` generado
y commiteado. Un contrato que solo existe en tiempo de ejecución no se puede
comparar entre versiones, y la Fase 5 necesita saber cuándo cambia.

**El límite de peticiones llega ahora**, no antes. La Fase 2 lo dejó anotado
como deuda porque un limitador sobre endpoints sin tráfico es código que no se
puede comprobar. Ahora hay endpoints que proteger y se puede probar que
devuelven 429.

## Alcance

Dentro:

- Agregador entre la base y el motor, con pruebas sobre Postgres real.
- Endpoints: panel, transacciones, presupuesto, revisión, reglas, correos
  entrantes, conciliación, notificaciones.
- OpenAPI generado y versionado en `docs/api/openapi.json`.
- ProblemDetails en todo error. Ningún 500 con traza al cliente.
- Límite de peticiones en autenticación y en el registro rápido.
- Pago de la deuda m9, m10, m11 y m12 de CR-002.

Fuera:

- Cualquier cosa que toque `app/`. Fase 5.
- El parser del banco. Fase 7, bloqueada.
- La interpretación de «Gasté 450 pesos en almuerzo». Fase 9. La ruta
  `/transactions/cash` sigue devolviendo 501 y su deuda es de esa fase.

## Cómo se comprueba

| Criterio | Prueba |
|---|---|
| Endpoints responden con datos reales | Integración sobre Postgres en contenedor, sembrando filas y afirmando cifras |
| OpenAPI versionado | El archivo existe y una prueba falla si el generado difiere del commiteado |
| ProblemDetails en todo error | Una prueba por familia de error; ninguna respuesta con `StackTrace` |
| Ningún 500 con traza | Un endpoint que lanza a propósito, en entorno de producción |
| Límite de peticiones | N+1 peticiones al canje de reto devuelven 429 |
| Postgres real, no en memoria | Ya es el patrón de la Fase 2 |

## Los siete riesgos, aplicados a esta fase

1. **Aritmética de dinero.** Aplica al serializar: los centavos viajan como
   entero en el JSON, nunca como decimal. Prueba de que el contrato no tiene un
   solo `number` con coma.
2. **Idempotencia.** Aplica. El agregador no puede pasarle al motor el mismo
   movimiento dos veces; el motor ya lo rechaza y aquí hay que no provocarlo.
   Un join mal escrito es la causa más probable.
3. **El modelo no toca cifras.** No aplica todavía.
4. **Fallo cerrado.** Aplica. Un `Outcome` que no es `Computed` no se convierte
   en un cero en el JSON: se convierte en un campo nulo con su motivo, y la
   pantalla decide qué decir.
5. **Zona horaria.** Aplica, y es el riesgo alto de esta fase. La conversión
   vive en un solo sitio y tiene prueba de las 23:30 de punta a punta: sembrar
   un movimiento, pedir el panel, comprobar que cae en el ciclo correcto.
6. **Superficie expuesta.** Aplica. Ningún endpoint nuevo sin autorización
   explícita; la política por defecto ya lo exige y hay que no romperla.
7. **Accesibilidad.** No aplica.

## Terminado es

Los criterios de arriba marcados con prueba en verde, la deuda m9–m12 pagada,
compuerta 1 en verde y CR-004 sin Blockers ni Majors abiertos.
