import 'package:flutter/widgets.dart';

import '../../core/period.dart';
import '../../data/api_client.dart';
import '../../data/dashboard_repository.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';
import '../../domain/models.dart';
import 'app_shell.dart';

/// Carga el panel y decide qué se ve mientras tanto.
///
/// Tres estados y ninguno inventa una cifra: cargando, error con su motivo, o
/// el panel. Cuando el panel viene de la caché lo dice con la fecha de la
/// lectura, porque una cifra de dinero sin fecha es una cifra que el usuario
/// cree actual.
class DashboardLoader extends StatefulWidget {
  const DashboardLoader({
    super.key,
    required this.repository,
    required this.review,
  });

  final DashboardRepository repository;
  final ReviewRepository review;

  @override
  State<DashboardLoader> createState() => _DashboardLoaderState();
}

class _DashboardLoaderState extends State<DashboardLoader> {
  DashboardSnapshot? _snapshot;
  ApiFailure? _failure;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);

    try {
      final snapshot = await widget.repository.load();
      if (!mounted) return;

      setState(() {
        _snapshot = snapshot;
        _failure = null;
        _loading = false;
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;

      setState(() {
        _failure = failure;
        _loading = false;
      });
    }
  }

  Future<void> _resolve(AttentionItem item) async {
    try {
      await widget.review.resolve(item.id);
    } on ApiFailure {
      // No se recarga: el aviso sigue ahí y el usuario puede volver a
      // intentarlo. Tragarse el fallo y quitarlo de la lista haría creer que
      // se resolvió algo que sigue pendiente.
      return;
    }

    await _load();
  }

  @override
  Widget build(BuildContext context) {
    final snapshot = _snapshot;

    if (snapshot != null) {
      return Stack(
        children: [
          AppShell(snapshot: snapshot, onResolve: _resolve),
          if (snapshot.isFromCache)
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: _StaleBanner(fetchedAt: snapshot.fetchedAt),
            ),
        ],
      );
    }

    if (_loading) return const _Waiting();

    return _Failure(failure: _failure, onRetry: _load);
  }
}

/// Mientras se espera no se enseña ninguna cifra, ni siquiera un cero de
/// relleno: un cero en esta pantalla significa «no gastes nada».
class _Waiting extends StatelessWidget {
  const _Waiting();

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      color: Tone.ink,
      child: Center(
        child: Text(
          'Poniendo las cuentas al día…',
          style: Type.body(15, color: Tone.muted),
          semanticsLabel: 'Cargando el panel',
        ),
      ),
    );
  }
}

class _Failure extends StatelessWidget {
  const _Failure({required this.failure, required this.onRetry});

  final ApiFailure? failure;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      color: Tone.ink,
      child: Center(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: Space.xl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(_title, style: Type.body(18)),
              const SizedBox(height: Space.md),

              // El detalle es el que escribió el servidor en ProblemDetails,
              // que es una frase para una persona. No se inventa otra.
              Text(
                failure?.message ?? 'No se pudo cargar el panel.',
                style: Type.body(14, color: Tone.muted),
              ),
              const SizedBox(height: Space.xl),
              GestureDetector(
                onTap: onRetry,
                child: Semantics(
                  button: true,
                  label: 'Reintentar cargar el panel',
                  child: Text(
                    'Reintentar',
                    style: Type.eyebrow(color: Tone.bone),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  String get _title => switch (failure?.kind) {
        ApiFailureKind.unreachable => 'Sin conexión con el servidor',
        ApiFailureKind.unauthenticated => 'Hay que volver a entrar',
        ApiFailureKind.unavailableData => 'Todavía no hay cifras que enseñar',
        _ => 'Algo salió mal',
      };
}

/// Aviso de que lo que se ve es la última lectura guardada.
///
/// Va abajo y en tono apagado: no es una alarma, es una advertencia sobre la
/// vigencia de lo que hay arriba. Lleva la fecha porque «desactualizado» sin
/// fecha no permite decidir si importa.
class _StaleBanner extends StatelessWidget {
  const _StaleBanner({required this.fetchedAt});

  final DateTime fetchedAt;

  @override
  Widget build(BuildContext context) {
    final cuando = DateLabel.weekday(fetchedAt);
    final hora = '${fetchedAt.hour.toString().padLeft(2, '0')}:'
        '${fetchedAt.minute.toString().padLeft(2, '0')}';

    return ColoredBox(
      color: Tone.surfaceRaised,
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: Space.lg,
            vertical: Space.md,
          ),
          child: Text(
            'Sin conexión · última lectura del $cuando a las $hora',
            style: Type.data(12, color: Tone.muted),
            semanticsLabel:
                'Sin conexión. Estas cifras son de la última lectura, '
                'del $cuando a las $hora.',
          ),
        ),
      ),
    );
  }
}
