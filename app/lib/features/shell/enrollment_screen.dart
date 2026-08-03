import 'dart:async';

import 'package:flutter/material.dart';

import '../../data/api_client.dart';
import '../../data/enrollment.dart';
import '../../design/tokens.dart';

/// La pantalla de entrada: da de alta este dispositivo con el código.
///
/// Aparece cuando no hay token y desaparece cuando lo hay. No es un formulario
/// de inicio de sesión: no hay usuario ni contraseña que escribir, porque el
/// servidor no guarda ninguna. Lo que se escribe una vez es el código de alta,
/// y a partir de ahí la identidad es una clave que vive en este teléfono.
class EnrollmentScreen extends StatefulWidget {
  const EnrollmentScreen({
    super.key,
    required this.enrollment,
    required this.deviceName,
    required this.onDone,
  });

  final Enrollment enrollment;
  final String deviceName;
  final VoidCallback onDone;

  @override
  State<EnrollmentScreen> createState() => _EnrollmentScreenState();
}

class _EnrollmentScreenState extends State<EnrollmentScreen> {
  final _controller = TextEditingController();
  bool _working = false;
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final code = _controller.text.trim();

    // Se comprueba antes de mandarlo: el servidor limita los intentos de alta,
    // y gastar uno con el campo vacío es gastarlo para nada.
    if (!Enrollment.looksLikeCode(code)) {
      setState(() => _error = 'Escribe el código de alta completo.');
      return;
    }

    setState(() {
      _working = true;
      _error = null;
    });

    try {
      // Con tope. Sin él, cualquier promesa que no se resuelva —IndexedDB
      // bloqueado por otra pestaña, WebCrypto sin contexto seguro— deja el
      // botón en «Conectando…» indefinidamente, que es la peor respuesta
      // posible: no dice si esperar, reintentar o cambiar algo.
      await widget.enrollment
          .enroll(code: code, deviceName: widget.deviceName)
          .timeout(const Duration(seconds: 30));

      if (mounted) widget.onDone();
    } on ApiFailure catch (failure) {
      _fallo(_explain(failure));
    } on TimeoutException {
      _fallo('El alta tardó demasiado. Vuelve a intentarlo.');
    } catch (error) {
      // **Se atrapa todo.** La generación de la clave ocurre antes de la
      // primera petición y puede fallar por motivos que no son de red:
      // almacenamiento del navegador desactivado, contexto no seguro, una
      // pestaña vieja bloqueando la base. Capturar solo `ApiFailure` dejaba
      // esos casos sin recoger y la pantalla colgada.
      _fallo('$error');
    }
  }

  void _fallo(String mensaje) {
    if (!mounted) return;

    setState(() {
      _working = false;
      _error = mensaje;
    });
  }

  /// Traduce el fallo a algo que se pueda leer de pie en la calle.
  ///
  /// El detalle técnico no ayuda a nadie aquí: lo que hace falta es saber si
  /// hay que corregir el código, esperar, o llamar a quien administra.
  static String _explain(ApiFailure failure) => switch (failure.kind) {
        ApiFailureKind.unauthenticated ||
        ApiFailureKind.forbidden =>
          'El código no es correcto.',
        ApiFailureKind.unreachable =>
          'No se pudo conectar con el servidor. Comprueba la conexión.',
        ApiFailureKind.serverError =>
          'El servidor no está aceptando altas ahora mismo.',
        _ => failure.message,
      };

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Tone.ink,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 28, vertical: 32),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    'Margen',
                    style: Theme.of(context).textTheme.headlineMedium,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 12),
                  Text(
                    'Escribe el código de alta para conectar este dispositivo. '
                    'Solo hace falta una vez.',
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: Tone.muted,
                        ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 32),
                  TextField(
                    controller: _controller,
                    enabled: !_working,
                    autocorrect: false,
                    enableSuggestions: false,
                    textInputAction: TextInputAction.go,
                    onSubmitted: (_) => _working ? null : _submit(),
                    decoration: const InputDecoration(
                      labelText: 'Código de alta',
                    ),
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 16),
                    Text(
                      _error!,
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                            color: Signal.risk,
                          ),
                    ),
                  ],
                  const SizedBox(height: 24),
                  FilledButton(
                    onPressed: _working ? null : _submit,
                    child: Text(_working ? 'Conectando…' : 'Conectar'),
                  ),
                  const SizedBox(height: 24),
                  Text(
                    'La clave de este dispositivo se genera aquí y no sale de '
                    'este teléfono. El servidor solo guarda su parte pública.',
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: Tone.faint,
                        ),
                    textAlign: TextAlign.center,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
