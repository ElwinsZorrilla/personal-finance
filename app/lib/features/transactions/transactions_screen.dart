import 'package:flutter/material.dart';

import '../../core/period.dart';
import '../../design/components/section_label.dart';
import '../../design/components/transaction_tile.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';
import '../../domain/models.dart';

/// Movimientos agrupados por día. La agrupación es la única jerarquía: el
/// resto es una lista plana y densa que se recorre con el pulgar.
class TransactionsScreen extends StatelessWidget {
  const TransactionsScreen({super.key, required this.transactions});

  final List<TxRecord> transactions;

  @override
  Widget build(BuildContext context) {
    final grouped = <DateTime, List<TxRecord>>{};
    for (final tx in transactions) {
      final day = DateTime(
        tx.occurredAt.year,
        tx.occurredAt.month,
        tx.occurredAt.day,
      );
      grouped.putIfAbsent(day, () => []).add(tx);
    }
    final days = grouped.keys.toList()..sort((a, b) => b.compareTo(a));

    if (days.isEmpty) {
      return const _Empty(
        title: 'Todavía no hay movimientos',
        body: 'Cuando llegue la primera notificación del banco aparecerá aquí.',
      );
    }

    return CustomScrollView(
      slivers: [
        SliverPadding(
          padding: EdgeInsets.only(
            left: Space.gutter,
            right: Space.gutter,
            top: MediaQuery.paddingOf(context).top + Space.lg,
            bottom: Space.xxxl,
          ),
          sliver: SliverList.builder(
            itemCount: days.length,
            itemBuilder: (context, i) {
              final day = days[i];
              return Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if (i > 0) const SizedBox(height: Space.xl),
                  SectionLabel(DateLabel.weekday(day)),
                  for (final tx in grouped[day]!) TransactionTile(tx: tx),
                ],
              );
            },
          ),
        ),
      ],
    );
  }
}

class _Empty extends StatelessWidget {
  const _Empty({required this.title, required this.body});

  final String title;
  final String body;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: Space.xxl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(title, style: Type.body(16, weight: FontWeight.w600)),
            const SizedBox(height: Space.sm),
            Text(
              body,
              textAlign: TextAlign.center,
              style: Type.body(14, color: Tone.muted),
            ),
          ],
        ),
      ),
    );
  }
}
