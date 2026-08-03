# ADR-001 — Cómo llega la app al iPhone

**Estado:** **DECIDIDO — opción C**, el 2026-08-03
**Fecha:** 2026-07-31 · decidido el 2026-08-03

## Contexto

El brief pide dos cosas que, juntas, no se sostienen:

1. App en Flutter en el iPhone.
2. Sin cuenta de pago de Apple Developer.
3. Avisos push solo cuando algo requiere atención (§20).

Con un Apple ID gratuito, Apple emite un perfil de aprovisionamiento
**personal** con estas condiciones: caduca a los 7 días y hay que volver a
firmar, hay un tope de 3 apps firmadas a la vez, y **no hay notificaciones
push**.

El tercer punto es el que decide. Una app financiera que solo avisa cuando
algo va mal, y que no puede avisar, es una app que hay que abrir por si acaso
— exactamente lo que el proyecto quiere eliminar.

## Opciones

| | Costo | Push | Mantenimiento | Requiere Mac |
|---|---|---|---|---|
| **A.** Apple Developer Program + firma ad-hoc | 99 USD/año | Sí (APNs) | Refirmar 1 vez al año | Sí, para firmar |
| **B.** PWA como en el brief original | 0 | Sí, Web Push en iOS 16.4+ instalada en pantalla de inicio | Ninguno | No |
| **C.** Flutter Web empaquetado como PWA | 0 | Sí, mismo mecanismo que B | Ninguno | No |
| **D.** Apple ID gratuito + Flutter nativo | 0 | **No** | Refirmar cada 7 días | Sí |

La D queda descartada: incumple un requisito funcional y añade una tarea
semanal permanente.

## Decisión (2026-08-03)

**Opción C: Flutter Web empaquetado como PWA.** Sin programa de Apple.

La recomendación original era la A. La decisión del humano fue no pagar, y al
medir lo que la propuesta original descartaba sin medir, la C resultó ser mejor
que la B.

### Lo que decía el ADR sobre la C, y lo que salió al medirlo

Decía: «el paquete inicial de Flutter Web pesa varios megabytes […] para una app
que se abre veinte veces al día en sesiones de tres segundos, ese arranque se
nota».

Medido sobre `flutter build web --release`, lo que viaja comprimido:

| | gzip |
|---|---|
| `main.dart.js` | 617 KB |
| `skwasm.wasm` | 1 501 KB |
| `flutter.js`, arranque y fuentes | 31 KB |
| **Primera carga** | **~2,1 MB** |

El número era correcto. **La conclusión no.** Esos 2,1 MB se descargan **una vez
por versión**, no en cada apertura: una PWA instalada en la pantalla de inicio
los cachea con su service worker. Las veinte aperturas diarias de tres segundos
salen de la caché y no tocan la red.

La objeción valía para una página web que se visita; no vale para una app
instalada.

### Por qué no la B

La B —una PWA escrita a mano— arrancaría en unas decenas de kilobytes, y cuesta
**reescribir `app/` entero**: 93 pruebas, la aritmética de centavos, el formato
de dinero que ya costó un Blocker en CR-005 porque ICU agrupa `RD$ 12.000` al
estilo europeo, las fechas que costaron otro, los DTO, los repositorios y la
caché.

Rehacer eso en otro lenguaje no es trasladar código: es volver a cometer los
mismos errores de dinero, porque son errores que se cometen **al escribir**, no
al copiar.

Dos megabytes cacheados cuestan menos que eso.

---

### La propuesta original, para el registro

**Opción A**, salvo que decidas lo contrario.

99 USD al año es menor que el costo de refirmar cada semana, y es la única vía
que da a la vez app nativa, push y una app que dura un año entre firmas.

Si el costo no se justifica para un proyecto de un solo usuario, la **opción B**
es la alternativa correcta y era la del brief original. En ese caso el trabajo
de diseño hecho aquí no se pierde: los tokens de `lib/design/tokens.dart` son
valores planos y se trasladan a CSS sin reinterpretación.

La **opción C** parece el mejor de dos mundos y no lo es: el paquete inicial de
Flutter Web pesa varios megabytes, el desplazamiento no se siente nativo y el
texto se rasteriza distinto. Para una app que se abre veinte veces al día en
sesiones de tres segundos, ese arranque se nota.

## Consecuencias de la C

- **No hace falta Mac, ni cuenta de Apple, ni refirmar nada.** Nunca.
- La Fase 11 deja de ser «empaquetado iOS» y pasa a ser **empaquetado PWA**:
  manifiesto, service worker, iconos y comportamiento sin red.
- **Los avisos push funcionan** desde iOS 16.4, con la app instalada en la
  pantalla de inicio y con permiso concedido. Era el requisito que descartaba la
  D y el que empujaba hacia la A.
- El Atajo de Siri de la Fase 9 funciona igual: habla con el API por HTTP y no
  le importa qué haya del otro lado.
- **El despliegue cambia**: hay algo estático que servir, así que la Fase 11 va
  antes que el arranque del stack. Desplegar sin la PWA y volver a desplegar con
  ella es tocar dos veces un servidor que tiene otras cosas en producción.
- Nada de lo hecho en las fases 1 a 10 se pierde ni se toca. El cliente Flutter
  se compila para otra plataforma; el servidor no se entera.
