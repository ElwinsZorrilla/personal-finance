import 'package:flutter/widgets.dart';

import '../../core/money.dart';
import '../tokens.dart';
import '../typography.dart';

/// Monto con el símbolo en menor jerarquía que la cifra.
///
/// `RD$` es contexto, no información: quien usa la app ya sabe en qué moneda
/// vive. Se imprime más pequeño y apagado para que el ojo caiga en el número.
class MoneyText extends StatelessWidget {
  const MoneyText(
    this.amount, {
    super.key,
    this.size = 15,
    this.weight = FontWeight.w500,
    this.color,
    this.mono = true,
    this.showSign = false,
  });

  final Money amount;
  final double size;
  final FontWeight weight;
  final Color? color;

  /// Monoespaciada en listas (alinea columnas). Proporcional en prosa.
  final bool mono;

  /// Muestra `+` en créditos. Solo donde el signo aporta: devoluciones,
  /// ingresos, ajustes.
  final bool showSign;

  @override
  Widget build(BuildContext context) {
    final resolved = color ?? Tone.bone;
    final digits = MoneyFormat.bare(amount.abs);
    final sign = amount.isNegative
        ? '-'
        : showSign
            ? '+'
            : '';

    final numberStyle = mono
        ? Type.data(size, weight: weight, color: resolved)
        : Type.display(size).copyWith(color: resolved, fontWeight: weight);

    return Text.rich(
      TextSpan(
        children: [
          TextSpan(
            text: 'RD\$\u2009',
            style: Type.data(
              size * 0.62,
              weight: FontWeight.w500,
              color: resolved.withValues(alpha: 0.55),
            ),
          ),
          TextSpan(text: '$sign$digits', style: numberStyle),
        ],
      ),
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
      semanticsLabel: MoneyFormat.exact(amount),
    );
  }
}
