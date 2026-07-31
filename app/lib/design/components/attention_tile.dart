import 'package:flutter/material.dart';

import '../../domain/models.dart';
import '../tokens.dart';
import '../typography.dart';

/// Único lugar de la app donde el color aparece sin que haya un problema
/// de dinero: aquí el color *es* el mensaje.
class AttentionTile extends StatelessWidget {
  const AttentionTile({super.key, required this.item, this.onTap});

  final AttentionItem item;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final accent = item.isUrgent ? Signal.risk : Signal.caution;

    return Semantics(
      button: true,
      label: '${item.title}. ${item.detail}',
      child: InkWell(
        onTap: onTap,
        borderRadius: Radii.brMd,
        child: Container(
          padding: const EdgeInsets.symmetric(
            horizontal: Space.lg,
            vertical: Space.md,
          ),
          decoration: BoxDecoration(
            color: Tone.surface,
            borderRadius: Radii.brMd,
            border: Border(left: BorderSide(color: accent, width: 2.5)),
          ),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.title,
                      style: Type.body(14, weight: FontWeight.w600),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      item.detail,
                      style: Type.body(13, color: Tone.muted, height: 1.35),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: Space.md),
              const Icon(Icons.chevron_right, size: 18, color: Tone.faint),
            ],
          ),
        ),
      ),
    );
  }
}
