# Estado

Fase: 10 - Conciliación
Rama: fase/10-conciliacion
Estado: **cerrada**
Paso: 7 de 7 (NEXT)
Vuelta: 1 de 3
Compuerta 1: verde en las dos
Revisión: CR-011 aprobada
Veredicto: **APROBADA**
Bloqueo: -

## Varios bancos

El buzón tiene **más de un banco**. El registro de parsers, la lista blanca de
remitentes y el redactor ya eran multibanco desde la Fase 6; la **captura de
muestras** no, y se arregló en esta fase:

- La cuota de muestras es por **banco y tipo**. Era global, así que los seis
  primeros consumos del primer banco la agotaban y de los demás no se capturaba
  ninguno.
- Las muestras llevan el banco en el nombre: `popularenlinea-compra-aprobada.txt`.
  Sin eso, con tres bancos ninguna muestra sirve para escribir ningún parser.

**Para añadir un banco**: poner su remitente en `IMAP_ALLOWED_SENDERS`, correr
la captura, leer las muestras y escribir su `IEmailParser`. El registro elige
por remitente, así que el banco nuevo no toca a los que ya funcionan.

| Banco | Parser | Muestras |
|---|---|---|
| Banco Popular | Escrito y aprobado (CR-008) | 16, faltan 3 tipos |
| Los demás | **Pendiente** | **Pendientes de capturar** |

## La conciliación

Un **perfil por banco** dice qué columna del CSV es cuál. Se escribe una vez, se
ve una vista previa y a partir de ahí importar el estado de cuenta del mes es
subir un archivo.

El mapeo de columnas es la respuesta a no conocer el formato de antemano, no un
sustituto de conocerlo. Lo explícito es explícito a propósito:

| Qué | Por qué no se adivina |
|---|---|
| Formato de fecha | `01/02/2026` es el 1 de febrero o el 2 de enero según el banco |
| Estilo decimal | `1.234,56` leído mal da 1,23 |
| Signo | Hay bancos que escriben los cargos en positivo |

Se detecta solo el separador —por consistencia de columnas, no por frecuencia—
y la codificación —UTF-8 o Latin-1—.

| Estado | Qué se hace al importar |
|---|---|
| Conciliado | El movimiento pasa a `Reconciled` |
| **Ausente** | **Se crea**, en revisión y sin categoría |
| Pendiente | Nada; sale en el informe |
| Discrepante | Nada: hay dos cifras y elegirlas sin preguntar no se hace |
| Duplicado | Nada: dos candidatos iguales no se desempatan al azar |

Lo ausente es el motivo entero de conciliar: el efectivo que nadie registró, los
correos que no llegaron y **los bancos que todavía no tienen parser** aparecen
ahí.

## El cierre de período

`POST /periods/{id}/close` cierra y devuelve qué asignar en el siguiente, sacado
de **lo que se gastó de verdad**: un presupuesto que se copia a sí mismo mes tras
mes repite el error del primer mes para siempre.

Si no cabe en el ingreso se recorta por prioridad, y lo Esencial y lo Importante
no se tocan. Si aun así no cabe, **se dice**: no es un fallo del cálculo, es que
los compromisos no caben en el sueldo.

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

**771 pruebas**: 680 en .NET, 91 en Flutter. Las dos compuertas 1 en verde.
Cobertura de `Margen.Classify`: 99,8 % de líneas, 98,3 % de ramas.

## Lo que necesito del humano

1. **ADR-001 sin responder**: pagar el programa de Apple o volver a PWA. Define
   el empaquetado de la Fase 11 y condiciona los avisos.
2. **Confirmar las fuentes** descargadas en la Fase 5: el README dice de dónde
   bajarlas, no qué versión.
4. **Relanzar la revisión adversarial del motor** (m15) antes de fusionar la
   Fase 3: CR-003 la respalda un solo lector.
4. **El merge a `main`.** Diez ramas publicadas, ninguna fusionada.
5. **Capturar los otros bancos.** Pon sus remitentes en `IMAP_ALLOWED_SENDERS`
   —separados por comas— y corre `--capturar-muestras`. Sin sus muestras no se
   les puede escribir parser, y sus movimientos solo entrarán por la
   conciliación del estado de cuenta.
6. **Guardar los avisos que faltan del Popular** si te llegan: compra
   rechazada, devolución y pago de tarjeta.

## Lo que sigue

**Fase 12 - endurecimiento y despliegue.** Es la última que no depende de nadie.

**Fase 11 - empaquetado iOS.** Depende de ADR-001, sin responder.

Y en paralelo, cuando haya muestras: **un parser por cada banco nuevo**. No es
una fase; es trabajo que se hace cada vez que aparece un banco, y el andamiaje
ya está.

## Notas de entorno

- Remoto `origin` en `github.com/ElwinsZorrilla/personal-finance`. Ramas:
  `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`, `fase/4-endpoints`,
  `fase/5-datos-flutter`, `fase/6-ingesta-correo`, `fase/7-parser-banco`,
  `fase/8-clasificacion`, `fase/9-efectivo-iphone`, `fase/10-conciliacion`.
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
