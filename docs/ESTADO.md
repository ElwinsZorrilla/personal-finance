# Estado

Fase: 3 — Motor de presupuesto
Rama: fase/3-motor-presupuesto
Estado: por empezar
Paso: brief
Vuelta: 0 de 3
Compuerta 1: sin ejecutar
Revisión: —
Veredicto: —
Bloqueo: —

## Criterios de esta fase

- [ ] período de ingreso a ingreso, no de día 1 a 30
- [ ] `DineroSeguro = líquido − obligaciones pendientes − reserva de tarjetas − ahorro comprometido − fondo de seguridad − retenciones`
- [ ] disponible diario, con prueba de que el último día no divide por cero
- [ ] base histórica 50 / 30 / 20
- [ ] proyección de cierre y desviación de ritmo
- [ ] redistribución que nunca toca prioridad 1 ni 2, con prueba de que recortar alquiler falla
- [ ] devolución que reduce el gasto de su categoría original
- [ ] pago de tarjeta que mueve saldo y no crea gasto
- [ ] cobertura mínima 90 % en el proyecto del motor

## Siguiente acción

Escribir el brief de la Fase 3 en `docs/briefs/FASE-3.md`.

## Lo hecho

Fase 2 cerrada. CR-002 aprobada tras 2 vueltas, 3 Majors corregidos.
Compuerta 1 en verde: `dotnet format --verify-no-changes` sin cambios,
`dotnet build -c Release` con 0 avisos, 59 pruebas pasando. Las 41 de
integración corren contra PostgreSQL 16 real en contenedor.

Lo que la Fase 3 hereda y puede usar:

- `Margen.Domain.Money` — entero de centavos, con `Scale(num, den)` por
  fracción exacta y `Prorate(pesos)` por resto mayor. Es lo que reparte el
  50 / 30 / 20 sin perder un centavo.
- `Transaction.SpendingEffect` — aporte con signo al gasto. La devolución ya
  resta; el pago de tarjeta y la transferencia ya valen cero.
- `Priority` con los cuatro niveles. La redistribución tiene que respetar 1 y 2.
- Esquema completo en Postgres: períodos, presupuestos por categoría, pagos
  recurrentes.

## Notas de entorno

- El repositorio no tenía git inicializado. Se creó con la Fase 1 como commit
  base en `main`. **Falta añadir el remoto `origin`**: sin él no se puede hacer
  push de la rama, y el merge a `main` lo hace el humano.
- No hay toolchain de Flutter en esta máquina: la compuerta 1 de Flutter sigue
  sin correr desde la Fase 1. La Fase 3 tampoco toca `app/`.
- Hay .NET 10.0.204 y Docker 29.5.3 en funcionamiento.
