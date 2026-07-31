import 'package:flutter/widgets.dart';

import '../../core/period.dart';
import '../tokens.dart';
import '../typography.dart';

/// **Riel de autonomía** — el elemento firma de la app.
///
/// Un donut de categorías responde "¿en qué gasté?". Esta app responde otra
/// pregunta: "¿hasta dónde me alcanza?". Eso es una distancia, no una
/// proporción, así que se dibuja como una distancia.
///
/// El riel abarca el período completo, de un ingreso al siguiente:
///
/// ```text
///   ══════════●━━━━━━━━━━━┈┈┈┈┈┈┈╎
///   25 jul    hoy                 24 ago
///   └ vivido ─┘└─ alcance ─┘└ falta ┘
/// ```
///
/// * El tramo **vivido** es historia: se dibuja apagado.
/// * El tramo de **alcance** es hasta dónde llega el dinero al ritmo actual.
/// * Si el alcance no llega al próximo ingreso, el faltante aparece punteado
///   en color de riesgo. Ese hueco es toda la advertencia que hace falta.
///
/// Cuando el dinero alcanza, no hay color en ningún lado.
class AutonomyRail extends StatefulWidget {
  const AutonomyRail({
    super.key,
    required this.period,
    required this.today,
    this.depletion,
  });

  final BudgetPeriod period;
  final DateTime today;

  /// Fecha de agotamiento proyectada. `null` cuando el dinero llega al cierre.
  final DateTime? depletion;

  @override
  State<AutonomyRail> createState() => _AutonomyRailState();
}

class _AutonomyRailState extends State<AutonomyRail>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: Motion.entrance,
  );

  late final CurvedAnimation _reach = CurvedAnimation(
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
    // CurvedAnimation registra un listener en su padre: liberarla antes que
    // el controlador evita retener el State después de desmontar.
    _reach.dispose();
    _controller.dispose();
    super.dispose();
  }

  String get _semanticsLabel {
    final left = widget.period.daysRemainingFrom(widget.today);
    if (widget.depletion == null) {
      return 'El dinero alcanza hasta el cierre del período, '
          'dentro de $left días.';
    }
    final gap = widget.period.end.difference(widget.depletion!).inDays;
    return 'Al ritmo actual el dinero se agota el '
        '${DateLabel.long(widget.depletion!)}, '
        '$gap días antes del próximo ingreso.';
  }

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: _semanticsLabel,
      excludeSemantics: true,
      child: SizedBox(
        height: 46,
        child: AnimatedBuilder(
          animation: _reach,
          builder: (context, _) => CustomPaint(
            size: Size.infinite,
            painter: _RailPainter(
              period: widget.period,
              today: widget.today,
              depletion: widget.depletion,
              reach: _reach.value,
              textScale: MediaQuery.textScalerOf(context),
            ),
          ),
        ),
      ),
    );
  }
}

class _RailPainter extends CustomPainter {
  _RailPainter({
    required this.period,
    required this.today,
    required this.depletion,
    required this.reach,
    required this.textScale,
  });

  final BudgetPeriod period;
  final DateTime today;
  final DateTime? depletion;
  final double reach;
  final TextScaler textScale;

  static const _railY = 12.0;
  static const _labelY = 26.0;

  @override
  void paint(Canvas canvas, Size size) {
    final w = size.width;
    final xToday = period.positionOf(today) * w;
    final xEnd = w;
    final xDepletion =
        depletion == null ? xEnd : period.positionOf(depletion!) * w;

    // Tramo vivido: historia, sin énfasis.
    canvas.drawLine(
      const Offset(0, _railY),
      Offset(xToday, _railY),
      Paint()
        ..color = Tone.line
        ..strokeWidth = 2
        ..strokeCap = StrokeCap.round,
    );

    // Tramo de alcance: crece desde hoy al entrar la pantalla.
    final reachEnd = xToday + (xDepletion - xToday) * reach;
    if (reachEnd > xToday) {
      canvas.drawLine(
        Offset(xToday, _railY),
        Offset(reachEnd, _railY),
        Paint()
          ..color = Tone.bone
          ..strokeWidth = 3
          ..strokeCap = StrokeCap.round,
      );
    }

    // Faltante: solo existe cuando el dinero no llega al próximo ingreso.
    if (depletion != null && xDepletion < xEnd - 1) {
      _dashed(
        canvas,
        Offset(xDepletion + 5, _railY),
        Offset(xEnd, _railY),
        Signal.risk.withValues(alpha: 0.28 + 0.72 * reach),
      );
      canvas.drawCircle(
        Offset(xDepletion, _railY),
        3,
        Paint()..color = Signal.risk,
      );
    }

    // Marca de hoy.
    canvas.drawCircle(
      Offset(xToday, _railY),
      4.5,
      Paint()..color = Tone.ink,
    );
    canvas.drawCircle(
      Offset(xToday, _railY),
      3,
      Paint()..color = Tone.bone,
    );

    // Cierre del período: el próximo ingreso.
    canvas.drawLine(
      Offset(xEnd - 0.5, _railY - 5),
      Offset(xEnd - 0.5, _railY + 5),
      Paint()
        ..color = Tone.bone
        ..strokeWidth = 1.5,
    );

    _label(canvas, DateLabel.short(period.start), 0, Alignment.centerLeft, w);
    _label(canvas, DateLabel.short(period.end), w, Alignment.centerRight, w);

    // "hoy" solo si no choca con las fechas de los extremos.
    if (xToday > 46 && xToday < w - 52) {
      _label(canvas, 'hoy', xToday, Alignment.center, w, color: Tone.bone);
    }
  }

  void _dashed(Canvas canvas, Offset from, Offset to, Color color) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 2
      ..strokeCap = StrokeCap.round;
    const dash = 3.0;
    const gap = 4.0;
    var x = from.dx;
    while (x < to.dx) {
      final segmentEnd = (x + dash).clamp(from.dx, to.dx);
      canvas.drawLine(Offset(x, from.dy), Offset(segmentEnd, from.dy), paint);
      x += dash + gap;
    }
  }

  void _label(
    Canvas canvas,
    String text,
    double x,
    Alignment align,
    double maxWidth, {
    Color color = Tone.muted,
  }) {
    final painter = TextPainter(
      text: TextSpan(
        text: text,
        style: Type.data(10, color: color).copyWith(letterSpacing: 0.4),
      ),
      textDirection: TextDirection.ltr,
      textScaler: textScale,
    )..layout();

    final dx = switch (align) {
      Alignment.centerLeft => 0.0,
      Alignment.centerRight => maxWidth - painter.width,
      _ => (x - painter.width / 2).clamp(0.0, maxWidth - painter.width),
    };
    painter.paint(canvas, Offset(dx, _labelY));
    painter.dispose();
  }

  @override
  bool shouldRepaint(_RailPainter old) =>
      old.reach != reach ||
      old.depletion != depletion ||
      old.today != today ||
      old.period != period;
}
