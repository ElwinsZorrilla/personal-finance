# Muestras de correo, un banco a la vez

Aquí van los correos de notificación de **cada** banco, ya anonimizados. Son lo
único contra lo que se puede escribir un parser: cada banco pone los campos
donde quiere, con las etiquetas que quiere, en el orden que quiere, y escribir
contra un formato supuesto es trabajo que se tira entero.

Los archivos `.txt` de esta carpeta están en `.gitignore`. **Se leen antes de
versionarlos**: el redactor quita lo que sabe reconocer, y tu banco puede poner
algo que no previó.

## Cómo se llaman

```
<banco>-<tipo>.txt        popularenlinea-compra-aprobada.txt
<banco>-<tipo>-<n>.txt    popularenlinea-compra-aprobada-2.txt
```

El banco delante **no es decoración**. Con varios bancos en el mismo buzón, un
archivo llamado `compra-aprobada-2.txt` no dice de cuál es, y como el formato de
cada uno es distinto, la muestra deja de servir para escribir ningún parser.

El nombre sale del dominio del remitente: `notificaciones@popularenlinea.com`
da `popularenlinea`, y `alertas@bhd.com.do` da `bhd`.

## Qué hace falta de cada banco

| Tipo | Qué correo |
|---|---|
| `compra-aprobada` | Una compra normal con tarjeta |
| `compra-rechazada` | Una compra que el banco declinó |
| `devolucion` | Una devolución o reverso |
| `retiro` | Un retiro en cajero |
| `pago-tarjeta` | El pago de la tarjeta de crédito |
| `transferencia` | Una transferencia enviada o recibida |
| `deposito` | Un depósito en cajero o ventanilla |

No hacen falta los siete para empezar: con lo que haya se escribe lo que se
pueda, y lo que no se reconozca queda en Revisión sin crear nada. Lo que **no**
se puede hacer es inventarse el formato de los que faltan.

Si el banco manda HTML, se guarda el HTML entero. Si manda texto plano, el
texto. Los dos si manda los dos.

## Cómo se capturan

```bash
dotnet run --project src/Margen.Worker -- --capturar-muestras
```

Abre el buzón en **solo lectura**, redacta lo personal y escribe aquí. No toca
la base de datos ni marca nada como leído. Los pasos completos, incluida la
contraseña de aplicación de Google, están en [`../gmail.md`](../gmail.md).

**Para varios bancos, pon todos los remitentes en `IMAP_ALLOWED_SENDERS`,
separados por comas.** La cuota de muestras es por banco y por tipo, así que un
banco con muchos correos no deja a los demás sin capturar.

## Cómo se anonimizan

Lo hace el redactor solo. Si lo revisas a mano, cambia estos datos y **deja el
resto exactamente igual**, incluidos los espacios, los saltos de línea y las
mayúsculas: el parser se escribe contra lo que hay, y un espacio de más cambia
una expresión regular.

- El nombre completo → `NOMBRE APELLIDO`
- Los últimos cuatro dígitos → `1234`
- Los montos → cualquier cifra, **conservando el formato**: si el banco escribe
  `RD$ 2,450.00`, la sustitución tiene que seguir teniendo la coma de millares y
  los dos decimales en el mismo sitio
- Las fechas → cualquier fecha, **conservando el formato**. Esto importa más de
  lo que parece: el Banco Popular escribe la fecha de tres maneras distintas en
  cuatro plantillas —`26/07/2026`, `12/6/2026` y `20260618`—
- El número de referencia o autorización → inventado, con la misma longitud y la
  misma forma
- La dirección de correo de destino → `finanzas@ejemplo.do`

**No se cambian**: el remitente del banco, el asunto, las etiquetas de los
campos, el orden de las líneas ni el formato de la fecha.

### Redactar de más rompe la muestra sin proteger a nadie

En la Fase 7, la regla que borraba cadenas largas de dígitos se llevó por
delante la fecha del depósito —que el Popular escribe sin separadores— y la
dejó en `00000000`. La muestra pasó a mentir sobre su propio formato, y un
parser escrito contra ella habría fallado con el correo real. Hizo falta otra
captura para arreglarlo.

## Cuáles hay ahora

| Banco | Estado |
|---|---|
| `popularenlinea` — Banco Popular Dominicano | 16 muestras, parser escrito y aprobado (Fase 7, CR-008). Faltan compra rechazada, devolución y pago de tarjeta: no aparecieron en 209 correos |
| Los demás | **Pendientes de capturar.** Añade sus remitentes a `IMAP_ALLOWED_SENDERS` y vuelve a correr la captura |

Cada banco nuevo es un `IEmailParser` nuevo. El registro los prueba en orden y
elige por remitente, así que añadir uno no toca a los que ya funcionan.
