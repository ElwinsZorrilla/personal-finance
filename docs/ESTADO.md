# Estado

Fase: 9 - Efectivo desde el iPhone
Rama: fase/9-efectivo-iphone
Estado: **cerrada**
Paso: 7 de 7 (NEXT)
Vuelta: 1 de 3
Compuerta 1: verde en las dos
Revisión: CR-010 aprobada
Veredicto: **APROBADA**
Bloqueo: -

## El Atajo

> «Oye Siri, gasté 450 pesos en almuerzo»

Los pasos exactos están en [`docs/atajo.md`](atajo.md), sin un solo secreto
dentro: el dominio y el token se ponen al montarlo.

Dos rutas al mismo sitio, las dos con el token de alcance `cash:create`:

| Ruta | Cuerpo | Quién la usa |
|---|---|---|
| `POST /transactions/cash` | monto en centavos enteros | La app, que ya tiene teclado numérico |
| `POST /transactions/cash/phrase` | la frase | El Atajo de iOS |

**El monto lo saca una expresión regular, nunca un modelo.** El día y la moneda
también. Lo que sí puede sugerir un modelo es la categoría, por la cascada de la
Fase 8, y allí tampoco decide solo.

Lo que no se entiende no crea nada y no toca el saldo: sin monto, sin
descripción, en moneda extranjera, en cero o por encima del tope. Los numerales
en palabras se rechazan a propósito —el dictado de iOS ya escribe dígitos, y
media implementación enseñaría que funciona para fallar un día cualquiera—.

## Lo hecho

| Fase | Estado | Revisión | Pruebas |
|---|---|---|---|
| 1 · Sistema visual, dominio, infraestructura | Cerrada | CR-001, CR-005 | 86 en `app/` |
| 2 · Base del API, esquema, autenticación | Cerrada | CR-002 | incluidas en las 105 del API |
| 3 · Motor de presupuesto | Cerrada | CR-003 | 101, cobertura 100 % |
| 4 · Endpoints y contrato | Cerrada | CR-004 | 105 de integración |
| 5 · Capa de datos en Flutter | Cerrada | CR-006 | incluidas en las 86 |
| 6 · Ingesta de correo | Cerrada | CR-007 | 48 puras + integración |
| 7 · Parser del Banco Popular | Cerrada | CR-008 | 105 en `Ingest` |
| 8 · Clasificación y anomalías | Cerrada | CR-009 | 136 en `Classify` + 17 de integración |
| 9 · Efectivo desde el iPhone | Cerrada | CR-010 | 41 entre `Ingest` e integración |

**692 pruebas**: 601 en .NET, 91 en Flutter. Las dos compuertas 1 en verde.
Cobertura de `Margen.Classify`: 99,8 % de líneas, 98,3 % de ramas.

## Lo que necesito del humano

1. **ADR-001 sin responder**: pagar el programa de Apple o volver a PWA. Define
   el empaquetado de la Fase 11 y condiciona los avisos.
2. **Confirmar las fuentes** descargadas en la Fase 5: el README dice de dónde
   bajarlas, no qué versión.
4. **Relanzar la revisión adversarial del motor** (m15) antes de fusionar la
   Fase 3: CR-003 la respalda un solo lector.
4. **El merge a `main`.** Nueve ramas publicadas, ninguna fusionada.
5. **Guardar los avisos que faltan** si te llegan: una compra rechazada, una
   devolución y un pago de tarjeta. Con `--capturar-muestras`, que abre el buzón
   en solo lectura y no borra nada.

## Lo que sigue

**Fase 10 - conciliación.** Importar el estado de cuenta en CSV, cuadrarlo
contra lo registrado y cerrar el período generando el presupuesto recomendado
del siguiente. Está **a medio bloquear**: la importación necesita saber cómo es
el CSV que exporta el Banco Popular, y escribir el mapeo contra un formato
supuesto es lo que bloqueó la Fase 7.

El cierre de período y los estados de conciliación sí se pueden construir sin
eso.

Después: 12 (endurecimiento y despliegue). La 11 depende de ADR-001.

## Notas de entorno

- Remoto `origin` en `github.com/ElwinsZorrilla/personal-finance`. Ramas:
  `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`, `fase/4-endpoints`,
  `fase/5-datos-flutter`, `fase/6-ingesta-correo`, `fase/7-parser-banco`,
  `fase/8-clasificacion`, `fase/9-efectivo-iphone`.
- .NET 10.0.204, Docker 29.5.3 y Flutter 3.44.5. Flutter está en
  `C:\src\flutter` pero **no en el PATH**: hay que añadirlo a mano.
- Regenerar el contrato tras cambiar la superficie del API:
  `dotnet run --project src/Margen.Api -- --generar-contrato`
- Reprocesar correos contra una versión nueva del parser:
  `dotnet run --project src/Margen.Worker -- --reprocesar`
- Montar el Atajo de iOS: [`docs/atajo.md`](atajo.md).
- Capturar muestras nuevas del buzón (solo lectura, no toca la base):
  `dotnet run --project src/Margen.Worker -- --capturar-muestras`
  Las credenciales salen de `infra/.env`; los pasos, de [`docs/gmail.md`](gmail.md).
