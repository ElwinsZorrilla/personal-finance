# Reglas de contención del despliegue

El servidor tiene **otras cosas en producción**. Este documento dice qué se
toca y qué no, y está escrito antes de tocar nada para que se pueda objetar
antes y no después.

## Lo único que se crea

| Recurso | Nombre | Por qué es seguro |
|---|---|---|
| Proyecto de Compose | `margen` | Docker aísla por proyecto. `docker compose -p margen down` se lleva esto y nada más |
| Contenedores | `margen-api`, `margen-worker`, `margen-db`, `margen-backup`, `margen-init` | Prefijados por el proyecto |
| Red interna | `margen_internal` | Nueva, marcada `internal: true`, sin salida |
| Volúmenes | `margen_pgdata`, `margen_dataprotection` | Nuevos |
| Directorio | uno solo, el que me digas | Nada fuera de ahí |

## Lo que no se toca, en ningún caso

- **Nada de Nginx Proxy Manager.** Ni su contenedor, ni su configuración, ni sus
  certificados, ni su base. El alta del *Proxy Host* la haces tú desde su
  interfaz web.
- **La red del proxy** se usa como `external: true`. Se **conecta** a ella; no
  se crea, ni se modifica, ni se borra.
- **Ningún contenedor, volumen, red o imagen que no empiece por `margen`.**
- **Ningún puerto del host.** El stack no publica ninguno. Si algún puerto está
  ocupado por lo tuyo, este stack no puede chocar con él porque no pide ninguno.

## Órdenes que no voy a ejecutar

```
docker system prune          # se lleva lo de todo el mundo
docker volume prune
docker network prune
docker image prune -a
docker stop/rm <algo que no sea margen->
rm -rf fuera del directorio del proyecto
apt / yum / systemctl        # el estado del servidor no es de este proyecto
```

Si algo de eso hiciera falta, se para y se pregunta.

## El orden

1. **Reconocimiento, sin escribir nada.** Qué hay levantado, qué redes existen,
   qué puertos están tomados, cuánto disco queda, qué versión de Docker. Solo
   lectura.
2. **Te enseño lo que encontré y el plan concreto** para ese servidor: nombre
   exacto de la red del proxy, dónde va el directorio, dónde van los respaldos.
3. **Construir las imágenes.** En el servidor o fuera, según lo que haya.
4. **Levantar el stack** y comprobar salud, sin que nadie de fuera pueda llegar
   todavía: el API no publica puerto, así que hasta que no exista el *Proxy
   Host* no es alcanzable.
5. **Tú das de alta el Proxy Host** en NPM y emites el certificado.
6. **Comprobar de punta a punta** y dar de alta el teléfono.

Entre el 4 y el 5 el stack está corriendo y no lo alcanza nadie. Es el momento
de parar si algo no cuadra.

## Cómo se deshace

```bash
cd <directorio>
docker compose -p margen down            # para y borra los contenedores
docker compose -p margen down -v         # y también los volúmenes: BORRA LA BASE
```

Ninguna de las dos toca la red del proxy, porque es externa: Docker no borra lo
que no creó.

## Sobre la clave

Que sea una clave **dedicada a esto y revocable**, no la tuya de siempre.
Cuando terminemos, bórrala del `authorized_keys` del servidor.

El motivo es concreto: todo lo que se pega en esta conversación queda escrito en
la transcripción de la sesión, en claro, en el disco de tu máquina. Una clave
que se revoca deja de importar; una que se queda, importa mientras exista ese
archivo.

Lo mismo vale para la salida de las órdenes: **no voy a imprimir el contenido de
`infra/.env`** ni de ningún archivo de secretos. Si necesito comprobar que una
variable está puesta, compruebo que está, no cuánto vale.
