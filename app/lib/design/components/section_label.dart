import 'package:flutter/widgets.dart';

import '../tokens.dart';
import '../typography.dart';

/// Separador de bloques. Versalita ancha en lugar de tarjetas: la pantalla
/// principal es una sola superficie continua, no una pila de contenedores.
class SectionLabel extends StatelessWidget {
  const SectionLabel(this.text, {super.key, this.trailing, this.color});

  final String text;
  final Widget? trailing;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.md),
      child: Row(
        children: [
          Text(
            text.toUpperCase(),
            style: Type.eyebrow(color: color ?? Tone.muted),
          ),
          const SizedBox(width: Space.md),

          // Una línea de un píxel, no un `Divider`: ese vive en Material y
          // este árbol no importa Material. El sistema visual se dibuja
          // entero sobre `widgets`, que es lo que permite que no haya un solo
          // color fuera de `Tone`.
          const Expanded(
            child: SizedBox(height: 1, child: ColoredBox(color: Tone.line)),
          ),
          if (trailing != null) ...[
            const SizedBox(width: Space.md),
            trailing!,
          ],
        ],
      ),
    );
  }
}
