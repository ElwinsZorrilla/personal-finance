# Estado

Fase: 5 — Capa de datos en Flutter
Rama: fase/5-datos-flutter
Estado: por empezar
Paso: brief
Vuelta: 0 de 3
Compuerta 1: sin ejecutar
Revisión: —
Veredicto: —
Bloqueo: **sí, parcial** — ver «Lo que hace falta antes»

## Criterios de esta fase

- [ ] cliente HTTP con reintento y expiración
- [ ] caché local que abre la app sin señal con el último panel conocido, con la fecha de esa lectura visible
- [ ] el repositorio de prueba deja de usarse en release
- [ ] el panel consume datos reales; ninguna cifra calculada en el cliente
- [ ] pago de la deuda m6 y m7 de CR-001

## Lo que hace falta antes

**No hay toolchain de Flutter en esta máquina.** `flutter` no está en el PATH.
La Fase 5 es entera de `app/`, así que no se puede ni construir ni verificar:
la compuerta 1 de Flutter —`dart format`, `flutter analyze --fatal-infos`,
`flutter test --coverage`— es lo primero que pide el LOOP y no se puede correr.

Es la misma carencia que arrastra CR-001 desde la Fase 1.

## Lo hecho

| Fase | Estado | Revisión | Pruebas |
|---|---|---|---|
| 1 · Sistema visual, dominio, infraestructura | Cerrada | CR-001 | compuerta 1 de Flutter sin correr |
| 2 · Base del API, esquema, autenticación | Cerrada | CR-002 | 41 de integración |
| 3 · Motor de presupuesto | Cerrada | CR-003 | 101, cobertura 100 % |
| 4 · Endpoints y contrato | Cerrada | CR-004 | 90 de integración |

Total en la solución: **234 pruebas**, compuerta 1 de .NET en verde.

El servidor está completo de punta a punta: esquema, autenticación, motor y
endpoints. `docs/api/openapi.json` fija el contrato con 19 rutas y hay una
prueba que falla si el servidor deja de coincidir con él.

Lo que la Fase 5 hereda:

- **El contrato** en `docs/api/openapi.json`. Es de dónde sale el cliente.
- **Todo monto es entero de centavos** en el JSON. `Money.cents` en Dart
  recibe el entero tal cual; no hay que interpretar ningún decimal.
- **`GET /dashboard`** devuelve el panel ya resuelto. `DashboardSnapshot` en
  Dart es su contraparte: ninguna cifra se calcula en el cliente.
- **Un campo que puede no existir** llega como `{"cents": null, "unavailable": "..."}`.
  La pantalla tiene que saber decir «no se pudo calcular», no enseñar un cero.
- **La autenticación** es alta con código, reto, firma P-256 y token opaco. En
  iOS la clave privada va en el Enclave Seguro. El mensaje que se firma está en
  `Margen.Domain.DeviceAuth` y hay que replicarlo byte a byte.

## Pendiente que no es de esta fase

- **Relanzar la revisión adversarial del motor** (m15). La de la Fase 3 falló
  entera por límite de sesión y CR-003 la respalda un solo lector.
- **ADR-001 sin responder**: pagar el programa de Apple o volver a PWA. Define
  el empaquetado de la Fase 11 y condiciona los avisos.
- El merge a `main` de las fases 2, 3 y 4 lo hace el humano. Las cuatro ramas
  están publicadas.

## Notas de entorno

- Remoto `origin` en `github.com/ElwinsZorrilla/personal-finance`. Ramas:
  `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`, `fase/4-endpoints`.
- .NET 10.0.204 y Docker 29.5.3 en funcionamiento. **Flutter, ausente.**
- Regenerar el contrato tras cambiar la superficie del API:
  `dotnet run --project src/Margen.Api -- --generar-contrato`
