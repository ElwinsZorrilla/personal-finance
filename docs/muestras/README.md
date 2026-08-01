# Muestras de correo del banco

**Esta carpeta está vacía y por eso la Fase 7 está bloqueada.**

El andamiaje entero está construido y probado (Fase 6, CR-007): bajar del buzón,
filtrar por remitente, guardar el original, elegir parser, detectar duplicados,
crear el movimiento y reprocesar. Lo único que falta es la clase que sabe leer
el formato de **tu** banco.

Escribirla contra un formato supuesto es trabajo que se tira entero: cada banco
pone los campos donde quiere, con las etiquetas que quiere, en el orden que
quiere.

## Qué hace falta

Un archivo por tipo, con el cuerpo del correo tal como llegó. Como mínimo estos
seis:

| Archivo | Qué correo |
|---|---|
| `compra-aprobada.txt` | Una compra normal con tarjeta |
| `compra-rechazada.txt` | Una compra que el banco declinó |
| `devolucion.txt` | Una devolución o reverso |
| `retiro.txt` | Un retiro en cajero |
| `pago-tarjeta.txt` | El pago de la tarjeta de crédito |
| `transferencia.txt` | Una transferencia enviada o recibida |

Si el banco manda HTML, guarda el HTML entero. Si manda texto plano, el texto.
Los dos si manda los dos.

## Cómo anonimizarlos

Cambia estos datos y **deja el resto exactamente igual**, incluidos los espacios,
los saltos de línea y las mayúsculas: el parser se escribe contra lo que hay, y
un espacio de más cambia una expresión regular.

- El nombre completo → `NOMBRE APELLIDO`
- Los últimos cuatro dígitos → `1234`
- Los montos → cualquier cifra, pero **conservando el formato**: si el banco
  escribe `RD$ 2,450.00`, la sustitución tiene que seguir teniendo la coma de
  miles y el punto decimal en el mismo sitio.
- El número de referencia o autorización → invéntalo, con la misma longitud y la
  misma forma.
- La dirección de correo de destino → `finanzas@ejemplo.do`

**No cambies**: el remitente del banco, el asunto, las etiquetas de los campos
(«Monto:», «Comercio:», lo que sea), el orden de las líneas ni el formato de la
fecha.

## Qué se hará con ellas

Se escribe un `IEmailParser` contra ellas y se convierten en las pruebas de ese
parser. Quedan versionadas en el repositorio: son la definición de qué formato
sabe leer el sistema, y cuando el banco cambie la plantilla, la prueba que falle
dirá exactamente qué cambió.

Como están anonimizadas, no llevan ningún dato real. Aun así, si prefieres no
versionarlas, dilo y se guardan fuera del repositorio con una nota aquí que diga
dónde.
