import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

import '../tokens.dart';

/// Cualquier cosa que se pueda pulsar, con la respuesta que se espera de una
/// app y no de una página.
///
/// **Encoge un 3 % mientras el dedo está encima.** Es la diferencia entre una
/// interfaz que parece escuchar y una que parece un documento: en una página
/// web un enlace no se hunde, y esa ausencia es justo lo que delata que algo no
/// es una app. Dura [Motion.quick] y va con `easeOutCubic`, que empieza rápido:
/// una curva que empieza lenta se siente tardía en el momento exacto en que se
/// está mirando.
///
/// Y da un golpecito háptico. En iOS es lo que confirma que el toque llegó
/// antes de que la pantalla tenga tiempo de cambiar.
class Pressable extends StatefulWidget {
  const Pressable({
    super.key,
    required this.child,
    required this.onTap,
    this.semanticLabel,
    this.scale = 0.97,
  });

  final Widget child;

  /// Nulo deshabilita: no encoge, no vibra y no llama.
  final VoidCallback? onTap;

  final String? semanticLabel;
  final double scale;

  @override
  State<Pressable> createState() => _PressableState();
}

class _PressableState extends State<Pressable> {
  bool _down = false;

  bool get _enabled => widget.onTap != null;

  void _set(bool down) {
    if (!_enabled || _down == down) return;
    setState(() => _down = down);
  }

  void _tap() {
    // Antes de la acción, no después: el golpecito es acuse de recibo del
    // toque, y llegar tras el trabajo lo convierte en otra cosa.
    HapticFeedback.lightImpact();
    widget.onTap!.call();
  }

  @override
  Widget build(BuildContext context) {
    final quieto = MediaQuery.disableAnimationsOf(context);

    return Semantics(
      button: true,
      enabled: _enabled,
      label: widget.semanticLabel,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTapDown: (_) => _set(true),
        onTapUp: (_) => _set(false),
        onTapCancel: () => _set(false),
        onTap: _enabled ? _tap : null,
        child: AnimatedScale(
          scale: _down && !quieto ? widget.scale : 1,
          duration: Motion.quick,
          curve: Motion.ease,
          child: widget.child,
        ),
      ),
    );
  }
}
