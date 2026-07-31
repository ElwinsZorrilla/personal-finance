# Margen

Asistente financiero personal. Registra los movimientos a partir de los
correos de notificación del banco, los clasifica y responde una sola pregunta:

> **¿Cuánto puedo gastar sin afectar mis compromisos?**

## Cómo se trabaja

Todo el proceso está en [`LOOP.md`](LOOP.md): el ciclo, las tres compuertas,
la rúbrica de revisión y las doce fases con sus criterios de aceptación.
Ninguna fase se cierra sin pasar la verificación y la revisión.

El punto exacto donde va el trabajo se mantiene en `docs/ESTADO.md`.

## Estructura

```text
LOOP.md       El proceso. Se lee entero antes de escribir nada.
app/          Flutter. Cliente iOS.
  lib/core/     Dinero y períodos. Aritmética entera, sin punto flotante.
  lib/domain/   Modelos. Sin dependencias de Flutter.
  lib/design/   Tokens, tipografía, componentes.
  lib/features/ Pantallas.
docs/         Sistema visual, decisiones, revisiones, deuda.
infra/        Stack de Docker para el servidor.
```

## Desarrollo

```bash
cd app
flutter pub get
flutter run --dart-define=USE_MOCKS=true
```

Contra el servidor real:

```bash
flutter run \
  --dart-define=USE_MOCKS=false \
  --dart-define=API_BASE_URL=https://finanzas.tudominio.com
```

Ninguna URL ni credencial vive en el código fuente.

## Antes de cada commit

Desde `app/`:

```bash
dart format --set-exit-if-changed .
flutter analyze --fatal-infos
flutter test --coverage
```

Y se lee `git diff --cached` buscando secretos.

## Fases

| | | |
|---|---|---|
| 1 | Sistema visual, dominio, infraestructura | Cerrada (CR-001) |
| 2 | Base del API, esquema, autenticación | Siguiente |
| 3 | Motor de presupuesto | |
| 4 | Endpoints y contrato | |
| 5 | Capa de datos en Flutter | |
| 6 | Ingesta de correo, sin parser de banco | |
| 7 | Parser del banco | Falta una muestra real |
| 8 | Clasificación y anomalías | |
| 9 | Efectivo desde el iPhone | |
| 10 | Conciliación | |
| 11 | Empaquetado iOS | Depende de ADR-001 |
| 12 | Endurecimiento y despliegue | |
