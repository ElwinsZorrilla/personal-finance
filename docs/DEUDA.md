# Deuda técnica

Deuda sin fase de pago asignada es deuda que no se paga. Nada entra aquí sin
una columna «Se paga en».

| # | Origen | Qué falta | Por qué se pospuso | Se paga en |
|---|---|---|---|---|
| m6 | CR-001 | La compuerta 1 no incluye compilación release en cada vuelta | Compilar release en cada vuelta alarga el ciclo de minutos a decenas de minutos | Fase 5, cuando exista pipeline |
| m7 | CR-001 | `_RiskNote` usa `projectedDepletion!` protegido por el llamador | Hoy hay un solo llamador y es correcto | Fase 5, al conectar la pantalla de Presupuesto a datos reales. La Fase 4 no tocó `app/` |
| m8 | CR-001 | `infra/backup.sh` usa `sleep 86400` y deriva | La deriva es de segundos por ciclo, sin efecto en un respaldo diario | Fase 12, junto con la prueba de restauración |
| — | Fase 1 | `MockRepository` sigue en el árbol de release aunque `Env.useMocks` sea `false` | El compilador de Dart elimina el código muerto con `bool.fromEnvironment` constante; falta confirmarlo midiendo el binario | Fase 5 |
| ~~m9~~ | CR-002 | Dos altas simultáneas del mismo teléfono chocan contra el índice único y salen como 500 en lugar de devolver el dispositivo existente | Hay un solo usuario y un solo teléfono; la carrera necesita dos peticiones en el mismo milisegundo | **Pagada en la Fase 4** (CR-004) |
| ~~m10~~ | CR-002 | El alta de dispositivo y el canje de reto no tienen límite de peticiones | El plan lo pone en la Fase 4, junto con los endpoints que protege | **Pagada en la Fase 4** (CR-004) |
| ~~m11~~ | CR-002 | `POST /transactions/cash` responde 501; existe solo para poder comprobar el alcance del token del Atajo | Sin una ruta que ese token sí alcance, «alcance restringido» se cumpliría por accidente | **Pagada en la Fase 4** (CR-004); la interpretación de texto sigue en la Fase 9 |
| ~~m12~~ | CR-002 | `Guid.Parse(user.FindFirstValue(...)!)` en cuatro endpoints. Una reclamación ausente sería un 500 | Las escribe el mismo handler que autentica; hoy no hay forma de que falten | **Pagada en la Fase 4** (CR-004) |
| m13 | CR-002 | `IncomingEmails.BodyHash` es único en toda la tabla: dos correos legítimos con el cuerpo idéntico se rechazarían como duplicado | No se puede decidir sin ver un correo real del banco: depende de si el cuerpo trae referencia u hora con segundos | Fase 6, con las muestras de la Fase 7 |
| m14 | CR-003 | `SpendingLedger.ResolveCategory` sigue una sola referencia: la devolución de una devolución se imputa a la categoría intermedia, no a la original | Hoy ningún parser genera cadenas de devoluciones, y resolverlas mal es preferible a un recorrido con ciclos sin límite | Fase 8, con la cascada de clasificación |
| m15 | CR-003 | La revisión adversarial multi-agente del motor no llegó a ejecutarse: los seis agentes se toparon con el límite de sesión. CR-003 la respalda un solo lector | Sin cuota. El guion queda escrito y se puede relanzar tal cual | No es deuda de código: relanzar cuando haya cuota, antes de fusionar la Fase 3 a `main` |
| m16 | CR-004 | El panel dispara cinco consultas extra por la base histórica: una por período cerrado más el conteo | Con un usuario y tres períodos son milisegundos; optimizarlo ahora sería adivinar dónde duele | Fase 12, con medición real |
| m17 | CR-004 | La redistribución persiste `Adjustment ±= monto` en vez de los valores que devuelve el motor. Equivalentes hoy | Guardar el ajuste aparte es lo que deja el rastro de qué se movió; unificarlo pide rediseñar el registro de cambios | Fase 10, con el cierre de período |
| m18 | CR-004 | `Page<T>.NextCursor` siempre es nulo: la paginación real no existe | Con un año de movimientos personales no se llega al tope de 100 por página | Fase 10 |
