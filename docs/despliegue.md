# Desplegar Margen

Todo lo de este documento lo ejecuta una persona. **Nada de esto lo hace el
asistente**: toca producción y credenciales, y eso está fuera de lo que puede
hacer sin ti.

---

## Lo que hay que tener antes

- Docker y Docker Compose en el servidor.
- **Nginx Proxy Manager ya corriendo**, publicando 80 y 443, con una red externa
  a la que se pueda conectar el API.
- Un dominio apuntando al servidor.
- Un registro de imágenes al que puedas empujar, o construir en el propio
  servidor.

## Cómo entra el tráfico

```
Internet → 443 → Nginx Proxy Manager → red del proxy → api:8080
                                            │
                                     red interna (sin salida)
                                            │
                                    db · worker · backup
```

**Ningún servicio publica un puerto al host.** El único camino de entrada es el
proxy. La base de datos vive en una red marcada `internal: true`, que no tiene
salida a internet ni entrada desde fuera del stack: aunque su contraseña se
filtre, no hay por dónde usarla sin estar ya dentro del servidor.

---

## 1 · Construir las imágenes

```bash
docker build -f src/Margen.Api/Dockerfile -t TU-REGISTRO/margen-api:1.0 .
docker build -f src/Margen.Worker/Dockerfile -t TU-REGISTRO/margen-worker:1.0 .

docker push TU-REGISTRO/margen-api:1.0
docker push TU-REGISTRO/margen-worker:1.0
```

`TAG` en el `.env` apunta a la versión. **Usa un número, no `latest`**: con
`latest` no se sabe qué está corriendo y no se puede volver atrás.

## 2 · Rellenar el `.env`

```bash
cp infra/.env.example infra/.env
```

Y completarlo. Los valores que hay que generar:

```bash
openssl rand -base64 32   # POSTGRES_PASSWORD
openssl rand -base64 24   # AUTH_ENROLLMENT_CODE
```

La contraseña de Gmail es una **contraseña de aplicación**: ver
[`gmail.md`](gmail.md). Qué hace cada secreto y cómo se rota:
[`secretos.md`](secretos.md).

`PROXY_NETWORK` tiene que ser la red a la que ya está conectado NPM:

```bash
docker network ls
```

## 3 · Levantar

```bash
cd infra
docker compose up -d
```

El orden lo resuelve el compose: `init` arregla los permisos del volumen de
claves y termina, `db` espera a estar sano, y solo entonces arrancan `api` y
`worker`. **El API aplica las migraciones al arrancar**, y solo el API: dos
procesos migrando la misma base a la vez es una carrera.

## 4 · Comprobar antes de abrirlo al mundo

```bash
docker compose ps                       # todos «running», api «healthy»
docker compose exec api curl -fsS localhost:8080/health/ready
docker compose logs api | tail -30
```

`/health/ready` responde 503 mientras la base no esté lista o el esquema esté
atrasado. Que responda 200 es la señal de que se puede enchufar el proxy.

### Comprobación obligatoria: los tipos de contenido

```bash
for f in / /index.html /main.dart.js /manifest.json; do
  echo -n "$f -> "; curl -sI "https://TU-DOMINIO$f" | grep -i "^content-type"
done
```

Tiene que salir `text/html`, `text/html`, `application/javascript` y
`application/json`. **Si sale `application/octet-stream`, el navegador
descargará la página en vez de abrirla**, como un archivo llamado «data».

No es una comprobación de más. Pasó: un bloque `types` en la configuración de
nginx **sustituye la tabla entera** de tipos MIME en vez de añadirse a ella, así
que declarar uno solo dejó todo lo demás sin tipo. `nginx -t` daba correcto, el
contenedor arrancaba sano, el proxy respondía 200 y la app no se abría.

Lo único que lo detecta es pedir la página y mirar la cabecera.

## 5 · El proxy

En NPM, un *Proxy Host*:

| Campo | Valor |
|---|---|
| Domain | tu dominio |
| Scheme | `http` |
| Forward Hostname | `api` |
| Forward Port | `8080` |
| Block Common Exploits | sí |
| SSL | Let's Encrypt, con *Force SSL* y HTTP/2 |

**Force SSL no es opcional.** Sin él, el token del dispositivo viaja en claro la
primera vez que alguien escribe la dirección sin `https`.

## 6 · Dar de alta el teléfono

Con el `AUTH_ENROLLMENT_CODE` del `.env`. Y en cuanto termines, **rótalo**:
déjalo vacío si no vas a registrar más dispositivos. Vacío, el alta responde 503
y no registra a nadie.

---

## Los respaldos

El servicio `backup` corre solo. Cada día hace un `pg_dump` en formato
comprimido, comprueba que el archivo se puede leer y borra los de más de
catorce días. **Una vez por semana lo restaura entero en una base descartable** y
cuenta las filas: un respaldo que nunca se restauró es un archivo, no un
respaldo.

```bash
docker compose logs backup | tail -20
ls -lh "${BACKUP_PATH}"
```

Que haya **varios archivos con fechas distintas** es la comprobación que
importa. Si solo hay uno, algo va mal: los respaldos se están pisando y solo
proteges de que se rompa el disco, no de un borrado que se note tres días
después.

### Restaurar de verdad

```bash
docker compose stop api worker
docker compose exec backup /usr/local/bin/restaurar.sh /backups/margen-AAAAMMDD-HHMMSS.dump
docker compose start api worker
```

Pide escribir el nombre de la base para confirmar, y comprueba el archivo
**antes** de tocar nada: vaciar la base y descubrir después que el respaldo
estaba corrupto deja sin datos y sin respaldo.

### Qué respaldar además de la base

El volumen **`dataprotection`**. Si se pierde, todos los tokens dejan de
validarse a la vez y hay que dar de alta el teléfono otra vez. No es una
catástrofe, pero saberlo de antemano ahorra un susto.

```bash
docker run --rm -v margen_dataprotection:/keys -v "$PWD:/salida" \
  busybox tar czf /salida/claves.tar.gz -C /keys .
```

---

## Actualizar

```bash
docker build ... -t TU-REGISTRO/margen-api:1.1 .
docker push TU-REGISTRO/margen-api:1.1

# TAG=1.1 en infra/.env
docker compose up -d
```

**Antes de una versión que traiga migraciones, haz un respaldo a mano.** El API
migra al arrancar y las migraciones de EF Core no se deshacen solas.

```bash
docker compose exec backup sh -c \
  'pg_dump -h db -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /backups/antes-de-1.1.dump'
```

## Volver atrás

```bash
# TAG=1.0 en infra/.env
docker compose up -d
```

Si la versión nueva migró el esquema, volver la imagen **no basta**: hay que
restaurar el respaldo de antes de migrar. Por eso se hace ese respaldo.

---

## Qué mirar cuando algo va mal

| Síntoma | Dónde mirar |
|---|---|
| El proxy da 502 | `docker compose ps`: el API no está sano o no está en la red del proxy |
| `/health/ready` da 503 | `docker compose logs api`: la base no responde o la migración falló |
| No entran movimientos | `docker compose logs worker`: credenciales de IMAP, o remitente fuera de la lista blanca |
| Entran correos y no se crean movimientos | Normal si el banco no tiene parser: quedan en Revisión. Ver `docs/muestras/README.md` |
| El alta responde 503 | `AUTH_ENROLLMENT_CODE` vacío. Es lo correcto salvo que estés registrando un teléfono |
