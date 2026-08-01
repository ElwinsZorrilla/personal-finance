import 'dart:convert';

import '../domain/models.dart';
import 'api_client.dart';
import 'dashboard_dto.dart';
import 'local_store.dart';

abstract interface class DashboardRepository {
  /// El panel. Puede venir del servidor o de la última lectura guardada;
  /// [DashboardSnapshot.isFromCache] y [DashboardSnapshot.fetchedAt] dicen
  /// cuál de las dos.
  Future<DashboardSnapshot> load();
}

/// Panel del servidor, con la última lectura guardada como red de seguridad.
///
/// El orden es: pedir al servidor, guardar lo que llegue, devolverlo. Si el
/// servidor no está, devolver lo guardado marcándolo como tal.
///
/// Lo que **no** hace es devolver la caché primero y refrescar después. En una
/// app que enseña cuánto dinero queda, una cifra vieja presentada como actual
/// es peor que un segundo de espera: el usuario decide gastar sobre ella.
class RemoteDashboardRepository implements DashboardRepository {
  RemoteDashboardRepository({
    required ApiClient api,
    required LocalStore store,
    DateTime Function()? now,
  })  : _api = api,
        _store = store,
        _now = now ?? DateTime.now;

  static const _cacheKey = 'dashboard.v1';

  final ApiClient _api;
  final LocalStore _store;
  final DateTime Function() _now;

  @override
  Future<DashboardSnapshot> load() async {
    try {
      final json = await _api.getJson('/dashboard');
      final fetchedAt = _now();

      await _save(json, fetchedAt);

      return DashboardDto.parse(json, fetchedAt: fetchedAt);
    } on ApiFailure catch (failure) {
      final cached = await _readCache();

      // Solo se cae a la caché cuando el problema es de red. Un 401 o un 409
      // no se tapan con datos de ayer: el primero pide volver a entrar y el
      // segundo dice que el servidor no puede responder, y las dos cosas hay
      // que enseñarlas.
      if (failure.kind == ApiFailureKind.unreachable && cached != null) {
        return cached;
      }

      rethrow;
    }
  }

  Future<void> _save(Map<String, dynamic> json, DateTime fetchedAt) async {
    // Se comprueba que lo que llegó se puede interpretar **antes** de
    // guardarlo. Guardar un cuerpo que no se entiende convertiría la red de
    // seguridad en una segunda forma de fallar.
    DashboardDto.parse(json, fetchedAt: fetchedAt);

    await _store.write(
      _cacheKey,
      jsonEncode({
        'fetchedAt': fetchedAt.toIso8601String(),
        'body': json,
      }),
    );
  }

  Future<DashboardSnapshot?> _readCache() async {
    final raw = await _store.read(_cacheKey);
    if (raw == null) return null;

    try {
      final envelope = jsonDecode(raw);
      if (envelope is! Map<String, dynamic>) return null;

      final fetchedAt =
          DateTime.tryParse(envelope['fetchedAt'] as String? ?? '');
      final body = envelope['body'];
      if (fetchedAt == null || body is! Map<String, dynamic>) return null;

      return DashboardDto.parse(body, fetchedAt: fetchedAt, isFromCache: true);
    } on FormatException {
      // Una caché ilegible es una caché que no existe. No se propaga: el
      // fallo que importa es el de red que nos trajo hasta aquí.
      return null;
    } on ApiFailure {
      // Guardada por una versión anterior del contrato. Mismo criterio.
      return null;
    }
  }
}

/// Panel de desarrollo. Solo se instancia cuando `Env.useMocks` es cierto.
class MockDashboardRepository implements DashboardRepository {
  const MockDashboardRepository(this._snapshot);

  final DashboardSnapshot Function() _snapshot;

  @override
  Future<DashboardSnapshot> load() async => _snapshot();
}

/// Resolver un aviso de la bandeja de Revisión.
///
/// Va aparte de [DashboardRepository] a propósito: leer el panel y marcar un
/// aviso son dos capacidades distintas, y la pantalla de Revisión no debería
/// poder recargar el panel entero solo porque tiene a mano el repositorio.
abstract interface class ReviewRepository {
  Future<void> resolve(String alertId);
}

class RemoteReviewRepository implements ReviewRepository {
  const RemoteReviewRepository(this._api);

  final ApiClient _api;

  @override
  Future<void> resolve(String alertId) async {
    // El endpoint es idempotente: resolver dos veces no es un error y no mueve
    // la fecha. Es lo que permite que un toque repetido por nervios no rompa
    // nada.
    await _api
        .postJson('/notifications/$alertId/resolve', const <String, Object>{});
  }
}

/// Para desarrollo: acepta y olvida.
class MockReviewRepository implements ReviewRepository {
  const MockReviewRepository();

  @override
  Future<void> resolve(String alertId) async {}
}
