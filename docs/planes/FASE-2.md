# Plan — Fase 2 · Base del API

## Árbol

**Idioma.** La Fase 1 fijó la convención y esta fase la respeta: identificadores
en inglés, prosa —comentarios, documentación y nombres de prueba— en español.
El motivo no es estético: los tipos del servidor son el contrato que consume el
cliente Flutter, donde ya existen `Money`, `TxKind` y `DashboardSnapshot`. Un
`Movimiento` en C# frente a un `TxRecord` en Dart obliga a traducir en el
serializador, y una traducción es un lugar donde dos nombres se separan.

```text
Margen.sln
Directory.Build.props              avisos como errores, nullable, marco común
Directory.Packages.props           versiones de paquete centralizadas
.editorconfig                      reglas que dotnet format verifica

src/
  Margen.Domain/                   sin dependencias de nada
    Money.cs                         entero de centavos, contraparte de Money en Dart
    Enums.cs                         TxKind, TxStatus, TxSource, Priority
    Entities/
      Account.cs  Transaction.cs  Category.cs  BudgetPeriod.cs
      CategoryBudget.cs  MerchantRule.cs  RecurringPayment.cs
      IncomingEmail.cs  Alert.cs
      Device.cs  DeviceChallenge.cs  AccessToken.cs
    Scopes.cs                        constantes de alcance de token

  Margen.Infrastructure/           EF Core y nada de HTTP
    MargenDbContext.cs
    Configurations/*.cs              una por entidad
    Migrations/                      generada por dotnet ef
    ServiceCollectionExtensions.cs

  Margen.Api/                      ASP.NET Core
    Program.cs
    Health/DatabaseReadyCheck.cs
    Auth/
      TokenAuthenticationHandler.cs  esquema Bearer sobre token opaco
      DeviceService.cs               alta, reto, canje
      TokenService.cs                emisión, hash, revocación
      ScopePolicies.cs               políticas de autorización
      Contracts.cs                   cuerpos de petición y respuesta
    Endpoints/AuthEndpoints.cs
    Endpoints/ProbeEndpoints.cs      dos endpoints para comprobar el alcance
    Dockerfile

  Margen.Worker/                   solo el esqueleto; su trabajo es la Fase 6
    Program.cs
    Dockerfile

tests/
  Margen.Domain.Tests/
    MoneyTests.cs
  Margen.Api.Tests/
    Infra/PostgresFixture.cs         Testcontainers, un contenedor por colección
    Infra/TestApp.cs                 WebApplicationFactory
    Infra/TestDeviceKey.cs           firma P-256 desde el lado del cliente
    SchemaTests.cs
    TimeZoneTests.cs
    HealthTests.cs
    AuthTests.cs
    ScopeTests.cs
```

`Margen.Worker` entra ahora porque el compose de la Fase 1 ya lo declara y ya le
pasa variables de entorno. Un servicio nombrado en el stack que no existe en la
solución es una discrepancia que se paga en el primer despliegue.

## Decisiones de esquema

**Claves.** `Guid` v7 generado en el servidor (`Guid.CreateVersion7()`, .NET 9+).
Ordenado por tiempo, así que el índice no se fragmenta como con v4, y no
depende de una secuencia de la base como un `bigint` identidad.

**Dinero.** Toda columna monetaria es `bigint` y guarda centavos. En el dominio
es `Money`, un `readonly record struct` sobre `long`, con conversión a EF por
`HasConversion`. No hay `decimal` en ningún camino: entra por la puerta de atrás
como «solo para el total» y termina en una división.

El escalado no toma un `double`. `Money` en Dart tiene `scaled(double)` porque
lo usa un widget para dibujar una barra; aquí lo usaría el motor de presupuesto
para repartir el 50/30/20 de la base histórica, y ahí un `double` sí llega a la
cifra que se muestra. `Scale(long numerador, long denominador)` hace la
multiplicación en `Int128` y redondea a la mitad alejándose del cero, de forma
explícita. `Prorate` reparte por pesos con el método del resto mayor: la suma de
las partes es exactamente el total, siempre.

**Instantes.** Todo `DateTime` es `DateTimeKind.Utc` y toda columna es
`timestamptz`. Las fechas sin hora —el día de un período presupuestario, el
vencimiento de un pago recurrente— son `DateOnly` sobre `date`, que es un
concepto distinto y no tiene zona que perder.

**Idempotencia del correo.** `CorreoEntrante` nace con índice único sobre
`MessageId` y otro sobre `HuellaCuerpo`. La Fase 6 los usa; la Fase 2 los pone,
porque añadir un índice único a una tabla que ya tiene duplicados es una
migración que falla en producción.

## Decisiones de autenticación

```text
1. alta   POST   /auth/devices          código de alta + clave pública SPKI
2. reto   POST   /auth/challenges       deviceId → 32 bytes aleatorios, 2 min
3. canje  POST   /auth/tokens           firma del reto → token de 30 días
4. atajo  POST   /auth/shortcut-tokens  token de alcance cash:create, 1 año
   quién  GET    /auth/whoami           dispositivo y alcances del token en uso
   lista  GET    /auth/tokens           tokens del dispositivo, sin los hashes
   revoca DELETE /auth/tokens/{id}      idempotente, solo tokens propios
```

El código de alta viaja en la cabecera `X-Margen-Enrollment` y no en el cuerpo,
para que no aparezca en un registro de peticiones que vuelque el cuerpo.

- El **código de alta** llega por `Auth__CodigoDeAlta`. Si no está configurado,
  el paso 1 responde 503 y no registra nada. Fallo cerrado.
- El **reto** se guarda con su fecha de consumo en nulo. El canje lo marca con
  un `UPDATE ... WHERE ConsumidoEn IS NULL` y exige que afecte una fila. Si
  afecta cero, otro ya lo canjeó y la respuesta es 401.
- El **token** se devuelve una sola vez en claro. En la base vive
  `SHA256(token)`; la búsqueda es por ese hash y la comparación del resto es en
  tiempo constante.
- El **token del Atajo** solo se puede pedir con un token de alcance total, y
  lleva `cash:create` y nada más.

Para comprobar que el alcance del Atajo está restringido hace falta una ruta que
ese token **sí** alcance: si no, el criterio quedaría cumplido por accidente
—un token que no puede hacer nada tampoco puede hacer de más—.

La primera versión de este plan resolvía eso con dos endpoints de sondeo
inventados. Se descartó: dejar rutas falsas en el árbol para que una prueba
tenga contra qué apuntar es peor que la ruta real vacía. Lo que hay es
`POST /transactions/cash`, la ruta de verdad, con su autorización puesta y
devolviendo 501 hasta que la Fase 9 la implemente. La prueba distingue 501 de
403, que es la diferencia entre «autorizado y sin implementar» y «sin permiso».
Queda anotado en `DEUDA.md`.

## Qué se puede romper

| Riesgo | Señal | Qué hago |
|---|---|---|
| Npgsql rechaza un `DateTime` que no es UTC | excepción al guardar | Es lo correcto: no se desactiva. Se normaliza en el dominio |
| `dotnet ef` no está instalado | falla el paso de migración | `dotnet tool` local, fijado en `.config/dotnet-tools.json` |
| Testcontainers no encuentra Docker | fallan todas las pruebas de integración | Docker verificado antes de empezar; si cae, la fase se detiene y se dice |
| `America/Santo_Domingo` no resuelve en Windows | falla la prueba de zona | .NET usa ICU desde la 6; se comprueba en la primera prueba que se escriba |
| Avisos como errores frenan la generación de migraciones | `dotnet ef` falla | El directorio `Migrations` queda excluido del análisis en `Directory.Build.props` |
| El Worker vacío no compila sin un `Main` | falla la solución | Plantilla `worker` completa, no un `csproj` suelto |

## Orden

1. Solución, `Directory.Build.props`, `.editorconfig`, herramienta `dotnet-ef`.
2. Dominio: `Dinero`, enums, entidades.
3. `DbContext` y configuraciones; migración inicial.
4. Pruebas de esquema y de zona horaria. Aquí se sabe si el suelo está bien.
5. `Program.cs`, sonda de salud, sus dos pruebas.
6. Autenticación completa y sus pruebas.
7. Alcances y la prueba del token del Atajo.
8. Dockerfiles, README de compilación.
9. Compuerta 1.

Los pasos 4 y 6 son los que importan. Si algo se cae, se cae ahí.
