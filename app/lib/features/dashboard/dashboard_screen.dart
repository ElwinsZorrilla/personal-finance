import 'package:flutter/material.dart';

import '../../core/money.dart';
import '../../core/period.dart';
import '../../design/components/attention_tile.dart';
import '../../design/components/autonomy_rail.dart';
import '../../design/components/money_text.dart';
import '../../design/components/reserved_row.dart';
import '../../design/components/safe_to_spend.dart';
import '../../design/components/section_label.dart';
import '../../design/components/transaction_tile.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';
import '../../domain/models.dart';

/// Pantalla principal. Un solo lienzo continuo, sin tarjetas apiladas.
///
/// Orden de lectura deliberado: la respuesta, hasta cuándo alcanza, cuánto
/// toca hoy, qué está apartado, qué hay que atender, qué pasó.
class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key, required this.snapshot, this.onRefresh});

  final DashboardSnapshot snapshot;
  final Future<void> Function()? onRefresh;

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: Motion.entrance,
  );

  late final CurvedAnimation _entrance = CurvedAnimation(
    parent: _controller,
    curve: Motion.ease,
  );

  bool _started = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    if (MediaQuery.disableAnimationsOf(context)) {
      _controller.value = 1;
    } else {
      _controller.forward();
    }
  }

  @override
  void dispose() {
    _entrance.dispose();
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.snapshot;

    return RefreshIndicator(
      onRefresh: widget.onRefresh ?? () async {},
      color: Tone.bone,
      backgroundColor: Tone.surface,
      child: CustomScrollView(
        physics: const AlwaysScrollableScrollPhysics(),
        slivers: [
          SliverPadding(
            padding: EdgeInsets.only(
              left: Space.gutter,
              right: Space.gutter,
              top: MediaQuery.paddingOf(context).top + Space.md,
              bottom: Space.xxxl,
            ),
            sliver: SliverList.list(
              children: [
                _Eyebrow(snapshot: s),
                const SizedBox(height: Space.xl),
                _Entrance(
                  animation: _entrance,
                  child: SafeToSpend(
                    amount: s.safeToSpend,
                    caption: 'seguros hasta el ${DateLabel.long(s.period.end)}, '
                        'tu próximo ingreso',
                    tone: s.safeToSpend.isNegative ? Signal.risk : null,
                  ),
                ),
                const SizedBox(height: Space.xl),
                AutonomyRail(
                  period: s.period,
                  today: s.today,
                  depletion: s.projectedDepletion,
                ),
                if (s.runsOutEarly) ...[
                  const SizedBox(height: Space.md),
                  _RiskNote(snapshot: s),
                ],
                const SizedBox(height: Space.xl),
                const Divider(color: Tone.line),
                _TodayLine(amount: s.safeToday, days: s.daysLeft),
                const Divider(color: Tone.line),
                if (s.attention.isNotEmpty) ...[
                  const SizedBox(height: Space.xxl),
                  SectionLabel(
                    'Atención',
                    trailing: Text(
                      '${s.attention.length}',
                      style: Type.data(11, color: Tone.muted),
                    ),
                  ),
                  for (final item in s.attention) ...[
                    AttentionTile(item: item),
                    const SizedBox(height: Space.sm),
                  ],
                ],
                const SizedBox(height: Space.xxl),
                SectionLabel(
                  'Apartado',
                  trailing: MoneyText(
                    s.committed,
                    size: 12,
                    color: Tone.muted,
                  ),
                ),
                for (final c in s.commitments) ReservedRow(commitment: c),
                const SizedBox(height: Space.xxl),
                const SectionLabel('Movimientos recientes'),
                for (final tx in s.recent.take(6))
                  TransactionTile(tx: tx),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Fecha y cuenta regresiva. Es el único contexto que necesita la cifra.
class _Eyebrow extends StatelessWidget {
  const _Eyebrow({required this.snapshot});

  final DashboardSnapshot snapshot;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Text(
          DateLabel.weekday(snapshot.today).toUpperCase(),
          style: Type.eyebrow(),
        ),
        const SizedBox(width: Space.sm),
        Text('·', style: Type.eyebrow(color: Tone.faint)),
        const SizedBox(width: Space.sm),
        Text(
          DateLabel.countdown(snapshot.daysLeft).toUpperCase(),
          style: Type.eyebrow(color: Tone.faint),
        ),
      ],
    );
  }
}

class _TodayLine extends StatelessWidget {
  const _TodayLine({required this.amount, required this.days});

  final Money amount;
  final int days;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.lg),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Hoy puedes gastar', style: Type.body(15)),
              const SizedBox(height: 2),
              Text(
                'repartido entre los $days días que faltan',
                style: Type.body(12, color: Tone.faint),
              ),
            ],
          ),
          MoneyText(amount, size: 22, weight: FontWeight.w600),
        ],
      ),
    );
  }
}

/// Aparece solo cuando el riel muestra un hueco. Traduce el dibujo a una
/// frase, para quien no lo lea de un vistazo y para VoiceOver.
class _RiskNote extends StatelessWidget {
  const _RiskNote({required this.snapshot});

  final DashboardSnapshot snapshot;

  @override
  Widget build(BuildContext context) {
    final depletion = snapshot.projectedDepletion!;
    final short = snapshot.period.end.difference(depletion).inDays;

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          margin: const EdgeInsets.only(top: 5),
          width: 5,
          height: 5,
          decoration: const BoxDecoration(
            color: Signal.risk,
            shape: BoxShape.circle,
          ),
        ),
        const SizedBox(width: Space.sm),
        Expanded(
          child: Text(
            'A este ritmo el dinero se acaba el ${DateLabel.long(depletion)}, '
            '$short días antes del ingreso.',
            style: Type.body(13, color: Tone.bone.withValues(alpha: 0.85)),
          ),
        ),
      ],
    );
  }
}

/// Entrada de la cifra principal: sube 10 px y aparece. Es el único
/// movimiento no funcional de la pantalla.
class _Entrance extends StatelessWidget {
  const _Entrance({required this.animation, required this.child});

  /// Recibe la animación ya construida. Crearla aquí produciría una instancia
  /// nueva —y un listener nuevo— en cada reconstrucción.
  final Animation<double> animation;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: animation,
      builder: (context, inner) => Opacity(
        opacity: animation.value,
        child: Transform.translate(
          offset: Offset(0, 10 * (1 - animation.value)),
          child: inner,
        ),
      ),
      child: child,
    );
  }
}
