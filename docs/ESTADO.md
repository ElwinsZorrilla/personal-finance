# Estado

Fase: 7 - Parser del banco
Rama: fase/7-parser-banco
Estado: **cerrada**
Paso: 7 de 7 (NEXT)
Vuelta: 4 de 3 - se pasó de tres, y las cuatro fueron correcciones aceptadas,
        no discusión sobre el mismo punto
Compuerta 1: verde en las dos
Revisión: CR-008 aprobada
Veredicto: **APROBADA**
Bloqueo: -

## Lo que lee el parser

Las **dieciséis muestras reales** del Banco Popular, sin una a revisión:

| Aviso | Muestras | Tipo | Dirección |
|---|---|---|---|
| Notificación de Consumo | 6 | Compra | Egreso |
| Notificación de Retiro | 6 | Retiro | Egreso |
| Pagos al Instante transferencia enviada | 2 | Transferencia | Egreso |
| Depósito por ATM | 2 | Depósito | **Ingreso** |

Lo que no reconoce no crea nada: queda en Revisión con el motivo escrito.

Faltan tres avisos que no aparecieron en los 209 correos del buzón -compra
rechazada, devolución y pago de tarjeta-. El parser ya los contempla; cuando
lleguen, `--reprocesar` los pasa por la versión nueva sin duplicar nada.

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

**481 pruebas**: 390 en .NET, 91 en Flutter. Las dos compuertas 1 en verde.

## Lo que necesito del humano

1. **ADR-001 sin responder**: pagar el programa de Apple o volver a PWA. Define
   el empaquetado de la Fase 11 y condiciona los avisos.
2. **Confirmar las fuentes** descargadas en la Fase 5: el README dice de dónde
   bajarlas, no qué versión.
4. **Relanzar la revisión adversarial del motor** (m15) antes de fusionar la
   Fase 3: CR-003 la respalda un solo lector.
4. **El merge a `main`.** Siete ramas publicadas, ninguna fusionada.
5. **Guardar los avisos que faltan** si te llegan: una compra rechazada, una
   devolución y un pago de tarjeta. Con `--capturar-muestras`, que abre el buzón
   en solo lectura y no borra nada.

## Lo que sigue

**Fase 8 - clasificación y anomalías.** La cascada: regla exacta -> patrón ->
historial -> clasificador -> modelo -> revisión. Ahora se puede probar contra
comercios que el banco escribió de verdad -`UBER*EATS`, `UBER EATS-WB*UBER
EATS-WB`, `OPENAI *CHATGPT SUBSCR`, `SM NACIONAL CHARLES`-, que es justo el
material que necesita la normalización.

Después: 9 (efectivo desde el iPhone), 10 (conciliación, a medio bloquear hasta
conocer el CSV del banco), 12 (endurecimiento y despliegue). La 11 depende de
ADR-001.

## Notas de entorno

- Remoto `origin` en `github.com/ElwinsZorrilla/personal-finance`. Ramas:
  `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`, `fase/4-endpoints`,
  `fase/5-datos-flutter`, `fase/6-ingesta-correo`, `fase/7-parser-banco`.
- .NET 10.0.204, Docker 29.5.3 y Flutter 3.44.5. Flutter está en
  `C:\src\flutter` pero **no en el PATH**: hay que añadirlo a mano.
- Regenerar el contrato tras cambiar la superficie del API:
  `dotnet run --project src/Margen.Api -- --generar-contrato`
- Reprocesar correos contra una versión nueva del parser:
  `dotnet run --project src/Margen.Worker -- --reprocesar`
- Capturar muestras nuevas del buzón (solo lectura, no toca la base):
  `dotnet run --project src/Margen.Worker -- --capturar-muestras`
  Las credenciales salen de `infra/.env`; los pasos, de [`docs/gmail.md`](gmail.md).
