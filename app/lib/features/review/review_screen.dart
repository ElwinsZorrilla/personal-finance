import 'package:flutter/material.dart';

import '../../design/components/attention_tile.dart';
import '../../design/components/section_label.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';
import '../../domain/models.dart';

/// Bandeja de lo que el sistema no pudo resolver solo. Vacía es el estado
/// deseado, así que la pantalla vacía celebra en lugar de disculparse.
class ReviewScreen extends StatelessWidget {
  const ReviewScreen({super.key, required this.items});

  final List<AttentionItem> items;

  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: Space.xxl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                'Nada pendiente',
                style: Type.display(22),
              ),
              const SizedBox(height: Space.sm),
              Text(
                'Todo lo que llegó se clasificó con confianza.',
                textAlign: TextAlign.center,
                style: Type.body(14, color: Tone.muted),
              ),
            ],
          ),
        ),
      );
    }

    final urgent = items.where((i) => i.isUrgent).toList();
    final rest = items.where((i) => !i.isUrgent).toList();

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
              if (urgent.isNotEmpty) ...[
                const SectionLabel('Resolver primero'),
                for (final item in urgent) ...[
                  AttentionTile(item: item),
                  const SizedBox(height: Space.sm),
                ],
                const SizedBox(height: Space.xl),
              ],
              if (rest.isNotEmpty) ...[
                const SectionLabel('Cuando puedas'),
                for (final item in rest) ...[
                  AttentionTile(item: item),
                  const SizedBox(height: Space.sm),
                ],
              ],
            ],
          ),
        ),
      ],
    );
  }
}
