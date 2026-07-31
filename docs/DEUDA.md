# Deuda técnica

Deuda sin fase de pago asignada es deuda que no se paga. Nada entra aquí sin
una columna «Se paga en».

| # | Origen | Qué falta | Por qué se pospuso | Se paga en |
|---|---|---|---|---|
| m6 | CR-001 | La compuerta 1 no incluye compilación release en cada vuelta | Compilar release en cada vuelta alarga el ciclo de minutos a decenas de minutos | Fase 5, cuando exista pipeline |
| m7 | CR-001 | `_RiskNote` usa `projectedDepletion!` protegido por el llamador | Hoy hay un solo llamador y es correcto | Fase 4, al reutilizar el widget en Presupuesto |
| m8 | CR-001 | `infra/backup.sh` usa `sleep 86400` y deriva | La deriva es de segundos por ciclo, sin efecto en un respaldo diario | Fase 12, junto con la prueba de restauración |
| — | Fase 1 | `MockRepository` sigue en el árbol de release aunque `Env.useMocks` sea `false` | El compilador de Dart elimina el código muerto con `bool.fromEnvironment` constante; falta confirmarlo midiendo el binario | Fase 5 |
