# Brief — Fase 9: efectivo desde el iPhone

## Qué resuelve

El efectivo es el único gasto del que el banco no avisa. Sale del cajero
—«Notificación de Retiro», que sí llega— y a partir de ahí desaparece: mil pesos
retirados se reparten en almuerzos, pasajes y un café, y ninguno de esos cargos
existe en ningún correo.

Sin registrarlos, la respuesta a *«¿cuánto puedo gastar?»* está inflada por todo
el efectivo que ya se gastó. Y registrarlos tiene que costar menos que
gastarlos, o no se hace.

## Qué entra

- **Interpretar la frase.** «Gasté 450 pesos en almuerzo» se convierte en monto,
  descripción y día.
- **Clasificar lo que salga**, con la misma cascada de la Fase 8.
- **El Atajo documentado** en `docs/atajo.md`, con los pasos exactos.

## Qué ya estaba

El endpoint `POST /transactions/cash`, el token de alcance `cash:create`,
revocable y con límite de peticiones, y el descuento del saldo dentro de una
transacción de base de datos. Todo eso es de las Fases 2 y 4. Aquí se comprueba
que sigue siendo cierto y se le añade la puerta de la frase.

## La decisión de la fase

### El modelo no toca el monto. Ni aquí ni en ninguna parte

Es la tentación evidente: mandarle «Gasté 450 pesos en almuerzo» a un modelo y
que devuelva `{monto: 450, categoría: "Comida"}`. Sería menos código y
funcionaría casi siempre.

**Casi siempre no sirve para dinero.** Un modelo que un día lee «450» donde
decía «45.0» mete un error de un orden de magnitud en una cifra que luego se
resta del líquido, y no hay ninguna señal de que ha pasado: el número es
plausible, el gasto existe, la categoría es correcta. Se descubre semanas
después, cuadrando a mano.

Así que el reparto es el mismo de la Fase 8, y por el mismo motivo:

| Qué | Quién |
|---|---|
| El monto | Una expresión regular. Determinista, con prueba |
| El día | Palabras exactas: «hoy», «ayer». Determinista |
| La moneda | Palabras exactas. Lo que no sea peso dominicano **se rechaza** |
| La descripción | Lo que queda de la frase, sin tocar |
| **La categoría** | La cascada de la Fase 8, donde el modelo sí puede sugerir |

Y la categoría que sugiera el modelo sigue sin aplicarse sola, exactamente como
en la Fase 8.

### Números en dígitos, no en palabras

«Gasté mil pesos» no se interpreta. La frase se rechaza y se dice por qué.

Suena limitado y es la decisión correcta: el dictado de iOS **ya convierte los
números hablados a dígitos** —se dice «cuatrocientos cincuenta» y escribe
«450»—, así que el caso real llega en dígitos. Escribir un intérprete de
numerales en castellano añadiría un camino que casi nadie recorre y que puede
equivocarse en la cifra, que es justo lo que esta fase evita.

Media implementación sería peor que ninguna: si «mil» funciona y «mil
doscientos» no, se aprende que funciona y un día falla.

### Lo que no sea peso dominicano se rechaza

«Gasté 20 dólares en el aeropuerto» **no se guarda como 20 pesos**. La cuenta de
efectivo lleva pesos, no hay tasa de cambio en el sistema y elegir una sería
inventarse una cifra. Se rechaza diciendo exactamente eso.

## Los siete riesgos, antes de escribir

**1 · Aritmética de dinero.** El monto se construye entero desde los dígitos:
parte entera por cien más los centavos. Sin `decimal`, sin `double`, sin
`Parse` de coma flotante. Separador de millares `,` y decimal `.`, que es como
se escribe en la República Dominicana.

**2 · Idempotencia.** El Atajo reintenta cuando la red falla a medio camino. La
huella —cuenta, día local, monto y descripción normalizada— ya lo cubre desde la
Fase 4; aquí se comprueba con la frase entrando por la puerta nueva.

**3 · El modelo no toca cifras.** La decisión de arriba.

**4 · Fallo cerrado.** Sin monto, sin descripción, con moneda extranjera o con
un monto por encima del tope: **no se crea nada** y se dice qué falta. Nunca se
asume un valor.

**5 · Zona horaria.** «Hoy» y «ayer» son días locales. Un gasto de las once de
la noche registrado como «hoy» tiene que caer en el día que ve el usuario, no en
el siguiente en UTC.

**6 · Superficie expuesta.** El token del Atajo vive en una automatización del
teléfono que cualquiera con el teléfono desbloqueado puede abrir. Alcance de una
sola ruta, revocable, con límite de peticiones y con tope de monto. El texto de
la frase entra en la base y sale en pantalla: tope de longitud.

**7 · Accesibilidad.** No aplica: el Atajo es de iOS y la pantalla no cambia.

## Criterio de cierre

- La frase se interpreta en sus formas reales, con prueba de cada una.
- Lo que no se entiende no crea nada y dice qué falta.
- El monto nunca pasa por coma flotante.
- Lo que entra por la frase se clasifica con la cascada de la Fase 8.
- `docs/atajo.md` con los pasos exactos y sin un solo secreto dentro.
- Las dos compuertas 1 en verde.
