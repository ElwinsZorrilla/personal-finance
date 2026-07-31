import 'package:flutter/widgets.dart';

import '../../core/money.dart';
import '../tokens.dart';
import '../typography.dart';

/// Cifra principal: primero el número, después qué significa.
///
/// El orden invertido es deliberado. Quien abre esta app ya sabe qué está
/// buscando; poner la etiqueta arriba obliga a leer una línea antes de obtener
/// la respuesta. El número entra primero y la etiqueta lo explica.
class SafeToSpend extends StatelessWidget {
  const SafeToSpend({
    super.key,
    required this.amount,
    required this.caption,
    this.tone,
  });

  final Money amount;
  final String caption;
  final Color? tone;

  @override
  Widget build(BuildContext context) {
    final color = tone ?? Tone.bone;
    // El tamaño cede ante montos largos antes que truncar: RD$ 1,240,000 debe
    // caber en un iPhone SE sin puntos suspensivos.
    final digits = MoneyFormat.bare(amount.abs);
    final size = switch (digits.length) {
      <= 6 => 60.0,
      7 => 54.0,
      8 => 46.0,
      _ => 40.0,
    };

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text.rich(
          TextSpan(
            children: [
              TextSpan(
                text: 'RD\$\u2009',
                style: Type.display(size * 0.42)
                    .copyWith(color: color.withValues(alpha: 0.5)),
              ),
              TextSpan(
                text: amount.isNegative ? '-$digits' : digits,
                style: Type.display(size, weight: FontWeight.w700)
                    .copyWith(color: color),
              ),
            ],
          ),
          maxLines: 1,
          semanticsLabel: '${MoneyFormat.exact(amount)}, $caption',
        ),
        const SizedBox(height: Space.sm),
        Text(
          caption,
          style: Type.body(14, color: Tone.muted),
          maxLines: 2,
        ),
      ],
    );
  }
}
