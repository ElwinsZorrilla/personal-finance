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

2026-08-03 · FASE-7 · parser del Banco Popular, desbloqueada y cerrada
            CR-008 aprobada tras 4 vueltas · 3 Blockers y 5 Majors corregidos
            las dos compuertas 1 en verde: 481 pruebas (390 .NET, 91 Flutter)
            16 de 16 muestras reales leídas · 0 a revisión · 0 ajenas
            cinco capturas hicieron falta: cada formato nuevo destapó una fuga
            del redactor que ninguna revisión de código habría encontrado
            la peor no fue una fuga sino lo contrario: la regla de los ocho
            dígitos destruyó la fecha del depósito y dejó una muestra que
            mentía sobre el formato · redactar de más no es la opción segura
            el redactor llegó a consumir su propia salida: la X que ponía en la
            referencia la releía el patrón de máscara con IgnoreCase
            M5 no tenía síntoma · el separador etiqueta-valor cruzaba el salto
            de línea y el comercio de los dos depósitos era el importe
            tres formas de escribir la fecha en cuatro plantillas del mismo
            banco: 26/07/2026, 12/6/2026 y 20260618
            depósito = Ingreso, el resto Egreso · Directions decide, no la UI
            faltan tres avisos que no estaban en los 209 correos del buzón:
            compra rechazada, devolución y pago de tarjeta
            deuda m23 pagada

2026-08-03 · FASE-8 · clasificación y anomalías
            CR-009 aprobada tras 2 vueltas · 5 Majors corregidos
            las dos compuertas 1 en verde: 634 pruebas (543 .NET, 91 Flutter)
            Margen.Classify: 99,8 % de líneas, 98,3 % de ramas
            proyecto nuevo Margen.Classify, lógica pura como el de presupuesto
            Outcome<T> se muda a Margen.Domain: ya lo necesitan dos motores
            el modelo sugiere y NUNCA decide · tres defensas, y la que importa
            es la del origen porque no depende de que nadie mueva un número
            la firma del puerto no admite Money, ni fecha, ni cuenta
            M1: UBER*EATS iba a transporte · el banco pega las palabras con
            asterisco y «UBER EATS» no estaba contenido · lo destapó la prueba
            sembrada con los seis comercios reales de la Fase 7
            M2: el historial se habría mordido la cola · columna
            CategoryConfirmedAt para que el automatismo no se cite a sí mismo
            mediana y desviación absoluta mediana, no media y desviación típica:
            cinco compras de 300 y una de 40 000 dan media 6 900, y ese rango no
            marca como raro justamente el cargo raro
            los tres Majors de la vuelta 2 estaban todos en la costura entre el
            motor y la base, que es donde el 99,8 % de cobertura no cubre nada
            deuda abierta: m27 (fase 12), m28 (fase 10)

2026-08-03 · FASE-9 · efectivo desde el iPhone
            CR-010 aprobada tras 1 vuelta · 1 Major corregido
            las dos compuertas 1 en verde: 692 pruebas (601 .NET, 91 Flutter)
            «Gasté 450 pesos en almuerzo» por POST /transactions/cash/phrase
            el monto, el día y la moneda salen de expresiones regulares
            deterministas · el modelo no toca la cifra, solo la categoría
            dos rutas y no un cuerpo con dos formas válidas: la misma trampa
            que llevó el PUT de movimientos a dejar de ser PATCH
            M1: ganaba el primer número y «compré 2 panes de 25 pesos» habría
            registrado 2 pesos · nada falla, nada avisa, solo la cifra está mal
            ahora gana el número con la moneda pegada y si no, el primero
            los numerales en palabras se rechazan: el dictado de iOS ya escribe
            dígitos y media implementación enseñaría que funciona
            la moneda extranjera se rechaza en vez de guardarse como pesos: no
            hay tasa de cambio y elegir una sería inventarse una cifra
            docs/atajo.md sin un solo secreto dentro, ni de ejemplo
            deuda abierta: m29 (fase 12)

2026-08-03 · FASE-10 · conciliación, y el caso de varios bancos
            CR-011 aprobada tras 1 vuelta · 1 Blocker y 3 Majors corregidos
            las dos compuertas 1 en verde: 771 pruebas (680 .NET, 91 Flutter)
            el usuario dijo a mitad de fase que tiene MÁS DE UN BANCO
            B1: la cuota de muestras era global · los seis primeros consumos del
            primer banco la agotaban y de los demás no se capturaba ninguno, y
            el recuento decía «compra-aprobada 6» como si estuviera cubierto
            M1: las muestras no decían de qué banco eran · con tres bancos eso
            hace inútil cualquier muestra, porque cada formato es distinto
            los dos estaban en la Fase 7 y pasaron dos revisiones sin verse
            el mapeo de columnas ES la respuesta a no conocer el formato, no un
            sustituto de conocerlo: un perfil por banco, escrito una vez
            explícitos a propósito: formato de fecha, estilo decimal y signo
            se detectan solos: separador —por consistencia, no por frecuencia—
            y codificación —UTF-8 o Latin-1—
            lo ausente se crea, lo discrepante y lo duplicado NO se tocan
            M2: sumar Money dentro de un GroupBy no lo traduce EF y revienta en
            ejecución · lo encontró la prueba de integración, no el compilador
            M3: una línea aproximada se quedaba con el movimiento de una exacta
            el cierre de período recomienda desde lo GASTADO, no lo asignado
            deuda abierta: m30 (fase 12), m31 (fase 11)
```
