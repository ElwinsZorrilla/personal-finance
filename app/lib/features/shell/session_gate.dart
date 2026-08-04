import 'package:flutter/material.dart';

import '../../data/dashboard_repository.dart';
import '../../data/enrollment.dart';
import '../../data/setup_repository.dart';
import '../../design/tokens.dart';
import 'dashboard_loader.dart';
import 'enrollment_screen.dart';

/// Decide qué se ve: el alta o el panel.
///
/// Es lo que faltaba para que la app desplegada se pudiera usar. Antes el token
/// se ponía a mano desde el código, así que la PWA arrancaba, pedía el panel sin
/// credenciales y enseñaba «hay que volver a entrar» sin manera de entrar.
///
/// **Primero intenta recuperar el token guardado, después renovar con la clave
/// que ya hay, y solo si nada de eso sirve pide el código.** Ese orden es lo que
/// hace que el código de alta se escriba una vez en la vida del dispositivo y no
/// cada vez que caduca un token.
class SessionGate extends StatefulWidget {
  const SessionGate({
    super.key,
    required this.enrollment,
    required this.deviceName,
    required this.repository,
    required this.review,
    this.setup,
  });

  final Enrollment enrollment;
  final String deviceName;
  final DashboardRepository repository;
  final ReviewRepository review;

  /// Se pasa de largo hasta el panel: es este quien descubre, al recibir un
  /// 409, que lo que falta es configurar la app.
  final SetupRepository? setup;

  @override
  State<SessionGate> createState() => _SessionGateState();
}

class _SessionGateState extends State<SessionGate> {
  SessionState? _state;

  @override
  void initState() {
    super.initState();
    _resolve();
  }

  Future<void> _resolve() async {
    if (await widget.enrollment.restore()) {
      if (mounted) setState(() => _state = SessionState.ready);
      return;
    }

    // Sin token guardado pero con clave y dispositivo: se renueva sola. Es el
    // caso normal cuando caduca, y pedir el código aquí obligaría a guardarlo en
    // el teléfono para siempre.
    try {
      if (await widget.enrollment.renew()) {
        if (mounted) setState(() => _state = SessionState.ready);
        return;
      }
    } on Exception {
      // Si la renovación falla —sin red, o el servidor revocó el dispositivo—
      // se cae al alta, que es la única salida que le queda al usuario.
    }

    if (mounted) setState(() => _state = SessionState.needsEnrollment);
  }

  @override
  Widget build(BuildContext context) {
    if (_state == null) {
      return const Scaffold(
        backgroundColor: Tone.ink,
        body: Center(child: CircularProgressIndicator()),
      );
    }

    if (_state == SessionState.ready) {
      return DashboardLoader(
        repository: widget.repository,
        review: widget.review,
        setup: widget.setup,
      );
    }

    return EnrollmentScreen(
      enrollment: widget.enrollment,
      deviceName: widget.deviceName,
      onDone: () => setState(() => _state = SessionState.ready),
    );
  }
}
