# Estado

Fase: 12 - Endurecimiento y despliegue
Rama: fase/12-despliegue
Estado: **cerrada en lo que se puede construir**
Paso: 7 de 7 (NEXT)
Vuelta: 1 de 3
Compuerta 1: verde en las dos
Revisión: CR-014 aprobada
Veredicto: **APROBADA, con el despliegue pendiente del humano**
Bloqueo: -

## El despliegue lo haces tú

Desplegar toca producción y credenciales, que es condición de parada del
`LOOP.md`. Está todo construido y verificado; el arranque está escrito paso a
paso en [`docs/despliegue.md`](despliegue.md).

Lo que sí se puede afirmar sin desplegar:

- El compose es válido y **ningún servicio publica un puerto al host**. El único
  camino de entrada es el proxy, y la base vive en una red sin salida.
- **El respaldo se restaura de verdad**, y hay una prueba que lo comprueba en
  cada tanda: `pg_dump` real, base descartable, `pg_restore --exit-on-error`, y
  se cuentan las filas y los tipos.
- Los secretos y su rotación están en [`docs/secretos.md`](secretos.md), y
  ninguno está en el repositorio.

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
| 10 · Conciliación | Cerrada | CR-011 | 56 puras + 15 de integración |

| — · Segundo banco: Qik | Cerrado | CR-012 | 18 en `Ingest` |
| — · Tercer banco: Banreservas | Cerrado | CR-013 | 19 en `Ingest` |
| 12 · Endurecimiento y despliegue | Cerrada | CR-014 | 3 de respaldo + 2 de caché |

**823 pruebas**: 730 en .NET, 93 en Flutter. Las dos compuertas 1 en verde.
Cobertura de `Margen.Classify`: 99,8 % de líneas, 98,3 % de ramas.

## Lo que necesito del humano

1. **ADR-001 sin responder**: pagar el programa de Apple o volver a PWA. Define
   el empaquetado de la Fase 11 y condiciona los avisos.
2. **Confirmar las fuentes** descargadas en la Fase 5: el README dice de dónde
   bajarlas, no qué versión.
4. **Relanzar la revisión adversarial del motor** (m15) antes de fusionar la
   Fase 3: CR-003 la respalda un solo lector.
4. **El merge a `main`.** Diez ramas publicadas, ninguna fusionada.
5. **Capturar el resto de bancos, si hay más.** Pon sus remitentes en
   `IMAP_ALLOWED_SENDERS` —separados por comas— y corre `--capturar-muestras`.
6. **Guardar los avisos que faltan** de los tres bancos. Ninguno apareció en el
   buzón y ningún parser se los inventa: lo que no reconoce va a Revisión.
   Del Popular, compra rechazada, devolución y pago de tarjeta; de Qik,
   depósito, transferencia y pago; de Banreservas, **una transacción declinada**
   —no se sabe qué palabra usa— más depósito y transferencia.

## Lo que sigue

**Ya no queda ninguna fase que yo pueda abrir.** Todo lo que falta depende de
ti:

1. **Desplegar.** El runbook está en [`docs/despliegue.md`](despliegue.md).
   Hasta que el stack corra contra datos de verdad, tres deudas de rendimiento
   —m16, m27, m30— no se pueden pagar: su condición es «con medición real», y
   medir sobre datos sembrados mediría los datos sembrados.
2. **Responder ADR-001**, que desbloquea la Fase 11.

Y cuando aparezca un banco nuevo: **un parser más**. No es una fase; es trabajo
que se hace cada vez, y el andamiaje ya está probado tres veces.

## Notas de entorno

- Remoto `origin` en `github.com/ElwinsZorrilla/personal-finance`. Ramas:
  `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`, `fase/4-endpoints`,
  `fase/5-datos-flutter`, `fase/6-ingesta-correo`, `fase/7-parser-banco`,
  `fase/8-clasificacion`, `fase/9-efectivo-iphone`, `fase/10-conciliacion`,
  `bancos/qik`, `bancos/banreservas`, `fase/12-despliegue`.
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
