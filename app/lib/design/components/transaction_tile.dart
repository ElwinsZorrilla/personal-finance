import 'package:flutter/material.dart';

import '../../core/period.dart';
import '../../domain/models.dart';
import '../tokens.dart';
import '../typography.dart';
import 'money_text.dart';

/// Fila de movimiento. Densa por diseño: la lista se recorre buscando algo
/// concreto, no se contempla.
class TransactionTile extends StatelessWidget {
  const TransactionTile({super.key, required this.tx, this.onTap});

  final TxRecord tx;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final isCredit = tx.isCredit;
    final dimmed = tx.status == TxStatus.rejected ||
        tx.status == TxStatus.duplicate ||
        tx.kind == TxKind.transfer ||
        tx.kind == TxKind.payment;

    final amountColor = switch (true) {
      _ when isCredit => Signal.credit,
      _ when dimmed => Tone.faint,
      _ => Tone.bone,
    };

    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: Space.md),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Flexible(
                        child: Text(
                          tx.merchant,
                          style: Type.body(
                            14,
                            weight: FontWeight.w500,
                            color: dimmed ? Tone.muted : Tone.bone,
                          ),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      if (tx.status == TxStatus.pending) ...[
                        const SizedBox(width: Space.sm),
                        const _Flag(text: 'retenido'),
                      ],
                      if (tx.status == TxStatus.needsReview) ...[
                        const SizedBox(width: Space.sm),
                        const _Flag(text: 'revisar', tone: Signal.caution),
                      ],
                    ],
                  ),
                  const SizedBox(height: 3),
                  Text(
                    '${tx.category} · ${DateLabel.short(tx.occurredAt)} · ····${tx.accountLastFour}',
                    style: Type.data(10.5, color: Tone.faint),
                    overflow: TextOverflow.ellipsis,
                  ),
                ],
              ),
            ),
            const SizedBox(width: Space.md),
            MoneyText(
              tx.amount,
              size: 14,
              weight: FontWeight.w500,
              color: amountColor,
              showSign: isCredit,
            ),
          ],
        ),
      ),
    );
  }
}

class _Flag extends StatelessWidget {
  const _Flag({required this.text, this.tone = Tone.muted});

  final String text;
  final Color tone;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 2),
      decoration: BoxDecoration(
        borderRadius: Radii.brSm,
        border: Border.all(color: tone.withValues(alpha: 0.45), width: 1),
      ),
      child: Text(
        text,
        style: Type.data(9, color: tone).copyWith(letterSpacing: 0.5),
      ),
    );
  }
}
