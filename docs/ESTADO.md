# Estado

Fase: 4 — Endpoints y contrato
Rama: fase/4-endpoints
Estado: en curso
Paso: plan
Vuelta: 0 de 3
Compuerta 1: sin ejecutar
Revisión: —
Veredicto: —
Bloqueo: —

## Criterios de esta fase

- [ ] transacciones, registro rápido, presupuesto, revisión, reglas, correos entrantes, conciliación, notificaciones
- [ ] OpenAPI generado y versionado en `docs/api/openapi.json`
- [ ] errores en formato ProblemDetails, ningún 500 con traza al cliente
- [ ] límite de peticiones en autenticación y en el registro rápido
- [ ] pruebas de integración sobre Postgres real en contenedor
- [ ] deuda m9, m10, m11 y m12 de CR-002 pagada

## Siguiente acción

Escribir `docs/planes/FASE-4.md` y empezar por el agregador entre la base y el
motor, que es la única pieza nueva con lógica y donde está el riesgo de la fase.

## Lo hecho

| Fase | Estado | Revisión | Pruebas |
|---|---|---|---|
| 1 · Sistema visual, dominio, infraestructura | Cerrada | CR-001 | compuerta 1 de Flutter sin correr |
| 2 · Base del API, esquema, autenticación | Cerrada | CR-002 | 41 de integración sobre Postgres 16 |
| 3 · Motor de presupuesto | Cerrada | CR-003 | 101, cobertura 100 % líneas y ramas |

Total en la solución: **185 pruebas**, compuerta 1 en verde.

Lo que la Fase 4 hereda y debe usar sin reimplementar:

- `Margen.Budget` entero. El cliente no calcula y el API tampoco: el API
  consulta, arma las entradas del motor y devuelve lo que el motor responde.
- `Outcome<T>`. Un resultado que no es `Computed` se serializa como campo nulo
  con su motivo, nunca como cero.
- `SpendingLedger.Summarize` **rechaza** una lista con el mismo
  `TransactionId` repetido. Un join que multiplique filas hará fallar el
  endpoint, que es lo que debe pasar.
- La autenticación por token opaco y las políticas de alcance de la Fase 2.

## Pendiente que no es de esta fase

- **Relanzar la revisión adversarial del motor** (m15 en `DEUDA.md`). La de la
  Fase 3 falló entera por límite de sesión y CR-003 la respalda un solo lector.
  Conviene antes de fusionar `fase/3-motor-presupuesto` a `main`.
- **Compuerta 1 de Flutter**, sin correr desde la Fase 1: no hay toolchain en
  esta máquina.
- **ADR-001**, sin responder. No bloquea hasta la Fase 11.
- El merge a `main` de las fases 2 y 3 lo hace el humano. Las ramas están
  publicadas.

## Notas de entorno

- Remoto `origin` configurado a `github.com/ElwinsZorrilla/personal-finance`.
  Ramas publicadas: `main`, `fase/2-base-api`, `fase/3-motor-presupuesto`.
- .NET 10.0.204 y Docker 29.5.3 en funcionamiento. Sin toolchain de Flutter.
