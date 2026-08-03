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
src/          .NET. Servidor.
  Margen.Domain/          Entidades y dinero. Sin una sola referencia.
  Margen.Infrastructure/  EF Core, esquema y migraciones.
  Margen.Budget/          Motor de presupuesto. Lógica pura, sin HTTP ni base.
  Margen.Classify/        Cascada de clasificación y anomalías. Lógica pura.
  Margen.Ingest/          Parsers, huellas y duplicados. Lógica pura.
  Margen.Api/             ASP.NET Core. Salud y autenticación.
  Margen.Worker/          Lector del buzón y captura de muestras.
tests/        Pruebas del servidor. Las de integración usan Postgres real.
docs/         Sistema visual, decisiones, revisiones, deuda, briefs, planes.
  atajo.md      Cómo montar el Atajo de iOS. Sin secretos dentro.
  despliegue.md Runbook del despliegue. Lo ejecuta una persona.
  secretos.md   Qué abre cada secreto y cómo se rota.
  api/          Contrato OpenAPI versionado. Se regenera, no se edita.
infra/        Stack de Docker para el servidor.
```

## Desarrollo

Flutter está en `C:\src\flutter` y **no en el PATH**; hay que añadirlo.

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

## El servidor

```bash
dotnet build
dotnet test
```

Las pruebas de integración levantan un PostgreSQL 16 en contenedor con
Testcontainers: hace falta Docker corriendo. No hay base en memoria a propósito
—no tiene `timestamptz` ni índices únicos, que es justo lo que se comprueba—.

Para generar una migración:

```bash
dotnet ef migrations add <Nombre> \
  --project src/Margen.Infrastructure \
  --startup-project src/Margen.Infrastructure \
  --output-dir Migrations
```

Ninguna URL ni credencial vive en el código fuente. El servidor las lee de
variables de entorno; ver `infra/.env.example`.

## Antes de cada commit

Desde `app/`:

```bash
dart format --set-exit-if-changed .
flutter analyze --fatal-infos
flutter test --coverage
```

Desde la raíz:

```bash
dotnet format --verify-no-changes
dotnet build --configuration Release
dotnet test
```

Y se lee `git diff --cached` buscando secretos.

## Fases

| | | |
|---|---|---|
| 1 | Sistema visual, dominio, infraestructura | Cerrada (CR-001) |
| 2 | Base del API, esquema, autenticación | Cerrada (CR-002) |
| 3 | Motor de presupuesto | Cerrada (CR-003) |
| 4 | Endpoints y contrato | Cerrada (CR-004) |
| 5 | Capa de datos en Flutter | Cerrada (CR-006) |
| 6 | Ingesta de correo, sin parser de banco | Cerrada (CR-007) |
| 7 | Parser del banco | Cerrada (CR-008). Popular, Qik (CR-012) y Banreservas (CR-013) |
| 8 | Clasificación y anomalías | Cerrada (CR-009) |
| 9 | Efectivo desde el iPhone | Cerrada (CR-010) |
| 10 | Conciliación | Cerrada (CR-011) |
| 11 | Empaquetado iOS | Depende de ADR-001 |
| 12 | Endurecimiento y despliegue | Cerrada (CR-014). Desplegar lo hace el humano |
