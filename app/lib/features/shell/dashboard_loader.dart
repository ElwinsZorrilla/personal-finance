import 'package:flutter/widgets.dart';

import '../../core/period.dart';
import '../../data/api_client.dart';
import '../../data/dashboard_repository.dart';
import '../../data/setup_repository.dart';
import '../../data/transactions_repository.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';
import '../../domain/models.dart';
import '../setup/setup_screen.dart';
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
    this.setup,
    this.movements,
  });

  final DashboardRepository repository;
  final ReviewRepository review;

  /// Opcional porque el modo de maquetas no tiene servidor al que preguntar.
  /// Sin él, un 409 se enseña como antes.
  final SetupRepository? setup;

  /// Ver `AppShell.movements`.
  final TransactionsRepository? movements;

  @override
  State<DashboardLoader> createState() => _DashboardLoaderState();
}

class _DashboardLoaderState extends State<DashboardLoader> {
  DashboardSnapshot? _snapshot;
  ApiFailure? _failure;
  SetupStatus? _pendiente;
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
        _pendiente = null;
        _loading = false;
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;

      // **Un 409 no siempre es un error que enseñar.** El servidor responde
      // «no puedo calcular» tanto cuando falta configurar la app como cuando
      // está configurada y aun así no hay con qué. Lo primero se arregla desde
      // la app; lo segundo, no.
      //
      // Antes los dos casos acababan en la misma pantalla —«todavía no hay
      // cifras que enseñar», sin nada que tocar—, y el primero es justo el
      // estado en que arranca una instalación nueva: los endpoints de
      // configuración existían desde la Fase 4 y ninguna pantalla los llamaba.
      final pendiente = failure.kind == ApiFailureKind.unavailableData
          ? await _queFalta()
          : null;

      if (!mounted) return;

      setState(() {
        _pendiente = pendiente;
        _failure = failure;
        _loading = false;
      });
    }
  }

  /// Pregunta qué falta, o nulo si no falta nada que la app pueda arreglar.
  ///
  /// Si la consulta falla se devuelve nulo y se enseña el error original: el
  /// 409 del panel es información cierta, y taparlo con un fallo de una
  /// consulta secundaria cambiaría un mensaje correcto por otro que no lo es.
  Future<SetupStatus?> _queFalta() async {
    final setup = widget.setup;
    if (setup == null) return null;

    try {
      final estado = await setup.status();
      return estado.isReady ? null : estado;
    } on ApiFailure {
      return null;
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
          AppShell(
            snapshot: snapshot,
            onResolve: _resolve,
            setup: widget.setup,
            movements: widget.movements,
            onChanged: _load,
          ),
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

    final pendiente = _pendiente;
    final setup = widget.setup;

    if (pendiente != null && setup != null) {
      return SetupScreen(
        repository: setup,
        status: pendiente,
        onReady: _load,
      );
    }

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
