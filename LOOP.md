# Loop de ingeniería — Margen

Este documento es tu instrucción permanente. Léelo entero antes de escribir
nada y vuelve a él cada vez que cierres un paso.

Guárdalo como `LOOP.md` en la raíz del proyecto.

---

## El proyecto

Asistente financiero personal, un solo usuario, República Dominicana.

Registra los movimientos a partir de los correos de notificación del banco,
los clasifica y responde una sola pregunta en pantalla:

> **¿Cuánto puedo gastar sin afectar mis compromisos?**

Cliente en Flutter para iPhone. Servidor .NET en contenedores sobre un Linux
con Docker, Portainer, Nginx Proxy Manager y PostgreSQL ya funcionando.
Moneda DOP. Todo instante se guarda en UTC y se muestra en
`America/Santo_Domingo`.

Repositorio: `https://github.com/ElwinsZorrilla/personal-finance.git`,
rama base `main`.

---

## Punto de partida

La Fase 1 está hecha y revisada: sistema visual, primitivas de dominio
(`Money`, `BudgetPeriod`), componentes, pantallas con datos de prueba, y el
stack de Docker. Está en `app/`, `docs/` e `infra/`.

**Empiezas en la Fase 2.** No rehagas la 1; léela para tomar sus convenciones.

Lo primero que haces en tu primer turno, antes de nada:

1. Recorre `app/lib/` y `docs/` para conocer lo que ya existe.
2. Crea `docs/ESTADO.md` con la plantilla del final de este documento.
3. Abre la rama `fase/2-base-api`.
4. Escribe el brief de la Fase 2 y empieza.

---

## El ciclo

Por cada fase, en este orden. No se salta ningún paso.

```
1 BRIEF      qué se construye y cómo se comprueba
2 PLAN       qué archivos, qué se puede romper
3 BUILD      implementación
4 VERIFY     compuerta 1 — automática
5 REVIEW     compuerta 2 — revisión de código
6 COMMIT     compuerta 3 — solo si la 2 aprueba
7 SIGUIENTE  cierre y apertura de la fase que sigue
```

Si la compuerta 1 falla, vuelves al 3. Si la 2 pide cambios, vuelves al 3.
A la tercera vuelta sobre el mismo punto, te detienes: el plan está mal, no
el código.

Actualizas `docs/ESTADO.md` al terminar cada paso. Es lo que te permite
retomar en una sesión nueva sin releer el repositorio entero.

---

## Compuerta 1 — verificación

No hay revisión hasta que esto pase en verde. Corres los comandos, no un
script.

**Flutter**, desde `app/`:

```
dart format --set-exit-if-changed .
flutter analyze --fatal-infos
flutter test --coverage
```

**.NET**, desde la raíz:

```
dotnet format --verify-no-changes
dotnet build --configuration Release
dotnet test
```

**Secretos.** Antes de cada commit, `git diff --cached` y lo lees. Si aparece
una contraseña, un token, una cadena de conexión con credenciales o una URL
de producción incrustada, no hay commit.

**Cobertura.** Mínimo 80 % en `app/lib/core/`, `app/lib/domain/` y el
proyecto del motor de presupuesto. Sin mínimo en la interfaz. La cobertura se
exige donde el error es silencioso: un widget mal alineado se ve, un centavo
perdido en una división no.

---

## Compuerta 2 — revisión

Al terminar de implementar, **cambias de sombrero**. Ya no eres quien
escribió el código: eres quien lo revisa y no confía en él. Escribes el
informe en `docs/reviews/CR-<n>.md` antes de decidir nada.

### Severidades

| | Qué es | Efecto |
|---|---|---|
| **Blocker** | Pérdida o corrupción de datos, hueco de seguridad, cifra incorrecta en pantalla, caída reproducible | Detiene el commit |
| **Major** | Comportamiento incorrecto en un caso real aunque no en el camino feliz; deuda que encarece la fase siguiente | Detiene el commit |
| **Minor** | Legibilidad, nombres, duplicación tolerable | Se anota |
| **Nit** | Preferencia de estilo | Opcional |

**Cero Blockers y cero Majors abiertos ⇒ aprobada.** Cualquier otra cosa ⇒
cambios solicitados, y vuelves al paso 3.

Un Minor se acepta como deuda solo si queda escrito en `docs/DEUDA.md` con
una fase de pago asignada. Deuda sin fecha es deuda que no se paga.

### Los siete riesgos de este dominio

La revisión general no basta. En cada fase compruebas estos siete puntos
explícitamente y dices en el informe si aplican o no:

1. **Aritmética de dinero.** Ningún `double`, `float` ni `decimal` de punto
   flotante en un camino monetario. Enteros en centavos de punta a punta.
   Toda división de dinero reparte sin perder ni inventar centavos. Todo
   redondeo es explícito.
2. **Idempotencia.** Reprocesar el mismo correo dos veces no puede crear dos
   transacciones. La defensa son el `messageId` y la huella; se comprueba que
   ambas estén y que haya prueba de ello.
3. **El modelo no toca cifras.** Ningún camino de código permite que un
   modelo de lenguaje escriba `amount`, `currency`, `transactionDate` ni
   decida un duplicado de forma definitiva. Clasifica y nada más. Esto se
   verifica en el código, no confiando en el prompt.
4. **Fallo cerrado.** Si un dato crítico no se pudo extraer, el resultado es
   `NeedsReview`. Nunca un valor asumido. Un cero por defecto es una mentira
   con formato de número.
5. **Zona horaria.** UTC en la base, `America/Santo_Domingo` en pantalla. Una
   compra de las 11 de la noche no puede aparecer al día siguiente y cambiar
   de período presupuestario. Debe haber prueba.
6. **Superficie expuesta.** Ningún puerto publicado salvo por el proxy. La
   base de datos en red interna. Ningún secreto en el repositorio ni en el
   compose.
7. **Accesibilidad.** Toda cifra tiene etiqueta semántica legible en prosa.
   Contraste mínimo 4.5:1 en texto. El movimiento respeta la preferencia de
   animación reducida del sistema.

---

## Compuerta 3 — commit

Solo después de que la revisión apruebe.

```
<tipo>(<ámbito>): <qué cambia, en imperativo>

<por qué, si no es evidente>

Refs: FASE-<n>
```

Tipos: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `chore`, `build`.
Ámbitos: `app`, `api`, `worker`, `parser`, `budget`, `infra`, `design`.

En español, en imperativo, en minúscula. Describe el cambio, no el proceso de
llegar a él.

**Sin coautoría, sin firmas, sin atribución a herramientas.** Ni en los
commits, ni en el código, ni en los comentarios, ni en la documentación. El
historial registra qué cambió y por qué; nada más.

Rama por fase: `fase/<n>-<slug>`. Haces commit y push de la rama.
**El merge a `main` lo hace el humano.** No abres ni fusionas pull requests.

---

## Reglas que no se negocian

**Dinero.** Entero en centavos. Ninguna columna monetaria es `float`, `real`
ni `double precision`; son `bigint`. La conversión a texto es el último paso,
nunca un paso intermedio.

**El cálculo vive en el servidor.** El cliente presenta, no calcula. Dos
implementaciones del mismo cálculo en dos lenguajes es garantía de que se
desincronizan.

**Secretos.** Ninguno en el repositorio. Ninguna URL fija en el código: entran
por `--dart-define` en Flutter y por variables de entorno en el servidor.

**Diseño.** Antes de escribir interfaz, lees `docs/DESIGN_SYSTEM.md`. Los
colores salen de `Tone` y `Signal`, los espacios de `Space`. Un hex literal en
un widget es un hallazgo de revisión.

La regla que gobierna la paleta: **el color es un sistema de aviso, no
decoración.** Un período sano se ve casi monocromo. Si un componente introduce
color sin una razón semántica, está mal.

**Pruebas.** Un criterio de aceptación que no se puede comprobar corriendo
algo no cuenta como cumplido. «Implementado» no es un estado; «la prueba
pasa» sí.

---

## El plan

Ordenado para que el trabajo no se detenga: todo lo que **no** depende del
formato real del correo del banco va primero.

Los criterios de abajo son la definición de terminado. Si al implementar
resulta que uno es imposible o está equivocado, lo corriges en este documento
y anotas por qué. No lo ignoras en silencio.

### F1 · Sistema visual, dominio, infraestructura — CERRADA

### F2 · Base del API

- `dotnet build` limpio en la solución completa.
- Migración inicial de EF Core que crea el esquema completo y se aplica sobre
  Postgres 16 en limpio: cuentas, transacciones, categorías, períodos,
  presupuestos por categoría, reglas de comercios, pagos recurrentes, correos
  entrantes, alertas.
- `GET /health/ready` responde 200 con la base conectada, 503 sin ella.
- Toda columna de instante es `timestamptz`. Prueba que persiste y recupera un
  instante de las 23:30 hora local sin cambiar de día.
- Ninguna columna monetaria es de punto flotante.
- Autenticación por par de claves del dispositivo: registro, reto, firma.
  Prueba de integración que rechaza una firma inválida y una repetida.
- Token separado para el Atajo de iOS, con alcance solo de creación de
  efectivo.

### F3 · Motor de presupuesto

Lógica pura, sin HTTP ni base de datos. Es la parte con más riesgo de error
silencioso y la más fácil de probar. Cobertura mínima 90 %.

- Período de ingreso a ingreso, no de día 1 a 30.
- `DineroSeguro = líquido − obligaciones pendientes − reserva de tarjetas
  − ahorro comprometido − fondo de seguridad − retenciones`.
- Disponible diario. Prueba de que el último día del período no divide por
  cero.
- Base histórica: 50 % del último período, 30 % del anterior, 20 % del
  tercero.
- Proyección de cierre y desviación de ritmo.
- Redistribución que **nunca** toca prioridad 1 ni 2. Prueba de que un intento
  de recortar alquiler falla.
- Devolución que reduce el gasto de su categoría original.
- Pago de tarjeta que mueve saldo y **no** crea gasto.

### F4 · Endpoints y contrato

- Transacciones, registro rápido, presupuesto, revisión, reglas, correos
  entrantes, conciliación, notificaciones.
- OpenAPI generado y versionado en `docs/api/openapi.json`.
- Errores en formato ProblemDetails. Ningún 500 con traza al cliente.
- Límite de peticiones en autenticación y en el registro rápido.
- Pruebas de integración sobre Postgres real en contenedor, no en memoria.

### F5 · Capa de datos en Flutter

- Cliente HTTP con reintento y expiración.
- Caché local que permite abrir la app sin señal y mostrar el último panel
  conocido, con la fecha de esa lectura visible en pantalla.
- El repositorio de prueba deja de usarse en release.
- El panel consume datos reales. Ninguna cifra calculada en el cliente.

### F6 · Ingesta de correo, sin parser de banco

Todo el andamiaje, probado con muestras sintéticas que tú mismo generas.

- Cliente IMAP contra el buzón, con lista blanca de remitentes.
- Registro de correos entrantes con identificador de mensaje, hash del cuerpo
  y estado.
- Interfaz de parser y registro de parsers disponibles.
- Huella y detección de duplicados: coincidencia exacta y aproximada.
- **Reprocesar el mismo correo dos veces no crea dos transacciones.** Prueba
  explícita.
- Un correo que no reconoce ningún parser va a `NeedsReview` y no crea nada.
- Herramienta de reproceso contra una versión nueva del parser, sin duplicar.
- Pantalla de Revisión conectada.

### F7 · Parser del banco — BLOQUEADA

Necesita correos reales anonimizados en `docs/muestras/`. Como mínimo: compra
aprobada, compra rechazada, devolución, retiro, pago de tarjeta,
transferencia.

Escribir esto contra un formato supuesto es trabajo que se tira entero. Al
llegar aquí, te detienes y los pides.

### F8 · Clasificación y anomalías

- Cascada: regla exacta del usuario → regla por patrón → historial del
  comercio → clasificador local → modelo → revisión humana.
- Una corrección del usuario genera una regla y evita la siguiente llamada al
  modelo.
- Rango normal por comercio; detección de gasto inusual, de suscripción que
  sube de precio y de factura recurrente que no llegó.

### F9 · Efectivo desde el iPhone

- Endpoint de registro rápido que interpreta «Gasté 450 pesos en almuerzo».
- Token restringido, revocable, con límite de peticiones.
- Atajo documentado en `docs/atajo.md`.

### F10 · Conciliación

- Importación CSV con mapeo de columnas.
- Estados: conciliado, ausente, pendiente, discrepante, duplicado, ignorado.
- Cierre de período que genera el presupuesto recomendado del siguiente.

### F11 · Empaquetado iOS — depende de una decisión

Con Apple ID gratuito no hay notificaciones push y hay que refirmar cada
7 días. Los avisos son un requisito del producto. Antes de esta fase el humano
tiene que decidir entre pagar el programa de Apple o volver a una PWA. Está en
`docs/adr/ADR-001-distribucion-ios.md`.

### F12 · Endurecimiento y despliegue

- Stack desplegado y accesible solo por el proxy.
- Respaldo diario con restauración probada en una base descartable.
- Rotación de secretos documentada.

---

## Encadenado

Al cerrar una fase, abres la siguiente **sin preguntar**:

1. Anotas el cierre en `docs/BITACORA.md`.
2. Tomas la primera fase pendiente de este documento.
3. Si está marcada BLOQUEADA, actualizas `docs/ESTADO.md` con el motivo y te
   detienes. Ese es un final legítimo del turno.
4. Si no, actualizas `docs/ESTADO.md`, creas la rama y empiezas por el brief.

---

## Cuándo te detienes

Detenerte aquí no es fallar el loop; es el loop funcionando.

- **Una fase marcada BLOQUEADA.**
- **Tres vueltas sobre el mismo punto.** El plan está mal, no el código.
- **Falta un dato del mundo real** que no puedes inventar: el HTML de un
  correo del banco, el formato de un CSV, el nombre de la red del proxy.
- **Una decisión abierta**, como la de la Fase 11.
- **Algo que toque dinero real, credenciales o el servidor en producción.**
- **Un `git push --force`, un `git reset --hard` o un borrado recursivo.**

Cuando te detengas, dices exactamente qué necesitas del humano. Una frase,
concreta, con el archivo o el dato que falta.

## Cuándo no te detienes

Mientras haya un criterio de aceptación sin cumplir en la fase abierta y
ninguna de las condiciones de arriba se cumpla, sigues trabajando.

Antes de terminar cualquier turno, te haces estas tres preguntas y las
respondes en voz alta:

1. ¿Están todos los criterios de la fase cumplidos y comprobados corriendo
   algo?
2. ¿Pasó la compuerta 1 y la revisión aprobó?
3. Si la respuesta a alguna es no, ¿por qué estoy terminando el turno?

Si la tercera no tiene una respuesta que aparezca en la lista de arriba,
continúa.

---

## Plantilla de `docs/ESTADO.md`

Lo creas en tu primer turno y lo mantienes al día. En una sesión nueva, lo
primero que lees.

```markdown
# Estado

Fase: 2 — Base del API
Rama: fase/2-base-api
Estado: en curso
Paso: build
Vuelta: 0 de 3
Compuerta 1: sin ejecutar
Revisión: —
Veredicto: —
Bloqueo: —

## Criterios de esta fase

- [ ] dotnet build limpio
- [ ] migración inicial aplicada sobre Postgres en limpio
- [ ] /health/ready responde 200 y 503
- [ ] columnas de instante en timestamptz, con prueba de las 23:30
- [ ] ninguna columna monetaria en punto flotante
- [ ] autenticación por clave de dispositivo, con prueba de firma inválida
- [ ] token del Atajo con alcance restringido

## Siguiente acción

<una frase>
```
