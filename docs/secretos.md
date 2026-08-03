# Secretos: dónde viven y cómo se rotan

Ninguno está en el repositorio. Todos viven en `infra/.env`, que está en
`.gitignore`, y llegan al contenedor como variables de entorno.

**Nunca escribas un secreto de ejemplo con forma de secreto de verdad.** Un
marcador que parece una credencial es lo que alguien rellena con la buena sin
pensarlo. En `infra/.env.example` todos los valores están vacíos.

---

## Los seis

| Secreto | Qué abre | Si se filtra |
|---|---|---|
| `POSTGRES_PASSWORD` | La base de datos | Nada desde fuera: la base está en una red interna sin salida y no publica puertos. Hace falta estar ya dentro del servidor |
| `AUTH_ENROLLMENT_CODE` | Dar de alta un teléfono nuevo | **Lo más grave.** Con él, cualquiera registra un dispositivo y lee tus finanzas |
| `IMAP_PASSWORD` | Tu buzón de Gmail | Grave y **de otro sistema**: no lo controlas tú, lo controla Google |
| Token de dispositivo | Todo el API | Vive en el Enclave Seguro del teléfono; no sale de ahí |
| Token del Atajo | Solo registrar efectivo | Vive en una automatización del iPhone que cualquiera con el teléfono desbloqueado puede leer. Por eso alcanza dos rutas y nada más |
| Anillo de claves de Data Protection | Firma las cookies y los tokens | Está en un volumen del servidor, no en el `.env` |

---

## Cómo se rota cada uno

### `AUTH_ENROLLMENT_CODE` — después de instalar la app

Es el que más urge y el más fácil.

```bash
openssl rand -base64 24
```

Se pone en `infra/.env` y se reinicia el API. **Los teléfonos ya dados de alta
no se ven afectados**: el código solo sirve para dar de alta uno nuevo.

Rótalo en cuanto termines de instalar la app. Vacío, el alta responde 503 y no
registra a nadie, que es el estado correcto de un servidor que ya no espera
teléfonos nuevos.

### `IMAP_PASSWORD` — desde Google, no desde aquí

Es una **contraseña de aplicación**, no la de la cuenta. Se revoca en
`myaccount.google.com` → Seguridad → Contraseñas de aplicaciones, y se genera
otra. Los pasos completos están en [`gmail.md`](gmail.md).

Revocar la vieja **antes** de poner la nueva: el worker deja de leer correo unos
minutos y no pierde nada —el buzón sigue ahí—, mientras que dejar las dos
activas es dejar una que ya considerabas comprometida.

### `POSTGRES_PASSWORD` — cambia en dos sitios

Es el único que hay que cambiar dentro y fuera, y en este orden:

```bash
# 1. Cambiarla en el motor
docker compose exec db psql -U margen -d margen \
  -c "ALTER USER margen WITH PASSWORD 'la-nueva'"

# 2. Cambiarla en infra/.env

# 3. Recrear lo que se conecta
docker compose up -d --force-recreate api worker
```

Entre el paso 1 y el 3 el API y el worker no pueden conectarse. Son segundos, y
el API responde 503 en `/health/ready` mientras tanto, que es lo correcto.

### Los tokens de dispositivo y del Atajo

Desde la app, con sesión iniciada:

```
GET    /auth/tokens          ver cuáles hay
DELETE /auth/tokens/{id}     revocar
```

La revocación es inmediata: el token deja de valer en la siguiente petición. El
servidor guarda el resumen del token y no el token, así que uno perdido no se
recupera —se revoca y se emite otro—.

### El anillo de claves

No se rota a mano. Está en el volumen `dataprotection` y lo gestiona el propio
API. **Sí hay que respaldarlo**: si se pierde, todos los tokens emitidos dejan
de validarse a la vez y hay que dar de alta el teléfono otra vez.

---

## Si algo se filtró

Por orden de urgencia:

1. **Revoca todos los tokens.** `GET /auth/tokens` y `DELETE` uno a uno.
2. **Cambia `AUTH_ENROLLMENT_CODE`**, o déjalo vacío si no vas a dar de alta
   nada: vacío, nadie puede registrarse.
3. **Revoca la contraseña de aplicación de Gmail** desde tu cuenta de Google.
4. **Cambia `POSTGRES_PASSWORD`** con los tres pasos de arriba.
5. Mira `docker compose logs api | grep -i "enrollment\|401\|403"` para ver si
   alguien lo intentó.

Si el que se filtró fue **el token del Atajo**, el daño posible es que alguien
registre gastos de efectivo falsos. Molesto y visible: aparecen en la lista y se
borran. Ese token no puede leer nada.

---

## Lo que no protege nada de esto

El `.env` está en claro en el servidor. Quien tenga acceso de root a la máquina
tiene todos los secretos, y ninguna de las medidas de arriba cambia eso.

Se dice porque conviene saber dónde está el límite: esto protege de que los
secretos acaben en el repositorio, en un registro o en la red; no protege de que
alguien entre en el servidor. Para eso está el acceso al servidor, que es otro
problema y no vive en este proyecto.
