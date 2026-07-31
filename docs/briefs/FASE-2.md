# Brief — Fase 2 · Base del API

## Qué se construye

El esqueleto del servidor: la solución .NET, el esquema completo en PostgreSQL,
la sonda de disponibilidad y la autenticación del dispositivo. No hay lógica de
negocio todavía —el motor de presupuesto es la Fase 3 y los endpoints de datos
son la Fase 4—. Lo que se construye aquí es el suelo sobre el que se paran las
dos fases siguientes.

Tres cosas se deciden en esta fase y no se vuelven a tocar sin dolor: **el tipo
de las columnas**, **cómo se identifica el dispositivo** y **qué significa que
el servidor esté listo**. Por eso van primero.

## Por qué así

**El esquema completo de una vez, no por fases.** Nueve tablas de negocio en una
sola migración inicial. La alternativa —una migración por fase— produce una
cadena de veinte migraciones que nadie puede aplicar en limpio con confianza, y
obliga a que la Fase 3 empiece modificando el esquema en vez de calculando. Las
tablas nacen vacías; llenarlas es trabajo de las fases siguientes.

**ECDSA P-256, no Ed25519.** El Enclave Seguro del iPhone genera claves de una
sola curva: P-256. Una clave que no vive en el Enclave es una clave que se puede
copiar del respaldo del teléfono. Ed25519 es mejor criptografía y peor decisión
aquí: obligaría a guardar la clave privada en el llavero en vez del Enclave, y
además .NET 10 no lo trae de fábrica. P-256 con SHA-256 está en
`System.Security.Cryptography` sin dependencias.

**Token opaco, no JWT.** La Fase 9 pide un token del Atajo «restringido,
revocable». Un JWT no se revoca: se espera a que caduque. El token es 32 bytes
aleatorios; en la base vive solo su SHA-256, igual que una contraseña. Revocar
es un `UPDATE`. Verificar es un índice. Y no hay una clave de firma más que
proteger.

**El reto se consume, no se compara.** La defensa contra la repetición no es
mirar si la firma ya se vio: es que el reto solo se pueda canjear una vez, con
la marca de consumo dentro de la misma transacción que emite el token. Una
comprobación en dos pasos —leer, verificar, escribir— tiene una ventana entre el
segundo y el tercero.

**Fecha absoluta en la base, zona horaria solo al presentar.** `timestamptz` en
toda columna de instante. Postgres guarda el punto en la línea del tiempo, no la
lectura de un reloj. Una compra de las 11:30 de la noche del 15 es la misma
compra la mire quien la mire; lo que cambia es cómo se escribe, y eso es asunto
de la pantalla.

## Alcance

Dentro:

- Solución `Margen.sln` con cuatro proyectos y dos de prueba.
- Entidades y `DbContext`; migración inicial; nueve tablas de negocio más tres
  de autenticación.
- `GET /health/live` y `GET /health/ready`.
- Registro de dispositivo, reto, canje de firma por token.
- Token del Atajo con alcance `cash:create` y nada más.
- Pruebas de integración contra Postgres 16 en contenedor.

Fuera, y a propósito:

- Cualquier endpoint de datos. Fase 4.
- Cualquier cálculo de presupuesto. Fase 3.
- Límite de peticiones. Fase 4 lo pide junto con los endpoints que protege; un
  limitador sobre dos endpoints de autenticación que todavía no tienen tráfico
  es código sin forma de comprobarse.
- OpenAPI versionado en `docs/api/`. Fase 4.
- Cualquier cosa que toque `app/`.

## Cómo se comprueba

Cada criterio del plan, con lo que lo demuestra:

| Criterio | Prueba | Dónde |
|---|---|---|
| `dotnet build` limpio | `dotnet build -c Release` sin avisos | compuerta 1 |
| Migración inicial sobre Postgres 16 en limpio | Contenedor nuevo, `Migrate()`, y las doce tablas existen | `SchemaTests.la_migracion_inicial_crea_el_esquema_completo` |
| `/health/ready` 200 con base | Petición contra la aplicación con el contenedor arriba | `HealthTests.ready_responde_200_con_la_base_conectada` |
| `/health/ready` 503 sin base | Aplicación apuntada a un puerto muerto | `HealthTests.ready_responde_503_sin_la_base` |
| Toda columna de instante es `timestamptz` | `information_schema` no devuelve ninguna `timestamp without time zone`, y el modelo tampoco | `SchemaTests.ninguna_columna_de_instante_pierde_la_zona`, `ModelTests.toda_propiedad_de_instante_va_a_una_columna_con_zona` |
| Las 23:30 no cambian de día | Guardar `2026-03-15 23:30` de Santo Domingo, leer, convertir de vuelta | `TimeZoneTests.una_compra_de_las_23_30_no_cambia_de_dia` |
| Ninguna columna monetaria en punto flotante | `information_schema` no devuelve `double precision`, `real` ni `numeric` en toda la base, y el modelo no tiene ninguna propiedad de esos tipos | `SchemaTests.ninguna_columna_es_de_punto_flotante`, `ModelTests.ninguna_propiedad_del_modelo_es_de_punto_flotante` |
| Firma inválida rechazada | Firmar con otra clave contra el mismo reto | `AuthTests.una_firma_de_otra_clave_no_abre_sesion` |
| Firma repetida rechazada | Canjear el mismo reto y firma dos veces | `AuthTests.el_mismo_reto_no_se_canjea_dos_veces` |
| Token del Atajo restringido | Con él, los endpoints de alcance total responden 403 y el de efectivo no | `ScopeTests.el_token_del_atajo_no_alcanza_mas_que_efectivo`, `ScopeTests.el_token_del_atajo_si_alcanza_el_registro_de_efectivo` |

Los nombres de arriba son los definitivos. La primera versión de esta tabla los
escribió en español —`EsquemaTests`, `AutenticacionTests`— antes de comprobar la
convención de la Fase 1, que es identificadores en inglés y prosa en español.
Queda anotado porque el plan explica por qué esa convención no se rompe.

La cobertura mínima del 80 % no aplica todavía: en esta fase no hay proyecto de
motor de presupuesto y `Margen.Domain` es casi solo forma. La exigencia entra en
la Fase 3, donde vive el riesgo de error silencioso.

## Los siete riesgos, aplicados a esta fase

1. **Aritmética de dinero.** Aplica. `long` de centavos en el dominio, `bigint`
   en la base. Hay una prueba que interroga a `information_schema`, no un
   comentario que promete.
2. **Idempotencia.** Aplica parcialmente. No hay ingesta todavía, pero el
   esquema tiene que nacer con la defensa puesta: índice único sobre el
   `MessageId` del correo entrante y sobre su huella.
3. **El modelo no toca cifras.** No aplica. No hay ninguna llamada a un modelo
   en esta fase.
4. **Fallo cerrado.** Aplica. Sin cadena de conexión, la aplicación no arranca.
   Sin código de alta configurado, el registro de dispositivos rechaza todo. Un
   valor por defecto en cualquiera de los dos es un hueco abierto.
5. **Zona horaria.** Aplica. Es el criterio de las 23:30.
6. **Superficie expuesta.** Aplica. El compose de la Fase 1 ya no publica
   puertos; esta fase no añade ninguno y no mete un secreto en el árbol.
7. **Accesibilidad.** No aplica. No hay interfaz en esta fase.

## Terminado es

Los siete criterios del plan marcados, cada uno con una prueba que corre en
verde, la compuerta 1 en verde y CR-002 sin Blockers ni Majors abiertos.
