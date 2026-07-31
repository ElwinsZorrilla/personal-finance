# Estado

Fase: 5 — Capa de datos en Flutter
Rama: fase/5-datos-flutter
Estado: en curso
Paso: build
Vuelta: 0 de 3
Compuerta 1: **verde en los dos lados** — .NET 234 pruebas, Flutter 51
Revisión: CR-005 sobre el paso cero
Veredicto: aprobada la puesta a punto; los criterios de la fase, sin empezar
Bloqueo: —

## Criterios de esta fase

- [x] **paso cero:** la compuerta 1 de Flutter en verde (CR-005)
- [ ] cliente HTTP con reintento y expiración
- [ ] caché local que abre la app sin señal con el último panel conocido, con la fecha de esa lectura visible
- [ ] el repositorio de prueba deja de usarse en release
- [ ] el panel consume datos reales; ninguna cifra calculada en el cliente
- [ ] pago de la deuda m7, m19 y m20

## El paso cero, ya hecho

`flutter` no estaba en el PATH pero sí instalado en `C:\src\flutter`
(3.44.5, Dart 3.12.2). Al correr la compuerta por primera vez desde la Fase 1,
el árbol **no compilaba**. CR-005 tiene el detalle: tres Blockers y un Major.

Para trabajar en `app/`:

```bash
export PATH="/c/src/flutter/bin:$PATH"
cd app && flutter pub get
dart format --set-exit-if-changed .
flutter analyze --fatal-infos
flutter test --coverage
```

## Lo hecho

| Fase | Estado | Revisión | Pruebas |
|---|---|---|---|
| 1 · Sistema visual, dominio, infraestructura | Cerrada | CR-001 | compuerta 1 de Flutter sin correr |
| 2 · Base del API, esquema, autenticación | Cerrada | CR-002 | 41 de integración |
| 3 · Motor de presupuesto | Cerrada | CR-003 | 101, cobertura 100 % |
| 4 · Endpoints y contrato | Cerrada | CR-004 | 90 de integración |
| 5 · Capa de datos en Flutter | Paso cero hecho | CR-005 | 51 en `app/` |

Total: **285 pruebas** — 234 en .NET, 51 en Flutter. Las dos compuertas 1 en verde.

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
- El merge a `main` lo hace el humano. Las cinco ramas están publicadas.
- **Confirmar las fuentes**: se descargaron de los orígenes que documenta
  `app/assets/fonts/README.md`, pero ese README dice de dónde bajarlas, no qué
  versión.

## Notas de entorno

- Remoto `origin` en `github.com/ElwinsZorrilla/personal-finance`. Ramas:
  `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`, `fase/4-endpoints`,
  `fase/5-datos-flutter`.
- .NET 10.0.204, Docker 29.5.3 y Flutter 3.44.5. Flutter está instalado en
  `C:\src\flutter` pero **no en el PATH**: hay que añadirlo a mano.
- Regenerar el contrato tras cambiar la superficie del API:
  `dotnet run --project src/Margen.Api -- --generar-contrato`
