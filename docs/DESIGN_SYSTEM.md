# Sistema visual

## La tesis

La app responde una sola pregunta: **¿hasta dónde me alcanza?** Eso es una
distancia, no una proporción. Por eso no hay dona de categorías en la pantalla
principal: hay un riel de tiempo.

Todo lo demás se subordina a esa idea.

## La regla que gobierna la paleta

**El color es un sistema de aviso, no decoración.**

Un período sano se ve casi monocromo: hueso sobre azul petróleo. Cuando
aparece ámbar o rojo óxido, significa que algo pide atención. Nada más puede
introducir color.

La consecuencia es que la app se lee de un vistazo, en la fila del
supermercado: si hay color, hay algo; si no, no.

| Token | Hex | Uso |
|---|---|---|
| `Tone.ink` | `#0B1418` | Fondo. Azul petróleo, no negro: el negro puro hace vibrar las cifras grandes al hacer scroll en OLED |
| `Tone.surface` | `#121F25` | Superficie elevada |
| `Tone.surfaceRaised` | `#18282F` | Segundo nivel: campos, indicador de pestaña |
| `Tone.line` | `#23373F` | Filetes. Nunca como texto |
| `Tone.bone` | `#EDE7DC` | Texto y cifras. Hueso cálido, no blanco: menos fatiga de noche |
| `Tone.muted` | `#7E939B` | Etiquetas, fechas, secundarios |
| `Tone.faint` | `#4E656E` | Terciario, vacíos |
| `Signal.caution` | `#D9A441` | Ritmo por encima. Todavía no es un problema |
| `Signal.risk` | `#B4462F` | No alcanza, se excedió, posible duplicado |
| `Signal.credit` | `#4E8C7A` | Entra dinero: ingreso, devolución, reverso |

Contraste sobre `ink`: hueso 13.8:1, apagado 5.4:1, ámbar 8.1:1, óxido 4.6:1.
Todos por encima del mínimo AA para texto normal.

## Tipografía

Tres papeles, cada uno con un trabajo:

| Papel | Familia | Dónde |
|---|---|---|
| Display | Bricolage Grotesque | La cifra principal y los títulos de pantalla. Nada más |
| Cuerpo | Public Sans | Interfaz, etiquetas, prosa |
| Datos | IBM Plex Mono | Montos en listas, fechas, referencias, últimos cuatro dígitos |

La monoespaciada no es un gesto: alinea columnas de montos sin trucos y viene
del mismo mundo que los estados de cuenta que la app consume.

Todas las cifras llevan `FontFeature.tabularFigures()`. Un monto que pasa de
999 a 1,000 no puede desplazar la fila.

Las tres se empaquetan en `assets/fonts/`. La app se abre sin señal y la
primera cifra no puede esperar una descarga.

## El elemento firma: el riel de autonomía

```text
  ══════════●━━━━━━━━━━━┈┈┈┈┈┈┈╎
  25 jul    hoy                 24 ago
  └ vivido ─┘└─ alcance ─┘└ falta ┘
```

- **Vivido** — historia, en color de filete. No pide nada.
- **Alcance** — hasta dónde llega el dinero al ritmo actual, en hueso sólido.
- **Falta** — solo existe cuando el dinero no llega al próximo ingreso.
  Punteado, en óxido. Ese hueco es toda la advertencia que hace falta.

Es el único lugar donde vive el concepto de proyección, y traduce cuatro
números del motor de presupuesto —gasto actual, ritmo, compromisos pendientes,
fecha de ingreso— en una lectura de medio segundo.

## Movimiento

Un solo momento orquestado: al entrar, la cifra sube 10 px y aparece mientras
el riel se dibuja desde hoy hacia afuera. **Nada más se anima.**

No hay conteo ascendente de la cifra. Es el recurso más común en apps
financieras y no aporta nada: retrasa la respuesta que la persona vino a
buscar.

Todo respeta `MediaQuery.disableAnimationsOf`.

## Estructura

- **Un solo lienzo continuo.** Sin tarjetas apiladas. Los bloques se separan
  con versalitas y filetes, no con contenedores.
- **La cifra antes que su etiqueta.** Quien abre la app ya sabe qué busca;
  poner la etiqueta arriba obliga a leer una línea antes de obtener la
  respuesta.
- **Cuatro destinos, ni uno más:** cuánto tengo, qué pasó, cómo voy, qué falta
  resolver.
- **Espaciado en base 4.** Margen lateral único de 20. No hay otro gutter.

## Escritura en la interfaz

- Sentence case en todo. Nada en mayúscula sostenida salvo las versalitas de
  sección, que son un recurso estructural.
- El verbo del botón es el verbo del resultado: «Ajustar» produce «Ajustado».
- Los errores no se disculpan y no son vagos: dicen qué pasó y qué hacer.
- Una pantalla vacía en Revisión es el estado deseado. Celebra: «Nada
  pendiente». No se disculpa por estar vacía.
- Nada de jerga de implementación. La persona administra avisos, no
  suscripciones push.
