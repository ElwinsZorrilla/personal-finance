# El Atajo de iOS

Registrar un gasto en efectivo desde el iPhone, diciéndolo en voz alta.

> «Oye Siri, gasté 450 pesos en almuerzo»

Ese es el punto entero. El efectivo es el único gasto del que el banco no
avisa, y si registrarlo cuesta más que gastarlo, no se registra.

---

## Antes de empezar

Hace falta un **token del Atajo**. Se emite desde la app, con sesión iniciada:

```
POST /auth/shortcut-tokens
{ "name": "iPhone de trabajo" }
```

Sale una cadena larga. **Se enseña una sola vez**: el servidor guarda su
resumen, no el token, así que si se pierde no se puede recuperar y hay que
emitir otro.

Ese token:

- **Solo alcanza dos rutas**: `/transactions/cash` y `/transactions/cash/phrase`.
  Con él no se pueden leer movimientos, ni ver el panel, ni emitir otro token.
- **Es revocable** desde la app: `DELETE /auth/tokens/{id}`. Si el teléfono se
  pierde, se revoca y deja de servir en el acto.
- **Tiene límite de peticiones.** Un bucle no puede llenar la base.
- **Tiene tope de monto**: RD$100,000 por registro. No es una regla de negocio,
  es un cortafuegos.

Lo que sigue vale la pena decirlo entero: **este token vive dentro de una
automatización del teléfono, y cualquiera que abra el teléfono desbloqueado
puede leerlo.** Todo lo de arriba existe porque eso es cierto y no se puede
evitar. El peor caso tiene que ser molesto, no caro.

---

## Montar el Atajo

En la app **Atajos** del iPhone, «+» para crear uno nuevo.

### 1 · Pedir el texto

Añade la acción **«Pedir entrada»**.

- Pregunta: `¿En qué gastaste?`
- Tipo: `Texto`

Con «Oye Siri», el dictado rellena esto solo.

### 2 · Mandarlo al servidor

Añade **«Obtener contenido de URL»**.

| Campo | Valor |
|---|---|
| URL | `https://TU-DOMINIO/transactions/cash/phrase` |
| Método | `POST` |
| Encabezados | `Authorization` → `Bearer TU-TOKEN` |
| | `Content-Type` → `application/json` |
| Cuerpo | `JSON` |
| Campo `text` | la **Entrada proporcionada** del paso 1 |

`TU-DOMINIO` y `TU-TOKEN` los pones tú al montarlo. **No los escribas en ningún
archivo de este repositorio**, ni siquiera de ejemplo: un marcador con forma de
credencial es lo que alguien rellena con la de verdad sin pensarlo.

### 3 · Enseñar qué pasó

Añade **«Mostrar notificación»** con el campo `merchant` del resultado, o
—más simple y más útil— **«Mostrar alerta»** con el resultado entero.

Esto no es decoración. Estás de pie en la calle y necesitas saber si se registró
o no, porque si no se registró y crees que sí, la cifra de la pantalla miente
hasta que lo notes.

### 4 · Ponerle nombre

«Gasté». Es lo que se dice después de «Oye Siri».

---

## Qué entiende

| Lo que dices | Monto | En qué | Día |
|---|---|---|---|
| Gasté 450 pesos en almuerzo | 450.00 | almuerzo | hoy |
| 450 en almuerzo | 450.00 | almuerzo | hoy |
| Gasté RD$450 en el almuerzo | 450.00 | almuerzo | hoy |
| pagué 450 de almuerzo | 450.00 | almuerzo | hoy |
| almuerzo 450 | 450.00 | almuerzo | hoy |
| Gasté 1,200.50 en gasolina | 1200.50 | gasolina | hoy |
| $350 en pasaje | 350.00 | pasaje | hoy |
| Gasté 450 ayer en almuerzo | 450.00 | almuerzo | ayer |

La coma separa millares y el punto los decimales, como se escribe aquí.
«450.5» son 450 pesos con **50** centavos.

## Qué no entiende, y por qué

| Lo que dices | Qué responde | Por qué |
|---|---|---|
| Gasté **mil** pesos en almuerzo | No encontré el monto | El dictado de iOS ya convierte «mil» en «1000», así que el caso real llega en dígitos. Escribir medio intérprete de numerales —«mil» sí, «mil doscientos» no— enseñaría que funciona para fallar un día cualquiera |
| Gasté 20 **dólares** en el aeropuerto | Solo se registran gastos en pesos dominicanos | No son 20 pesos. No hay tasa de cambio en el sistema, y elegir una sería inventarse una cifra que después se resta del dinero disponible |
| Gasté 450 pesos | Falta en qué gastaste | Un gasto sin concepto no se puede clasificar ni reconocer al verlo en la lista |
| gasté en almuerzo | No encontré el monto | — |

**Nada de esto crea un movimiento a medias.** Si no se entiende, no se guarda y
el saldo no se toca.

---

## Qué pasa con el gasto

1. Se guarda en la cuenta de efectivo y se le resta el monto al saldo, las dos
   cosas en la misma transacción de base de datos: o las dos o ninguna.
2. Se clasifica con la misma cascada que los movimientos del banco: regla del
   usuario, historial, tabla de palabras. Lo que reconoce queda listo; lo que no,
   en Revisión.
3. La primera vez que corriges «almuerzo» a una categoría, se crea la regla. A
   partir de ahí todos los almuerzos entran ya clasificados.

**El monto nunca pasa por un modelo de lenguaje.** Lo saca una expresión regular
y se construye entero, en centavos. Un modelo que un día lea «450» donde decía
«45.0» mete un error de un orden de magnitud en una cifra que luego se resta del
líquido, y no hay ninguna señal de que ha pasado: el número es plausible, el
gasto existe, la categoría es correcta. Se descubre semanas después cuadrando a
mano.

Lo que sí puede sugerir un modelo es la **categoría**, y aun así no se aplica
sola.

---

## Si lo mandas dos veces

El Atajo reintenta cuando la red falla a medio camino. Si mandas el mismo gasto
—mismo monto, mismo concepto, mismo día— la segunda vez responde:

> Ya hay un gasto igual ese día. Si de verdad gastaste dos veces lo mismo,
> cámbiale la descripción a uno de los dos.

Y **no descuenta el saldo dos veces**. Eso es lo que de verdad hace daño: un
movimiento repetido se ve en la lista; un saldo descontado dos veces no se ve en
ninguna parte.

Si de verdad almorzaste dos veces por 450 pesos el mismo día, di «almuerzo 2» o
«cena» en el segundo.

---

## Si se pierde el teléfono

```
GET    /auth/tokens          ver cuáles hay
DELETE /auth/tokens/{id}     revocar
```

Desde la app, con sesión iniciada. La revocación es inmediata: el token deja de
valer en la siguiente petición.
