import 'package:flutter/widgets.dart';

import '../../core/period.dart';
import '../../domain/models.dart';
import '../tokens.dart';
import '../typography.dart';
import 'money_text.dart';

/// Fila de dinero apartado. Lo comprometido se muestra apagado a propósito:
/// ya no participa en ninguna decisión.
class ReservedRow extends StatelessWidget {
  const ReservedRow({super.key, required this.commitment});

  final Commitment commitment;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.md),
      child: Row(
        children: [
          Expanded(
            child: Text(
              commitment.label,
              style: Type.body(14),
              overflow: TextOverflow.ellipsis,
            ),
          ),
          if (commitment.dueOn != null) ...[
            Text(
              DateLabel.short(commitment.dueOn!),
              style: Type.data(11, color: Tone.faint),
            ),
            const SizedBox(width: Space.lg),
          ],
          MoneyText(
            commitment.amount,
            size: 14,
            color: Tone.muted,
          ),
        ],
      ),
    );
  }
}
