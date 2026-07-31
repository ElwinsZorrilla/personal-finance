# Bitácora

Una línea por iteración cerrada.

```text
2026-07-31 · FASE-1 · sistema visual, primitivas de dominio, infraestructura
            CR-001 aprobada tras 2 vueltas · 6 Majors corregidos
            pendiente: correr la compuerta 1, empaquetar fuentes, responder ADR-001
            deuda abierta: m6 (fase 5), m7 (fase 4), m8 (fase 9)

2026-07-31 · FASE-2 · base del API, esquema, autenticación de dispositivo
            CR-002 aprobada tras 2 vueltas · 3 Majors corregidos
            compuerta 1 en verde: format sin cambios, Release sin avisos,
            59 pruebas · las 41 de integración sobre Postgres 16 en contenedor
            el repositorio no tenía git: la fase 1 quedó como commit base
            pendiente: añadir el remoto origin, correr la compuerta 1 de Flutter
            deuda abierta: m9–m12 (fase 4), m13 (fase 6)

2026-07-31 · FASE-3 · motor de presupuesto, lógica pura
            CR-003 aprobada tras 2 vueltas · 3 Majors corregidos
            compuerta 1 en verde: 185 pruebas · Margen.Budget al 100 % de
            líneas y ramas, contra el 90 % exigido
            la revisión multi-agente falló entera por límite de sesión:
            CR-003 la respalda un solo lector y hay que relanzarla
            deuda abierta: m14 (fase 8), m15 (sin fase, tarea de sesión)
```
