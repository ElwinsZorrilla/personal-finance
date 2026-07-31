import 'package:flutter/material.dart';

import '../../core/money.dart';
import '../../design/components/money_text.dart';
import '../../design/components/pace_bar.dart';
import '../../design/components/section_label.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';
import '../../domain/models.dart';

/// Presupuesto por categoría, ordenado por urgencia y no alfabéticamente:
/// lo que se está saliendo de ritmo va arriba.
class BudgetScreen extends StatelessWidget {
  const BudgetScreen({super.key, required this.snapshot});

  final DashboardSnapshot snapshot;

  @override
  Widget build(BuildContext context) {
    final progress = snapshot.period.progressAt(snapshot.today);
    final lines = [...snapshot.categories]..sort((a, b) {
        final gapA = a.paceGapAt(progress).cents;
        final gapB = b.paceGapAt(progress).cents;
        return gapB.compareTo(gapA);
      });

    return CustomScrollView(
      slivers: [
        SliverPadding(
          padding: EdgeInsets.only(
            left: Space.gutter,
            right: Space.gutter,
            top: MediaQuery.paddingOf(context).top + Space.lg,
            bottom: Space.xxxl,
          ),
          sliver: SliverList.list(
            children: [
              SectionLabel(
                'Ritmo del período',
                trailing: Text(
                  '${(progress * 100).round()} % transcurrido',
                  style: Type.data(11, color: Tone.muted),
                ),
              ),
              for (final line in lines) ...[
                _CategoryBlock(line: line, progress: progress),
                const SizedBox(height: Space.xl),
              ],
            ],
          ),
        ),
      ],
    );
  }
}

class _CategoryBlock extends StatelessWidget {
  const _CategoryBlock({required this.line, required this.progress});

  final CategoryLine line;
  final double progress;

  @override
  Widget build(BuildContext context) {
    final gap = line.paceGapAt(progress);
    final overPace = gap.cents > 0;
    final spentFraction =
        line.budget.isZero ? 0.0 : line.spent.cents / line.budget.cents;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                line.name,
                style: Type.body(15, weight: FontWeight.w600),
              ),
            ),
            MoneyText(line.spent, size: 14),
            Text(
              ' / ${MoneyFormat.bare(line.budget)}',
              style: Type.data(12, color: Tone.faint),
            ),
          ],
        ),
        const SizedBox(height: Space.md),
        PaceBar(
          spentFraction: spentFraction,
          expectedFraction: progress,
          overPace: overPace,
        ),
        const SizedBox(height: Space.sm),
        Text(
          '${MoneyFormat.display(gap.abs)} '
          '${overPace ? 'por encima' : 'por debajo'} del ritmo esperado',
          style: Type.body(
            12,
            color: overPace ? Signal.caution : Tone.faint,
          ),
        ),
        if (line.willOverrun && line.priority.canRedistribute) ...[
          const SizedBox(height: Space.md),
          _RedistributeHint(line: line),
        ],
      ],
    );
  }
}

/// La app propone, no ejecuta. El ajuste automático solo existe entre
/// categorías que el usuario autorizó antes.
class _RedistributeHint extends StatelessWidget {
  const _RedistributeHint({required this.line});

  final CategoryLine line;

  @override
  Widget build(BuildContext context) {
    final excess = line.projected - line.budget;
    return Container(
      padding: const EdgeInsets.all(Space.md),
      decoration: const BoxDecoration(
        color: Tone.surface,
        borderRadius: Radii.brMd,
      ),
      child: Row(
        children: [
          Expanded(
            child: Text(
              'Cerrará ${MoneyFormat.display(excess)} por encima. '
              'Puedes cubrirlo desde categorías flexibles.',
              style: Type.body(12.5, color: Tone.muted, height: 1.35),
            ),
          ),
          const SizedBox(width: Space.sm),
          TextButton(
            onPressed: () {},
            child: const Text('Ajustar'),
          ),
        ],
      ),
    );
  }
}
