# Brief — Fase 6 · Ingesta de correo, sin parser de banco

## Qué se construye

Todo el camino que recorre un correo del banco desde el buzón hasta un
movimiento guardado, **menos** la parte que lee el formato del banco.

Esa parte es la Fase 7 y está bloqueada: hace falta un correo real anonimizado.
Escribirla contra un formato supuesto es trabajo que se tira entero.

Lo que sí se puede construir ahora es el resto, y es la mayor parte: bajar del
buzón, filtrar por remitente, guardar el original, elegir qué parser lo mira,
decidir si ya se procesó, y reprocesar cuando el parser mejore.

## Por qué el andamiaje primero

Cuando llegue la muestra real, lo único que hará falta escribir es una clase que
implemente `IEmailParser`. Todo lo que la rodea —la idempotencia, el registro de
parsers, el reproceso— estará escrito y probado contra muestras sintéticas que
se generan aquí.

Es lo contrario de lo habitual: normalmente se escribe el parser primero y el
andamiaje se improvisa alrededor. Ese orden produce un andamiaje que solo
funciona con el formato que se tuvo delante.

## La decisión que gobierna esta fase

**Reprocesar el mismo correo dos veces no puede crear dos movimientos.**

No es una optimización: es la diferencia entre un saldo correcto y uno inflado.
El worker corre cada pocos minutos, el buzón devuelve lo mismo, y la red se
cae a mitad de una tanda.

Tres defensas, en este orden:

1. **`MessageId` único.** El correo ya se descargó. Ni se vuelve a guardar.
2. **Hash del cuerpo único.** El servidor de correo reescribió el identificador
   al reenviar, pero el cuerpo es el mismo.
3. **Huella del movimiento única.** Dos correos distintos —una notificación y su
   confirmación— describen la misma compra.

Las tres nacieron en la migración de la Fase 2. Esta fase las ejerce.

Y una cuarta, aproximada: dos movimientos con **el mismo importe, la misma
cuenta y el mismo comercio dentro de una ventana de minutos** son
probablemente el mismo. Esa no se decide sola: marca `Duplicate` y va a
Revisión, porque dos cafés seguidos en el mismo sitio también existen.

## Alcance

Dentro:

- `Margen.Ingest`: lógica pura de normalización, huella y detección de
  duplicados. Sin IMAP, sin base de datos.
- `IEmailParser` y el registro de parsers disponibles.
- Un parser sintético con su formato inventado, para poder probar el camino
  entero.
- Cliente IMAP en el worker, con lista blanca de remitentes.
- Guardado del correo entrante con identificador, hash y estado.
- Herramienta de reproceso.
- Pantalla de Revisión conectada al API.

Fuera:

- **El parser del banco.** Fase 7, bloqueada.
- La clasificación por reglas y el clasificador. Fase 8.

## Cómo se comprueba

| Criterio | Prueba |
|---|---|
| Lista blanca de remitentes | Un correo de fuera de la lista no se guarda ni se mira |
| Registro con identificador, hash y estado | Se guarda antes de intentar interpretarlo |
| Interfaz y registro de parsers | Se elige por remitente y asunto; hay prueba de que el orden es estable |
| Huella y duplicado exacto | El mismo correo dos veces no crea dos movimientos |
| Duplicado aproximado | Mismo importe, cuenta y comercio en minutos → `Duplicate` y a Revisión |
| Correo no reconocido | Va a `Unrecognized`, **no crea nada** |
| Reproceso sin duplicar | Una versión nueva del parser reinterpreta y actualiza, no añade |
| Pantalla de Revisión | Consume `/review` y corrige una categoría |

## Los siete riesgos

1. **Aritmética de dinero.** Aplica. El parser devuelve centavos enteros, nunca
   un texto que alguien convierta después. `Money.parse` exige que se le diga el
   formato.
2. **Idempotencia.** Aplica, y es el eje de la fase. Tres defensas con prueba
   cada una.
3. **El modelo no toca cifras.** Aplica de forma preventiva: `IEmailParser`
   devuelve un tipo cerrado con `Money` y `DateTime`; no hay ninguna ruta por la
   que un texto libre llegue a un campo monetario.
4. **Fallo cerrado.** Aplica. Un correo que ningún parser reconoce no crea nada
   y queda para revisión humana. Un parser que extrae el monto pero no la fecha
   devuelve `NeedsReview`, no la fecha de hoy.
5. **Zona horaria.** Aplica. El correo trae una hora local; se convierte a UTC
   una sola vez, con `LocalTime`, y se guarda en `timestamptz`.
6. **Superficie expuesta.** Aplica. Las credenciales de IMAP entran por entorno
   y no aparecen en ningún registro.
7. **Accesibilidad.** Aplica en la pantalla de Revisión.

## Terminado es

Los ocho criterios con prueba en verde, las dos compuertas 1 en verde y CR-007
sin Blockers ni Majors abiertos.

Al cerrarla, la Fase 7 está **BLOQUEADA** y el turno termina ahí.
