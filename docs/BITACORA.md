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

2026-07-31 · FASE-4 · endpoints, agregador y contrato versionado
            CR-004 aprobada tras 2 vueltas · 3 Majors corregidos
            compuerta 1 en verde: 234 pruebas · 90 de integración sobre
            Postgres 16 · contrato de 19 rutas en docs/api/openapi.json
            deuda m9, m10, m11 y m12 pagada
            la compilación rechazó Microsoft.OpenApi 2.0.0 por vulnerabilidad
            deuda abierta: m16 (fase 12), m17 (fase 10), m18 (fase 10)

2026-07-31 · PASO CERO DE LA FASE 5 · la compuerta 1 de Flutter, por fin
            CR-005 aprobada · 3 Blockers y 1 Major en el árbol de la Fase 1
            se encontró Flutter 3.44.5 en C:\src\flutter, fuera del PATH
            el árbol no compilaba: SectionLabel usaba Divider sin Material
            MoneyFormat escribía RD$ 12.000 por confiar en es_DO de ICU
            DateLabel lanzaba al pintar por falta de initializeDateFormatting
            51 pruebas · core/ al 98 %, domain/ al 84 %
            fuentes SIL OFL descargadas y versionadas
            deuda abierta: m19 (fase 5), m20 (fase 5)

2026-07-31 · FASE-5 · capa de datos en Flutter
            CR-006 aprobada tras 2 vueltas · 3 Majors corregidos
            las dos compuertas 1 en verde: 320 pruebas (234 .NET, 86 Flutter)
            cobertura Flutter: core 98 %, domain 95 %, data 83 %
            el contrato tenía dos huecos que habrían obligado a calcular en el
            cliente: se resolvieron en el servidor
            MockRepository fuera del release, medido sobre el binario
            deuda m20 y la de MockRepository pagadas
            deuda abierta: m21 (fase 12), m22 (fase 11)

2026-07-31 · FASE-6 · ingesta de correo, sin parser de banco
            CR-007 aprobada tras 2 vueltas · 1 Blocker y 2 Majors corregidos
            las dos compuertas 1 en verde: 367 pruebas (281 .NET, 86 Flutter)
            el Blocker: la huella tenía granularidad de día, así que dos cafés
            iguales el mismo día borraban el segundo sin dejar rastro
            cuatro defensas contra el duplicado, una prueba cada una
            deuda abierta: m23 (fase 7), m24 (fase 10)

2026-07-31 · FASE-7 · BLOQUEADA, con la herramienta para desbloquearla
            captura de muestras desde Gmail por IMAP en SOLO LECTURA
            redactor probado con 16 casos: quita nombres, correos, tarjetas,
            montos, referencias y cadenas largas de dígitos, y conserva el
            formato, que es lo que el parser tiene que aprender
            docs/muestras/*.txt en .gitignore hasta que se lean
            falta que el humano corra la captura con su contraseña
```
