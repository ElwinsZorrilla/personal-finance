# Conectar Gmail

Para capturar las muestras del banco y, después, para que el worker lea el
buzón en producción.

---

## Antes de nada: lee esto

**Una contraseña de aplicación de Google da acceso a todo el buzón.** No se
puede limitar a una etiqueta, a un remitente ni a solo lectura. Quien la tenga
puede leer todo el correo de esa cuenta.

Eso tiene dos consecuencias prácticas:

1. **Usa una cuenta dedicada, no tu Gmail principal.** Crea una cuenta nueva y
   configura en la principal un reenvío automático de los correos del banco
   hacia ella. Así, la credencial que acaba en un archivo de configuración —y
   más tarde en un servidor— solo abre un buzón que contiene notificaciones
   bancarias, no diez años de tu vida.

2. **Si aun así usas la principal**, sabe que estás poniendo esa llave en un
   `.env` y en un contenedor. Es tu decisión y es defendible para una cuenta
   personal en un servidor tuyo; pero es una decisión, no un detalle.

La herramienta de captura abre el buzón en **solo lectura**: no marca nada como
leído, no mueve nada y no borra nada. Eso limita lo que hace la herramienta, no
lo que permite la credencial.

---

## 1. Activa la verificación en dos pasos

Sin ella Google no deja crear contraseñas de aplicación.

<https://myaccount.google.com/signinoptions/twosv>

## 2. Crea una contraseña de aplicación

<https://myaccount.google.com/apppasswords>

Nómbrala `margen`. Google devuelve dieciséis letras en cuatro grupos. **Cópiala
en ese momento**: no se vuelve a mostrar.

Va sin espacios en la configuración, aunque Google la enseñe con ellos.

## 3. Comprueba que IMAP está activo

Gmail → Configuración → **Reenvío y correo POP/IMAP** → *Habilitar IMAP*.

En cuentas creadas hace poco ya viene activo.

## 4. Escribe la configuración

Copia `infra/.env.example` a `infra/.env` —que está en `.gitignore` y no se sube
nunca— y rellena:

```bash
IMAP_HOST=imap.gmail.com
IMAP_PORT=993
IMAP_USER=tucuenta@gmail.com
IMAP_PASSWORD=lascatorceletrassinespacios

# Los remitentes del banco. Sin esta lista no se lee nada.
# Vale la dirección completa o el dominio.
IMAP_ALLOWED_SENDERS=alertas@popularenlinea.com,notificaciones@bhdleon.com.do

# Tu nombre, para que el redactor lo quite de las muestras.
MUESTRAS_DATOS_PERSONALES=Nombre Apellido,Otro Nombre
```

Para saber qué poner en `IMAP_ALLOWED_SENDERS`: abre uno de esos correos en
Gmail, pulsa los tres puntos → *Mostrar original*, y mira la línea `From:`.

---

## 5. Captura las muestras

Desde la raíz del repositorio, en tu máquina:

```bash
export IMAP_HOST=imap.gmail.com
export IMAP_PORT=993
export IMAP_USER=tucuenta@gmail.com
export IMAP_PASSWORD=lascatorceletrassinespacios
export IMAP_ALLOWED_SENDERS=alertas@tubanco.com
export Muestras__DatosPersonales="Nombre Apellido"

dotnet run --project src/Margen.Worker -- --capturar-muestras
```

En PowerShell, `$env:IMAP_HOST = "imap.gmail.com"` y así con cada una.

Escribe en `docs/muestras/` un archivo por tipo de correo, con el nombre puesto
según el asunto: `compra-aprobada.txt`, `devolucion.txt`, `retiro.txt`…

### Qué hace y qué no

| Hace | No hace |
|---|---|
| Abre el buzón en solo lectura | No marca leídos, no mueve, no borra |
| Busca solo los remitentes de la lista | No mira el resto del buzón |
| Pasa todo por el redactor antes de escribir | No guarda ningún original |
| Escribe archivos de texto | No escribe una fila en la base de datos |

### Qué quita el redactor

Los nombres que le des, las direcciones de correo, los cuatro dígitos de la
tarjeta, los montos, las referencias y cualquier cadena de ocho dígitos
seguidos.

Y **conserva** las etiquetas de los campos, el orden de las líneas, los
espacios, el formato de las fechas y el nombre del comercio. Eso es a propósito:
el parser se escribe contra el formato, y una muestra con la fecha destrozada no
sirve para escribir nada. Está probado con 16 casos en `RedactorTests`.

---

## 6. Léelos antes de hacer commit

**Esto no es opcional.** El redactor quita lo que sabe reconocer, y tu banco
puede poner algo que no previó: un saludo con tu nombre escrito distinto, un
número de socio, una dirección postal.

Abre cada archivo y míralo. Si encuentras algo que sobró, dímelo y añado la
regla —y su prueba— antes de que ninguno de esos archivos entre en un commit.

Mientras tanto, `docs/muestras/*.txt` está en `.gitignore`: los archivos
capturados **no se suben solos**. Se quita esa línea cuando estén revisados.

---

## Después, en producción

Las mismas variables las consume el worker en el servidor, por `infra/.env` y el
compose. La diferencia es que ahí abre el buzón en lectura y escritura, porque
sí marca como leído lo que ya procesó.

## Si algo falla

| Síntoma | Causa casi siempre |
|---|---|
| `AuthenticationException` | La contraseña normal en vez de la de aplicación, o espacios sin quitar |
| Conecta pero no encuentra nada | El remitente de `IMAP_ALLOWED_SENDERS` no coincide con el `From:` real |
| `No se pudo conectar` | IMAP desactivado en la configuración de Gmail |

Cuando termines, **borra la contraseña de aplicación** en
<https://myaccount.google.com/apppasswords> si no vas a desplegar todavía. Crear
otra cuesta un minuto.
