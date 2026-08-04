import 'package:flutter/widgets.dart';

import '../tokens.dart';
import '../typography.dart';

/// Un error, con un punto rojo delante.
///
/// **El color no es lo único que lo marca.** El punto, la posición y el texto
/// lo señalan igual para quien no distingue el rojo, que es la regla de este
/// sistema: el color avisa, pero nunca es lo único que avisa.
///
/// El texto viene del servidor cuando el servidor lo escribió: sus respuestas
/// de error llevan una frase pensada para una persona, y reescribirla aquí
/// sería mantener dos versiones de la misma explicación.
class ErrorNote extends StatelessWidget {
  const ErrorNote(this.mensaje, {super.key});

  final String mensaje;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      liveRegion: true,
      child: Padding(
        padding: const EdgeInsets.only(bottom: Space.sm),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              margin: const EdgeInsets.only(top: 6),
              width: 5,
              height: 5,
              decoration: const BoxDecoration(
                color: Signal.risk,
                shape: BoxShape.circle,
              ),
            ),
            const SizedBox(width: Space.sm),
            Expanded(
              child: Text(mensaje, style: Type.body(13)),
            ),
          ],
        ),
      ),
    );
  }
}
