# Deuda técnica

Deuda sin fase de pago asignada es deuda que no se paga. Nada entra aquí sin
una columna «Se paga en».

| # | Origen | Qué falta | Por qué se pospuso | Se paga en |
|---|---|---|---|---|
| ~~m6~~ | CR-001 | La compuerta 1 no incluye compilación release en cada vuelta | — | **Resuelta en la Fase 12** (CR-014): en .NET se compila Release en cada vuelta desde la Fase 2, y así ha sido en las trece revisiones. En Flutter no aplica igual: el release de una app iOS necesita firma, que depende de ADR-001 |
| m7 | CR-001 | `_RiskNote` usa `projectedDepletion!` protegido por el llamador | Hoy hay un solo llamador y es correcto | Fase 5, al conectar la pantalla de Presupuesto a datos reales. La Fase 4 no tocó `app/` |
| ~~m8~~ | CR-001 | `infra/backup.sh` usa `sleep 86400` y deriva | — | **Pagada en la Fase 12** (CR-014): duerme lo que falta para completar el intervalo, contado desde que empezó. En la misma vuelta apareció algo peor: el sello se calculaba fuera del bucle y **había un solo respaldo que se sobrescribía cada día** |
| ~~—~~ | Fase 1 | `MockRepository` sigue en el árbol de release aunque `Env.useMocks` sea `false` | Faltaba confirmarlo midiendo el binario | **Pagada en la Fase 5** (CR-006): medido sobre `build/web/main.dart.js`, con cadenas de control |
| ~~m9~~ | CR-002 | Dos altas simultáneas del mismo teléfono chocan contra el índice único y salen como 500 en lugar de devolver el dispositivo existente | Hay un solo usuario y un solo teléfono; la carrera necesita dos peticiones en el mismo milisegundo | **Pagada en la Fase 4** (CR-004) |
| ~~m10~~ | CR-002 | El alta de dispositivo y el canje de reto no tienen límite de peticiones | El plan lo pone en la Fase 4, junto con los endpoints que protege | **Pagada en la Fase 4** (CR-004) |
| ~~m11~~ | CR-002 | `POST /transactions/cash` responde 501; existe solo para poder comprobar el alcance del token del Atajo | Sin una ruta que ese token sí alcance, «alcance restringido» se cumpliría por accidente | **Pagada en la Fase 4** (CR-004); la interpretación de texto sigue en la Fase 9 |
| ~~m12~~ | CR-002 | `Guid.Parse(user.FindFirstValue(...)!)` en cuatro endpoints. Una reclamación ausente sería un 500 | Las escribe el mismo handler que autentica; hoy no hay forma de que falten | **Pagada en la Fase 4** (CR-004) |
| m13 | CR-002 | `IncomingEmails.BodyHash` es único en toda la tabla: dos correos legítimos con el cuerpo idéntico se rechazarían como duplicado | No se puede decidir sin ver un correo real del banco: depende de si el cuerpo trae referencia u hora con segundos | Fase 6, con las muestras de la Fase 7 |
| m14 | CR-003 | `SpendingLedger.ResolveCategory` sigue una sola referencia: la devolución de una devolución se imputa a la categoría intermedia | **Vencía en la Fase 10 y no se pagó.** Sigue sin haber una sola devolución real: el Popular no mandó ninguna en 209 correos y la conciliación por CSV no distingue una devolución de un abono. Escribir el recorrido de la cadena sin haber visto el eslabón simple es el error que bloqueó la Fase 7 | En cuanto aparezca una devolución en el buzón o en un estado de cuenta |
| m15 | CR-003 | La revisión adversarial multi-agente del motor no llegó a ejecutarse: los seis agentes se toparon con el límite de sesión. CR-003 la respalda un solo lector | Sin cuota. El guion queda escrito y se puede relanzar tal cual | No es deuda de código: relanzar cuando haya cuota, antes de fusionar la Fase 3 a `main` |
| m16 | CR-004 | El panel dispara cinco consultas extra por la base histórica | **Vencía en la Fase 12 y no se pagó.** Su condición era «con medición real», y no hay medición real porque no hay producción: medir sobre datos sembrados mediría los datos sembrados. No es una excusa, es orden de dependencias | En cuanto haya un mes de movimientos de verdad en el servidor |
| m17 | CR-004 | La redistribución persiste `Adjustment ±= monto` en vez de los valores que devuelve el motor. Equivalentes hoy | **Vencía en la Fase 10 y no se pagó.** El cierre de período no lo forzó: recomienda desde el gasto real y no lee `Adjustment`. Unificarlo sigue pidiendo rediseñar el registro de cambios, que es lo que da el rastro de qué se movió | Fase 12, junto con m28: las dos necesitan la misma pieza que falta |
| m18 | CR-004 | `Page<T>.NextCursor` siempre es nulo: la paginación real no existe | Sigue sin llegarse al tope de 100 por página con un año de movimientos personales. Se cambia la fase por un disparador: una fase se pospone sola, un disparador no | Cuando una consulta devuelva 100 filas |
| m19 | CR-005 | El contraste 4.5:1 y el respeto a la animación reducida no se han comprobado en ningún widget | Sigue abierta y ahora es **más comprobable**: al ser PWA (CR-015), un navegador tiene herramientas para medir contraste que un simulador de iOS no tiene | Al desplegar, con la app abierta en el navegador |
| ~~m20~~ | CR-005 | `intl` sigue como dependencia aunque `core/` ya no la use | — | **Pagada en la Fase 5** (CR-006): fuera de `pubspec.yaml` |
| ~~m21~~ | CR-006 | La caché no caduca: una lectura de hace un mes se enseña igual que una de hace un minuto | — | **Pagada en la Fase 12** (CR-014): vale 72 horas. El plazo es largo porque la caché solo aparece cuando el servidor no responde, y uno corto la volvería inútil el fin de semana que se cae |
| ~~m22~~ | CR-006 | **No hay recorrido de alta desde la app.** `ApiClient.token` es un setter que nadie llama, así que la PWA arranca, pide el panel sin token y muestra «hay que volver a entrar» | **Vencía en la Fase 11 y no se pagó**: esa fase hizo el empaquetado y no esto. Es lo **único** que impide usar la app desplegada. Con ADR-001 resuelto a PWA, el Enclave Seguro deja de aplicar y el equivalente es WebCrypto | **Pagada** (CR-016): ECDSA P-256 con WebCrypto, no extraíble, en IndexedDB. Pantalla de alta, recuperación del token al arrancar y renovación sin volver a pedir el código. 11 pruebas |
| ~~m23~~ | CR-007 | `SampleBankParser` es un parser de un banco que no existe y queda en el árbol | — | **Resuelta en la Fase 7** (CR-008): se queda, y ahora cumple lo que decía su justificación —es la prueba de que el registro elige entre dos parsers por remitente, que con uno solo no se podía comprobar |
| m24 | CR-007 | El reproceso se invoca por línea de comandos en el worker, sin endpoint | El reprocesador vive en `Margen.Worker` y el API no lo referencia; exponerlo obliga a mover la pieza o duplicarla, y cuesta más que la comodidad que da. Lo usa quien despliega | Cuando haga falta reprocesar sin acceso al servidor |
| m25 | CR-008 | Faltan tres avisos del Popular que no aparecieron en 209 correos: compra rechazada, devolución y pago de tarjeta. El parser los contempla, pero contra un formato supuesto | Es el mismo error que bloqueó la fase entera: escribir contra un formato que nadie ha visto. Inventarlos daría una prueba que pasa y un parser que falla | Cuando lleguen al buzón: capturar, corregir y `--reprocesar` |
| ~~m26~~ | CR-008 | El comercio del retiro en sucursal queda como `BANCO POPULAR OF.CHAR DE`, truncado por el propio banco | — | **Resuelta en la Fase 8** (CR-009): la clasificación **no** necesita distinguir sucursales. La tabla local casa por «BANCO POPULAR» y las manda a Efectivo, que es lo correcto para las tres formas en que el banco escribe lo mismo |
| m27 | CR-009 | La detección de gasto inusual hace una consulta de historial por movimiento reciente | **Vencía en la Fase 12 y no se pagó**, por lo mismo que m16: sin producción no hay nada que medir | En cuanto haya un mes de movimientos de verdad |
| m28 | CR-009 | El cargo de un recurrente se busca por categoría y ventana de fechas, no por el compromiso | **Vencía en la Fase 10 y no se pagó.** Falta un enlace movimiento-compromiso, que es exactamente la misma pieza que le falta a m17. Inventarlo a medias en dos sitios distintos es peor que no tenerlo | Fase 12, junto con m17 |
| m29 | CR-010 | Un reintento del Atajo responde 409 en vez de 200 con el movimiento que ya existe | En los dos casos la acción correcta es no hacer nada, y el mensaje dice qué hacer si de verdad fueron dos gastos | Si el uso real enseña que confunde |
| m30 | CR-011 | El emparejamiento de la conciliación es O(líneas × movimientos) | **Vencía en la Fase 12 y no se pagó**, por lo mismo que m16 y m27 | Con el primer estado de cuenta real importado |
| m31 | CR-011 | El estado «ignorado» de la conciliación se calcula pero nadie lo puede fijar | Fijarlo necesita una pantalla donde una persona marque la línea, y esa pantalla no existe | Fase 11, con el cliente iOS |
| m32 | CR-012 | De Qik solo hay avisos de tarjeta: faltan depósito, transferencia y pago | No aparecieron en 75 correos. El parser no los inventa —lo que no reconoce va a Revisión sin crear nada—, y escribirlos contra un formato supuesto es el error que bloqueó la Fase 7 | Cuando lleguen al buzón: capturar, ampliar el parser y `--reprocesar` |
| m33 | CR-013 | De Banreservas faltan depósito, transferencia y **una transacción declinada**: no se sabe qué palabra usa para rechazar | El parser exige que el estado diga aprobado, así que una declinada va a Revisión sin crear nada. Es el comportamiento correcto hasta ver una de verdad; inventarse la palabra sería registrar gastos que no ocurrieron | Cuando lleguen al buzón: capturar, ampliar el parser y `--reprocesar` |


| m34 | CR-014 | El despliegue no se ha ejecutado: el compose es válido y las imágenes se construyen, pero nadie ha levantado el stack contra un servidor de verdad | Toca producción y credenciales, que es condición de parada del `LOOP.md`. El runbook está escrito paso a paso en `docs/despliegue.md` | **Lo hace el humano.** Nada de lo que sigue —m16, m27, m30— se puede pagar antes |
| m35 | CR-015 | La PWA no se ha abierto en un navegador de verdad: compila, pasa las pruebas y nadie la ha visto arrancar | Es la tercera vez que Flutter compila algo que no arranca —las dos anteriores, en la Fase 5—. Aquí no hay forma de comprobarlo sin un navegador delante | **Al desplegar.** Es el primer paso después de levantar el stack |
| m36 | CR-015 | Los avisos push no están implementados: la PWA los soporta desde iOS 16.4 y hace falta service worker propio, permiso y suscripción | El requisito §20 era «avisar solo cuando algo requiere atención», y lo que decide qué es digno de aviso son las alertas de la Fase 8, que ya existen. Falta el transporte, no el criterio | Después del despliegue, con la app instalada en la pantalla de inicio |
| ~~m39~~ | CR-021 | El período abierto no se puede editar: los días de cobro y el ingreso se fijan al abrirlo y `/setup/periods` es idempotente, así que devuelve el que hay en vez de corregirlo | Se vio al desplegar: el período de producción se había abierto antes con un solo cobro el 15, y no hay forma de decirle que son dos sin esperar a que termine. Hace falta un `PUT` sobre el período abierto y una pantalla de ajustes | **Pagada** (CR-022): `PUT /setup/periods/current` recalcula las fechas desde el calendario nuevo, y una pestaña de Ajustes que enseña el período actual antes de tocarlo |
| m40 | CR-021 | Cuatro dispositivos de prueba (`prueba-de-despliegue`, `prueba-de-ciclo`, `prueba-de-cuentas` y otro)  quedaron dados de alta al verificar contra producción | Sus claves privadas solo existieron en memoria de procesos ya terminados, así que no pueden volver a autenticarse nunca. Sus tokens viven 30 días | Cuando el humano borre las filas: es un `DELETE` contra su base de producción |
| m38 | CR-018 | El motor gráfico, las fuentes y los iconos no llevan versión en la URL: los pide el motor de Flutter en tiempo de ejecución y no salen del `index.html`, así que siguen con `no-cache` y Cloudflare lo convierte en cuatro horas | Solo cambian al subir de versión el SDK de Flutter, y el desfase se corrige solo en cuatro horas en vez de quedarse clavado. Versionarlos pide mover el motor a un directorio con la versión en el nombre y pasarle `canvasKitBaseUrl` al cargador | Al subir de versión el SDK de Flutter, que es lo único que lo dispara |
| m37 | CR-017 | Ninguna prueba automática ejecuta WebCrypto de verdad: la firma del navegador se comprueba en C# imitando su formato, no en un navegador | Es la costura donde estaban los dos defectos de CR-017, y sigue cubierta por imitación en vez de por ejecución. Hace falta un navegador en el arnés —`flutter drive` o Playwright contra el paquete construido—, que es infraestructura nueva | Cuando cambie algo del alta: hoy la sonda manual de CR-017 la cubre una vez |
---

## m22 — el alta desde la PWA · **hecho** (CR-016)

Se dejan las decisiones escritas porque explican el código que hay.

**La clave.** ECDSA P-256 con WebCrypto, generada con `extractable: false`: ni
el propio código puede leer la privada. Es lo más cercano al Enclave Seguro que
ofrece un navegador, y la diferencia con `true` es la que hay entre «no la
exporto» y «no se puede». P-256 y no otra curva porque es la que el servidor
valida desde la Fase 2.

**Dónde vive.** IndexedDB, guardando el `CryptoKey` opaco y no su material. No
`localStorage`, que solo admite texto y obligaría a exportar la clave —es decir,
a hacerla extraíble—.

**El recorrido**, que ya existe entero en el servidor:

1. `POST /auth/devices` con el nombre y la pública en SPKI base64, y el código
   de alta en la cabecera `X-Margen-Enrollment` → devuelve el `deviceId`.
2. `POST /auth/challenges` con el `deviceId` → devuelve un `nonce` en base64.
3. Firmar **los bytes** del nonce —`base64.decode`, no su texto— con SHA-256.
   El servidor verifica contra los bytes que generó; firmar la representación en
   texto da una firma que no valida y un error que no dice por qué.
4. `POST /auth/tokens` con `deviceId`, `nonce` y firma → devuelve el token.

**Qué se guarda.** El token en el almacén local, y se vuelve a pedir con la
misma clave cuando caduque: el paso 1 solo se hace una vez, los pasos 2 a 4 cada
vez que haga falta. Por eso un 401 no debe mandar al usuario a pedir otro código
de alta.

**Importación condicional**, como el almacén y el transporte: `dart:js_interop`
no existe fuera de web, y el día que haya app nativa la otra rama usará el
Enclave Seguro sin tocar nada de esto.

**Pantalla.** Una sola, con un campo para el código de alta y un botón. Aparece
cuando no hay token y desaparece cuando lo hay.
