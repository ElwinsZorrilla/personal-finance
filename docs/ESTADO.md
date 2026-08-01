# Estado

Fase: 7 — Parser del banco
Rama: fase/6-ingesta-correo (la 7 no se abre hasta desbloquearla)
Estado: **BLOQUEADA**
Paso: —
Vuelta: 0 de 3
Compuerta 1: verde en las dos, sobre la Fase 6
Revisión: CR-007 aprobada
Veredicto: —
Bloqueo: **faltan las muestras — la herramienta para sacarlas ya está lista**

## Por qué está bloqueada

El `LOOP.md` lo dice y la Fase 6 lo confirmó: escribir un parser contra un
formato supuesto es trabajo que se tira entero. Cada banco pone los campos donde
quiere, con las etiquetas que quiere.

**Todo lo demás está hecho.** El andamiaje entero —bajar del buzón, filtrar por
remitente, guardar el original, elegir parser, detectar duplicados por cuatro
caminos, crear el movimiento, reprocesar contra una versión nueva— está escrito
y probado contra muestras sintéticas. Lo único que falta es una clase que
implemente `IEmailParser` para el formato real.

## Cómo desbloquearla

Hay una herramienta que baja las muestras de Gmail, les quita lo personal y las
deja en `docs/muestras/`. Los pasos —incluida la contraseña de aplicación de
Google— están en [`docs/gmail.md`](gmail.md).

```bash
export IMAP_HOST=imap.gmail.com
export IMAP_USER=tucuenta@gmail.com
export IMAP_PASSWORD=lacontrasenadeaplicacion
export IMAP_ALLOWED_SENDERS=alertas@tubanco.com
export Muestras__DatosPersonales="Nombre Apellido"

dotnet run --project src/Margen.Worker -- --capturar-muestras
```

Abre el buzón en **solo lectura** y no escribe nada en la base. Los archivos
que genera están en `.gitignore` a propósito: **hay que leerlos antes de
versionarlos**, porque el redactor quita lo que sabe reconocer y el banco puede
poner algo que no previó.

El formato que debe conservar cada muestra está en
[`docs/muestras/README.md`](muestras/README.md).

## Lo hecho

| Fase | Estado | Revisión | Pruebas |
|---|---|---|---|
| 1 · Sistema visual, dominio, infraestructura | Cerrada | CR-001, CR-005 | 86 en `app/` |
| 2 · Base del API, esquema, autenticación | Cerrada | CR-002 | incluidas en las 105 del API |
| 3 · Motor de presupuesto | Cerrada | CR-003 | 101, cobertura 100 % |
| 4 · Endpoints y contrato | Cerrada | CR-004 | 105 de integración |
| 5 · Capa de datos en Flutter | Cerrada | CR-006 | incluidas en las 86 |
| 6 · Ingesta de correo | Cerrada | CR-007 | 48 puras + integración |

**383 pruebas**: 297 en .NET, 86 en Flutter. Las dos compuertas 1 en verde.

## Lo que necesito del humano

1. **Correr la captura** con tu contraseña de aplicación de Gmail y **leer los
   archivos** que genere. Yo no tengo ni debo tener esa contraseña.
2. **ADR-001 sin responder**: pagar el programa de Apple o volver a PWA. Define
   el empaquetado de la Fase 11 y condiciona los avisos.
3. **Confirmar las fuentes** descargadas en la Fase 5: el README dice de dónde
   bajarlas, no qué versión.
4. **Relanzar la revisión adversarial del motor** (m15) antes de fusionar la
   Fase 3: CR-003 la respalda un solo lector.
5. **El merge a `main`.** Seis ramas publicadas, ninguna fusionada.

## Si se quiere seguir sin las muestras

Las fases que **no** dependen del parser real y se podrían abordar:

- **Fase 8** — clasificación y anomalías. La cascada (regla exacta → patrón →
  historial → clasificador → modelo → revisión) se puede construir y probar con
  los movimientos que ya crea la ingesta sintética.
- **Fase 9** — efectivo desde el iPhone. El endpoint existe desde la Fase 4;
  falta interpretar «Gasté 450 pesos en almuerzo» y documentar el Atajo.
- **Fase 10** — conciliación. La importación de CSV necesita saber el formato
  del CSV del banco, así que está a medio bloquear.
- **Fase 12** — endurecimiento y despliegue.

La 11 depende de ADR-001.

## Notas de entorno

- Remoto `origin` en `github.com/ElwinsZorrilla/personal-finance`. Ramas:
  `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`, `fase/4-endpoints`,
  `fase/5-datos-flutter`, `fase/6-ingesta-correo`.
- .NET 10.0.204, Docker 29.5.3 y Flutter 3.44.5. Flutter está en
  `C:\src\flutter` pero **no en el PATH**: hay que añadirlo a mano.
- Regenerar el contrato tras cambiar la superficie del API:
  `dotnet run --project src/Margen.Api -- --generar-contrato`
- Reprocesar correos contra una versión nueva del parser:
  `dotnet run --project src/Margen.Worker -- --reprocesar`
