# Estado

Fase: 8 - Clasificación y anomalías
Rama: fase/8-clasificacion
Estado: **cerrada**
Paso: 7 de 7 (NEXT)
Vuelta: 2 de 3
Compuerta 1: verde en las dos
Revisión: CR-009 aprobada
Veredicto: **APROBADA**
Bloqueo: -

## La cascada

En este orden, y el primero que responda gana:

| # | Escalón | Confianza | ¿Cierra solo? |
|---|---|---|---|
| 1 | Regla exacta del usuario | 10000 | sí |
| 2 | Regla por patrón del usuario | 9500 | sí |
| 3 | Historial **confirmado** del comercio | dominancia real, tope 9000 | si llega a 8000 |
| 4 | Clasificador local | 7000 | no |
| 5 | Modelo | 5000, tope duro | **no, nunca** |
| 6 | Revisión humana | - | - |

Corregir un movimiento crea la regla del escalón 1, y la siguiente compra en ese
comercio ya no pregunta ni llama a nadie.

**El modelo sugiere y no decide**, defendido tres veces: la firma no admite una
cifra, el número queda bajo el umbral, y `IsAutomatic` comprueba además el
origen —así que bajar el umbral no abre esa puerta—.

No hay ningún proveedor de modelo enchufado: sin `ICategorySuggester`
registrado, la cascada corre sus cuatro escalones locales. Elegir proveedor es
una decisión que no está tomada, y tomarla a escondidas sería peor que no
tenerla.

## Las anomalías

Gasto inusual, suscripción que cambia de precio y recurrente que no llegó, cada
una con su clave de deduplicación: mirar el reloj diez veces no genera diez
alertas.

Lo normal se mide con **mediana y desviación absoluta mediana**, enteras. Con
menos de cinco cargos de un comercio no hay rango y no se avisa de nada.

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

**634 pruebas**: 543 en .NET, 91 en Flutter. Las dos compuertas 1 en verde.
Cobertura de `Margen.Classify`: 99,8 % de líneas, 98,3 % de ramas.

## Lo que necesito del humano

1. **ADR-001 sin responder**: pagar el programa de Apple o volver a PWA. Define
   el empaquetado de la Fase 11 y condiciona los avisos.
2. **Confirmar las fuentes** descargadas en la Fase 5: el README dice de dónde
   bajarlas, no qué versión.
4. **Relanzar la revisión adversarial del motor** (m15) antes de fusionar la
   Fase 3: CR-003 la respalda un solo lector.
4. **El merge a `main`.** Ocho ramas publicadas, ninguna fusionada.
5. **Guardar los avisos que faltan** si te llegan: una compra rechazada, una
   devolución y un pago de tarjeta. Con `--capturar-muestras`, que abre el buzón
   en solo lectura y no borra nada.

## Lo que sigue

**Fase 9 - efectivo desde el iPhone.** El endpoint existe desde la Fase 4 y
ahora lo que entre por ahí también se clasifica. Falta interpretar «Gasté 450
pesos en almuerzo», el token restringido y documentar el Atajo.

Después: 10 (conciliación, a medio bloquear hasta conocer el CSV del banco) y 12
(endurecimiento y despliegue). La 11 depende de ADR-001.

## Notas de entorno

- Remoto `origin` en `github.com/ElwinsZorrilla/personal-finance`. Ramas:
  `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`, `fase/4-endpoints`,
  `fase/5-datos-flutter`, `fase/6-ingesta-correo`, `fase/7-parser-banco`,
  `fase/8-clasificacion`.
- .NET 10.0.204, Docker 29.5.3 y Flutter 3.44.5. Flutter está en
  `C:\src\flutter` pero **no en el PATH**: hay que añadirlo a mano.
- Regenerar el contrato tras cambiar la superficie del API:
  `dotnet run --project src/Margen.Api -- --generar-contrato`
- Reprocesar correos contra una versión nueva del parser:
  `dotnet run --project src/Margen.Worker -- --reprocesar`
- Capturar muestras nuevas del buzón (solo lectura, no toca la base):
  `dotnet run --project src/Margen.Worker -- --capturar-muestras`
  Las credenciales salen de `infra/.env`; los pasos, de [`docs/gmail.md`](gmail.md).
