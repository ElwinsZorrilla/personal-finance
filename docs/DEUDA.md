# Deuda técnica

Deuda sin fase de pago asignada es deuda que no se paga. Nada entra aquí sin
una columna «Se paga en».

| # | Origen | Qué falta | Por qué se pospuso | Se paga en |
|---|---|---|---|---|
| m6 | CR-001 | La compuerta 1 no incluye compilación release en cada vuelta | Compilar release en cada vuelta alarga el ciclo de minutos a decenas de minutos | Fase 5, cuando exista pipeline |
| m7 | CR-001 | `_RiskNote` usa `projectedDepletion!` protegido por el llamador | Hoy hay un solo llamador y es correcto | Fase 4, al reutilizar el widget en Presupuesto |
| m8 | CR-001 | `infra/backup.sh` usa `sleep 86400` y deriva | La deriva es de segundos por ciclo, sin efecto en un respaldo diario | Fase 12, junto con la prueba de restauración |
| — | Fase 1 | `MockRepository` sigue en el árbol de release aunque `Env.useMocks` sea `false` | El compilador de Dart elimina el código muerto con `bool.fromEnvironment` constante; falta confirmarlo midiendo el binario | Fase 5 |
| m9 | CR-002 | Dos altas simultáneas del mismo teléfono chocan contra el índice único y salen como 500 en lugar de devolver el dispositivo existente | Hay un solo usuario y un solo teléfono; la carrera necesita dos peticiones en el mismo milisegundo | Fase 4, junto con el resto del manejo de errores |
| m10 | CR-002 | El alta de dispositivo y el canje de reto no tienen límite de peticiones | El plan lo pone en la Fase 4, junto con los endpoints que protege. El código de alta son 24 bytes aleatorios: la fuerza bruta no es el ataque realista | Fase 4 |
| m11 | CR-002 | `POST /transactions/cash` responde 501; existe solo para poder comprobar el alcance del token del Atajo | Sin una ruta que ese token sí alcance, «alcance restringido» se cumpliría por accidente | Fase 9, que la implementa |
| m12 | CR-002 | `Guid.Parse(user.FindFirstValue(...)!)` en cuatro endpoints. Una reclamación ausente sería un 500 | Las escribe el mismo handler que autentica; hoy no hay forma de que falten | Fase 4, al extraer el identificador a un filtro |
| m13 | CR-002 | `IncomingEmails.BodyHash` es único en toda la tabla: dos correos legítimos con el cuerpo idéntico se rechazarían como duplicado | No se puede decidir sin ver un correo real del banco: depende de si el cuerpo trae referencia u hora con segundos | Fase 6, con las muestras de la Fase 7 |
| m14 | CR-003 | `SpendingLedger.ResolveCategory` sigue una sola referencia: la devolución de una devolución se imputa a la categoría intermedia, no a la original | Hoy ningún parser genera cadenas de devoluciones, y resolverlas mal es preferible a un recorrido con ciclos sin límite | Fase 8, con la cascada de clasificación |
| m15 | CR-003 | La revisión adversarial multi-agente del motor no llegó a ejecutarse: los seis agentes se toparon con el límite de sesión. CR-003 la respalda un solo lector | Sin cuota. El guion queda escrito y se puede relanzar tal cual | No es deuda de código: relanzar cuando haya cuota, antes de fusionar la Fase 3 a `main` |
