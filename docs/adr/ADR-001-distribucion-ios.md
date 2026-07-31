# ADR-001 — Cómo llega la app al iPhone

**Estado:** requiere tu decisión antes de la Fase 5
**Fecha:** 2026-07-31

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

## Decisión

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

## Consecuencias

- Si eliges A: la Fase 5 necesita un Mac para firmar y una cuenta activa.
- Si eliges B o C: la Fase 5 cambia de contenido, no de calendario, y el
  Atajo de Siri de la Fase 6 funciona igual —habla con el API por HTTP y no
  le importa qué haya del otro lado.
- La decisión no bloquea nada antes de la Fase 5. Las fases 1 a 4 son idénticas
  en los tres caminos.
