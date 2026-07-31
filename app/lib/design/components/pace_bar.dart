import 'package:flutter/widgets.dart';

import '../tokens.dart';

/// Barra de ritmo de una categoría.
///
/// Muestra dos cosas a la vez: cuánto se gastó (barra) y cuánto se esperaría
/// haber gastado a estas alturas (marca vertical). Estar por delante de la
/// marca es la señal; el número absoluto casi nunca lo es.
class PaceBar extends StatelessWidget {
  const PaceBar({
    super.key,
    required this.spentFraction,
    required this.expectedFraction,
    required this.overPace,
  });

  final double spentFraction;
  final double expectedFraction;
  final bool overPace;

  @override
  Widget build(BuildContext context) {
    final fill = spentFraction.clamp(0.0, 1.0);
    final mark = expectedFraction.clamp(0.0, 1.0);
    final barColor = switch ((overPace, spentFraction >= 1.0)) {
      (_, true) => Signal.risk,
      (true, _) => Signal.caution,
      _ => Tone.bone,
    };

    return LayoutBuilder(
      builder: (context, constraints) {
        final w = constraints.maxWidth;
        return SizedBox(
          height: 6,
          child: Stack(
            children: [
              Container(
                decoration: const BoxDecoration(
                  color: Tone.line,
                  borderRadius: Radii.brSm,
                ),
              ),
              AnimatedContainer(
                duration: Motion.settle,
                curve: Motion.ease,
                width: w * fill,
                decoration: BoxDecoration(
                  color: barColor,
                  borderRadius: Radii.brSm,
                ),
              ),
              Positioned(
                left: (w * mark).clamp(0.0, w - 1.5),
                width: 1.5,
                top: -2,
                bottom: -2,
                child: const ColoredBox(color: Tone.ink),
              ),
              Positioned(
                left: (w * mark).clamp(0.0, w - 1.5),
                width: 1.5,
                child: const SizedBox(
                  height: 6,
                  child: ColoredBox(color: Tone.muted),
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}
