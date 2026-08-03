# Brief — Fase 8: clasificación y anomalías

## Qué resuelve

La Fase 7 dejó los movimientos entrando desde el correo con **`CategoryId` en
nulo y confianza cero**. Todos nacen en Revisión. Con dieciséis muestras eso se
arregla a mano en un minuto; con un año de compras son mil pantallas de
Revisión y la aplicación deja de servir para lo único que hace.

Esta fase pone quién decide la categoría, y qué hacer cuando el gasto no se
parece a lo de siempre.

## Qué entra

### La cascada

En este orden, y el primero que responda gana:

| # | Origen | Qué es |
|---|---|---|
| 1 | Regla exacta del usuario | «Este comercio es esta categoría». Lo dijo una persona |
| 2 | Regla por patrón | Contiene o expresión regular, también del usuario |
| 3 | Historial del comercio | Las diez últimas veces se clasificó así |
| 4 | Clasificador local | Tabla de palabras. Sin red, sin coste, determinista |
| 5 | Modelo | Sugiere; **nunca decide** |
| 6 | Revisión humana | Nadie respondió. Es un resultado, no un fallo |

### La corrección que enseña

Corregir la categoría de un movimiento crea una **regla exacta del usuario**
sobre el comercio normalizado. La siguiente compra en el mismo sitio se
resuelve en el escalón 1 y no llega al modelo. Corregir dos veces el mismo
comercio **actualiza** la regla; no apila una segunda.

### Las anomalías

- **Rango normal por comercio.** Con qué se compara un cargo para decir que es
  raro.
- **Gasto inusual**: fuera del rango normal de su comercio.
- **Suscripción que subió de precio**: mismo comercio recurrente, importe
  distinto al anterior.
- **Factura recurrente que no llegó**: vencida y sin cargo que la explique.

Cada una produce una alerta con clave de deduplicación: mirar el reloj diez
veces no genera diez alertas.

## Qué no entra

- **Categorías nuevas.** El clasificador elige entre las que existen; no
  inventa. Crear categorías es del usuario.
- **La pantalla.** El servidor calcula y expone; la Fase 9 y siguientes pintan.
- **Un modelo de verdad conectado.** Entra el **puerto** y la barrera que le
  impide ver cifras. Qué proveedor se enchufa es una decisión abierta, y
  enchufarlo antes de decidirla sería elegirla a escondidas.

## Las decisiones que hay que tomar

### 1 · El modelo sugiere y no decide

El riesgo 3 del `LOOP.md` dice que el modelo no toca cifras. Aquí se hace más
fuerte: **una sugerencia del modelo nunca clasifica sola.** El movimiento queda
en Revisión con la categoría propuesta puesta, y una persona confirma.

No es desconfianza decorativa. Un modelo que se equivoca en la categoría mueve
dinero de un presupuesto a otro, y el «cuánto puedo gastar» que sale en
pantalla queda mal por una razón que nadie ve. El escalón 5 existe para que la
Revisión llegue con la respuesta escrita y se confirme con un toque, que es
distinto de elegir entre veinte categorías.

La barrera es de tipos: el puerto recibe **el nombre normalizado del comercio y
la lista de categorías**. No recibe `Money`, ni fecha, ni cuenta, ni saldo. No
hay manera de pasarle una cifra sin cambiar la firma, y cambiarla se ve en la
revisión.

### 2 · Mediana y no media

El rango normal necesita centro y dispersión. La media y la desviación típica
se descartan por dos motivos, y el segundo importa más:

1. La desviación típica lleva raíz cuadrada, y eso es coma flotante en el
   camino del dinero.
2. **Con pocas muestras la media miente.** Cinco compras de 300 pesos y una de
   40 000 dan una media de 6 900: ni una sola compra se parece a eso, y el
   rango que sale de ahí no marca como raro justamente el cargo raro.

Se usa **mediana y desviación absoluta mediana**, las dos con aritmética
entera, sobre una lista ordenada. Un valor extremo no mueve la mediana.

### 3 · Sin muestras no hay rango

Menos de cinco cargos de un comercio: no hay rango, y no se marca nada. Es
`Outcome.Insufficient`, no un rango ancho por si acaso. Un rango inventado con
dos muestras marca como raro el tercer cargo normal, y una alerta que se
equivoca dos veces se deja de leer.

## Los siete riesgos, antes de escribir

**1 · Aritmética de dinero.** Mediana, desviación y variación porcentual, todo
entero. La variación de precio se expresa en **puntos básicos** —enteros— y no
en porcentaje con coma.

**2 · Idempotencia.** Clasificar dos veces el mismo movimiento no crea dos
reglas. Detectar la misma anomalía dos veces no crea dos alertas: `DedupeKey`
con índice único sobre las no resueltas.

**3 · El modelo no toca cifras.** Decisión 1. Es la barrera principal de la
fase.

**4 · Fallo cerrado.** Por debajo del umbral de confianza no se asigna
categoría: se deja en Revisión. Sin muestras no hay rango. Una regla con
expresión regular que tarde demasiado **no casa**, no revienta.

**5 · Zona horaria.** «Vencida» es una fecha local. Una factura que vence el 31
no está vencida a las 20:00 del 31 en Santo Domingo aunque en UTC ya sea 1.

**6 · Superficie expuesta.** Las reglas por expresión regular son **texto del
usuario compilado a expresión regular**. Van con tope de longitud y con tiempo
límite, y el tiempo límite cuenta como «no casa».

**7 · Accesibilidad.** No aplica: esta fase no pinta nada.

## Criterio de cierre

- La cascada resuelve por los seis escalones, con prueba de cada uno y del
  orden entre ellos.
- Una corrección crea la regla y la segunda corrección del mismo comercio no
  crea la segunda regla.
- Las cuatro anomalías, con prueba, y ninguna se repite al volver a mirar.
- El modelo no puede recibir una cifra, y hay una prueba que lo dice.
- Cobertura del motor de clasificación por encima del 80 %.
- Las dos compuertas 1 en verde.
