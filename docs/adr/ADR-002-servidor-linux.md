# ADR-002 — El servidor es Linux, no Windows

**Estado:** aceptado
**Fecha:** 2026-07-31

## Contexto

El brief describe un servidor Windows con IIS, DPAPI y SQL Server. El servidor
real es Linux con Docker, Portainer, Nginx Proxy Manager y PostgreSQL ya
funcionando.

## Decisión

Se conserva .NET y se descarta todo lo específico de Windows.

| Brief | Real | Motivo |
|---|---|---|
| IIS | Nginx Proxy Manager, ya instalado | No se añade un proxy donde ya hay uno |
| Windows DPAPI | ASP.NET Core Data Protection sobre volumen | DPAPI no existe fuera de Windows |
| SQL Server | PostgreSQL | Ya está desplegado y es el motor del stack |
| Servicio de Windows | Contenedor con `restart: unless-stopped` | Docker es el supervisor |
| Hangfire con panel | Cron dentro del worker | El panel es superficie expuesta sin dueño |

.NET se mantiene: el brief está escrito en C#, corre igual de bien en
contenedores Linux y cambiar de lenguaje ahora costaría más de lo que resuelve.

## Consecuencias

- Ningún servicio publica puertos al host. La base de datos vive en una red
  `internal: true` y no es alcanzable desde fuera del stack.
- Las claves de Data Protection viven en un volumen. Si se pierde, las sesiones
  y los tokens cifrados se invalidan: el volumen entra en el respaldo.
- El contenedor corre como UID 1001. Docker crea los volúmenes con dueño root,
  así que un servicio `init` ajusta el dueño una vez antes de que arranque la
  API. Sin eso, el contenedor entra en ciclo de reinicio con un error que no
  menciona permisos.
- Zona horaria fijada en `America/Santo_Domingo` en todos los servicios. Una
  compra de las 11 p.m. no puede caer en el día siguiente y cambiar de período
  presupuestario.
