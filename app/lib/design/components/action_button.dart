import 'package:flutter/widgets.dart';

import '../tokens.dart';
import '../typography.dart';
import 'pressable.dart';

/// El botón de la app.
///
/// Hueso sobre tinta, esquina pequeña, altura 52. **Alto de verdad**: 52 puntos
/// es lo que hace que se pueda pulsar con el pulgar sin mirar, y lo que
/// distingue un botón de app de un enlace de página.
///
/// Mientras trabaja no cambia de tamaño ni sale un círculo girando encima:
/// cambia el texto y se apaga. Un indicador que aparece desplaza todo lo que
/// tiene debajo, y el salto es peor que la espera.
class ActionButton extends StatelessWidget {
  const ActionButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.busyLabel,
    this.busy = false,
    this.quiet = false,
  });

  final String label;
  final VoidCallback? onPressed;

  /// Qué dice mientras trabaja. Sin esto se queda con el texto de reposo, que
  /// invita a pulsar otra vez.
  final String? busyLabel;

  final bool busy;

  /// Secundario: sin relleno, solo el texto. Para lo que no es la acción
  /// principal de la pantalla.
  final bool quiet;

  @override
  Widget build(BuildContext context) {
    final activo = onPressed != null && !busy;
    final texto = busy ? (busyLabel ?? label) : label;

    if (quiet) {
      return Pressable(
        onTap: activo ? onPressed : null,
        semanticLabel: texto,
        child: Container(
          height: 44,
          alignment: Alignment.center,
          child: Text(
            texto,
            style: Type.body(
              14,
              weight: FontWeight.w600,
              color: activo ? Tone.bone : Tone.faint,
            ),
          ),
        ),
      );
    }

    return Pressable(
      onTap: activo ? onPressed : null,
      semanticLabel: texto,
      child: AnimatedContainer(
        duration: Motion.quick,
        curve: Motion.ease,
        height: 52,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: activo ? Tone.bone : Tone.surfaceRaised,
          borderRadius: Radii.brSm,
        ),
        child: Text(
          texto,
          style: Type.body(
            15,
            weight: FontWeight.w600,
            color: activo ? Tone.ink : Tone.muted,
          ),
        ),
      ),
    );
  }
}
