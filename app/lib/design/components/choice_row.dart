import 'package:flutter/widgets.dart';

import '../tokens.dart';
import '../typography.dart';
import 'pressable.dart';

/// Elegir entre pocas opciones, todas a la vista.
///
/// **Sustituye a un desplegable.** Un desplegable esconde las opciones detrás
/// de un toque y abre una hoja del sistema encima de la pantalla; con cuatro
/// opciones cortas eso es un toque de más y un salto de contexto para nada.
/// Aquí se ven las cuatro y se elige con el pulgar.
class ChoiceRow<T> extends StatelessWidget {
  const ChoiceRow({
    super.key,
    required this.label,
    required this.options,
    required this.selected,
    required this.onChanged,
    this.enabled = true,
  });

  final String label;

  /// Valor y su etiqueta, en el orden en que se enseñan.
  final List<(T, String)> options;

  final T selected;
  final ValueChanged<T> onChanged;
  final bool enabled;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label.toUpperCase(), style: Type.eyebrow()),
          const SizedBox(height: Space.md),
          Wrap(
            spacing: Space.sm,
            runSpacing: Space.sm,
            children: [
              for (final (valor, etiqueta) in options)
                _Pill(
                  etiqueta: etiqueta,
                  activo: valor == selected,
                  enabled: enabled,
                  onTap: () => onChanged(valor),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Pill extends StatelessWidget {
  const _Pill({
    required this.etiqueta,
    required this.activo,
    required this.enabled,
    required this.onTap,
  });

  final String etiqueta;
  final bool activo;
  final bool enabled;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      selected: activo,
      child: Pressable(
        onTap: enabled ? onTap : null,
        semanticLabel: etiqueta,
        child: AnimatedContainer(
          duration: Motion.quick,
          curve: Motion.ease,
          padding: const EdgeInsets.symmetric(
            horizontal: Space.lg,
            vertical: Space.md,
          ),
          decoration: BoxDecoration(
            color: activo ? Tone.surfaceRaised : Tone.ink,
            borderRadius: Radii.brSm,
            // La opción elegida se distingue por **borde y peso**, no solo por
            // un tono de fondo: dos grises oscuros contiguos no son diferencia
            // suficiente para quien mira la pantalla al sol.
            border: Border.all(color: activo ? Tone.bone : Tone.line),
          ),
          child: Text(
            etiqueta,
            style: Type.body(
              14,
              weight: activo ? FontWeight.w600 : FontWeight.w400,
              color: activo ? Tone.bone : Tone.muted,
            ),
          ),
        ),
      ),
    );
  }
}
