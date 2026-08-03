import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:margen/core/money.dart';
import 'package:margen/data/api_client.dart';
import 'package:margen/data/dashboard_dto.dart';
import 'package:margen/data/dashboard_repository.dart';
import 'package:margen/data/local_store.dart';
import 'package:margen/domain/models.dart';

/// Una respuesta con la forma exacta que devuelve `GET /dashboard`.
///
/// Los nombres y los tipos salen de `docs/api/openapi.json`. Si el servidor
/// cambia el contrato, esta muestra deja de parecerse y estas pruebas dejan de
/// significar lo que dicen: el contrato versionado y su prueba en el lado .NET
/// son lo que impide que eso pase sin que nadie lo note.
Map<String, dynamic> respuesta({
  int safeToSpend = 1300000,
  bool sinHistoria = true,
}) {
  return {
    'period': <String, dynamic>{
      'start': '2026-07-25',
      'end': '2026-08-24',
      'today': '2026-08-09',
      'totalDays': 31,
      'daysRemaining': 16,
      'elapsedDays': 16,
    },
    'safeToSpendCents': safeToSpend,
    'isOverdrawn': false,
    'shortfallCents': 0,
    'deductions': <Map<String, dynamic>>[
      {'kind': 'PendingObligations', 'cents': 2500000},
      {'kind': 'CardReserve', 'cents': 1200000},
      {'kind': 'CommittedSavings', 'cents': 500000},
      {'kind': 'SafetyFund', 'cents': 1000000},
      {'kind': 'Withholdings', 'cents': 0},
    ],
    'totalDeductedCents': 5200000,
    'liquidCents': 6500000,
    'safeTodayCents': 81250,
    'spentSoFarCents': 245000,
    'expectedByNowCents': 2632258,
    'paceDeviationCents': -2387258,
    'projectedCloseCents': 474687,
    'projectedDepletion': null,
    'historicalBaseline': <String, dynamic>{
      'cents': sinHistoria ? null : 4000000,
      'unavailable': sinHistoria ? 'No hay ningún período cerrado.' : null,
    },
    'historicalPeriodsConsidered': sinHistoria ? 0 : 2,
    'categories': <Map<String, dynamic>>[
      <String, dynamic>{
        'categoryId': '11111111-1111-1111-1111-111111111111',
        'name': 'Alquiler',
        'priority': 'Essential',
        'allocatedCents': 2500000,
        'spentCents': 0,
        'availableCents': 2500000,
        'projectedCents': 0,
        'willOverrun': false,
        'canBeTrimmed': false,
      },
      <String, dynamic>{
        'categoryId': '22222222-2222-2222-2222-222222222222',
        'name': 'Comida',
        'priority': 'Flexible',
        'allocatedCents': 2000000,
        'spentCents': 245000,
        'availableCents': 1755000,
        'projectedCents': 474687,
        'willOverrun': false,
        'canBeTrimmed': true,
      },
    ],
    'commitments': <Map<String, dynamic>>[
      <String, dynamic>{
        'id': '33333333-3333-3333-3333-333333333333',
        'label': 'Alquiler',
        'amountCents': 2500000,
        'priority': 'Essential',
        'dueOn': '2026-08-12',
      },
    ],
    'attention': <Map<String, dynamic>>[
      <String, dynamic>{
        'id': '44444444-4444-4444-4444-444444444444',
        'kind': 'PossibleDuplicate',
        'title': 'Dos cargos iguales',
        'detail': 'RD\$ 2,450 dos veces.',
        'isUrgent': true,
        'createdAt': '2026-08-09T15:00:00Z',
      },
    ],
    'recent': <Map<String, dynamic>>[
      <String, dynamic>{
        'id': '55555555-5555-5555-5555-555555555555',
        'merchant': 'SUPERMERCADO NACIONAL',
        'amountCents': 245000,
        'currency': 'DOP',
        'occurredAt': '2026-08-09T03:30:00Z',
        'occurredOn': '2026-08-08',
        'kind': 'Purchase',
        'direction': 'Outflow',
        'directionLabel': 'Egreso',
        'isIncome': false,
        'status': 'Posted',
        'source': 'Email',
        'categoryId': '22222222-2222-2222-2222-222222222222',
        'categoryName': 'Comida',
        'accountLastFour': '1234',
        'confidenceBasisPoints': 10000,
      },
    ],
  };
}

class _Api extends ApiClient {
  _Api(this._responder)
      : super(
          baseUrl: '',
          send: ((_) async => const ApiResponse(200, '{}')),
        );

  final Future<Map<String, dynamic>> Function() _responder;
  int calls = 0;

  @override
  Future<Map<String, dynamic>> getJson(String path) {
    calls++;
    return _responder();
  }
}

void main() {
  group('DashboardDto', () {
    test('mapea cada cifra a su campo del contrato', () {
      final s = DashboardDto.parse(
        respuesta(),
        fetchedAt: DateTime(2026, 8, 9, 7, 30),
      );

      expect(s.safeToSpend, const Money(1300000));
      expect(s.safeToday, const Money(81250));
      expect(s.spentSoFar, const Money(245000));
      expect(s.projectedClose, const Money(474687));

      // `committed` sale de `totalDeductedCents`, que lo suma el servidor.
      // Sumar las cinco deducciones aquí sería calcular en el cliente.
      expect(s.committed, const Money(5200000));
      expect(s.committed.cents, respuesta()['totalDeductedCents']);

      expect(s.period.start, DateTime(2026, 7, 25));
      expect(s.period.end, DateTime(2026, 8, 24));
      expect(s.today, DateTime(2026, 8, 9));
      expect(s.fetchedAt, DateTime(2026, 8, 9, 7, 30));
      expect(s.isFromCache, isFalse);
    });

    test('una compra de las 23:30 conserva su dia local', () {
      // El contrato trae `occurredOn` ya en hora de Santo Domingo. El instante
      // UTC de esta compra es del 9; el día local es el 8.
      final s =
          DashboardDto.parse(respuesta(), fetchedAt: DateTime(2026, 8, 9));

      expect(s.recent.single.occurredAt, DateTime(2026, 8, 8));
    });

    test('las categorias traen su proyeccion del servidor', () {
      final s =
          DashboardDto.parse(respuesta(), fetchedAt: DateTime(2026, 8, 9));
      final comida = s.categories.firstWhere((c) => c.name == 'Comida');

      expect(comida.projected, const Money(474687));
      expect(comida.budget, const Money(2000000));
      expect(comida.available, const Money(1755000));
    });

    test('un enumerado desconocido cae del lado conservador', () {
      // Si el servidor amplía un enumerado, la pantalla no puede tumbarse ni
      // dar por recortable algo que no entiende.
      final json = respuesta();
      (json['categories'] as List)[1]['priority'] = 'PrioridadDelFuturo';
      (json['recent'] as List)[0]['status'] = 'EstadoDelFuturo';

      final s = DashboardDto.parse(json, fetchedAt: DateTime(2026, 8, 9));

      expect(s.categories[1].priority, Priority.essential);
      expect(s.categories[1].priority.canRedistribute, isFalse);
      expect(s.recent.single.status, TxStatus.needsReview);
    });

    test('un campo que falta es un error con nombre, no un cero', () {
      final json = respuesta()..remove('safeToSpendCents');

      expect(
        () => DashboardDto.parse(json, fetchedAt: DateTime(2026, 8, 9)),
        throwsA(
          isA<ApiFailure>().having(
            (f) => f.message,
            'message',
            contains('safeToSpendCents'),
          ),
        ),
      );
    });

    test('un campo con el tipo cambiado tambien', () {
      final json = respuesta()..['safeToSpendCents'] = '1300000';

      expect(
        () => DashboardDto.parse(json, fetchedAt: DateTime(2026, 8, 9)),
        throwsA(isA<ApiFailure>()),
      );
    });

    test('un deposito llega marcado como ingreso', () {
      // Salió de leer los correos reales: «Depósito por ATM» es dinero que
      // entra, y el cliente no lo deduce del tipo sino que lo recibe resuelto.
      final json = respuesta();
      (json['recent'] as List)[0]['kind'] = 'Deposit';
      (json['recent'] as List)[0]['direction'] = 'Inflow';
      (json['recent'] as List)[0]['directionLabel'] = 'Ingreso';

      final s = DashboardDto.parse(json, fetchedAt: DateTime(2026, 8, 9));

      expect(s.recent.single.direction, TxDirection.inflow);
      expect(s.recent.single.isIncome, isTrue);
      expect(s.recent.single.directionLabel, 'Ingreso');

      // Un depósito no reduce ninguna categoría: es dinero nuevo, no un gasto
      // negativo.
      expect(s.recent.single.affectsSpending, isFalse);
    });

    test('una direccion desconocida cae del lado del egreso', () {
      // Contar de más es el error barato; contar de menos hace gastar dinero
      // que no está.
      final json = respuesta();
      (json['recent'] as List)[0]['direction'] = 'DireccionDelFuturo';

      final s = DashboardDto.parse(json, fetchedAt: DateTime(2026, 8, 9));

      expect(s.recent.single.direction, TxDirection.outflow);
    });

    test('un movimiento sin categoria no se queda sin etiqueta', () {
      final json = respuesta();
      (json['recent'] as List)[0]['categoryName'] = null;

      final s = DashboardDto.parse(json, fetchedAt: DateTime(2026, 8, 9));

      expect(s.recent.single.category, 'Sin categoría');
    });
  });

  group('RemoteDashboardRepository', () {
    test('guarda lo que trae el servidor', () async {
      final store = MemoryStore();
      final repo = RemoteDashboardRepository(
        api: _Api(() async => respuesta()),
        store: store,
        now: () => DateTime(2026, 8, 9, 7, 30),
      );

      final s = await repo.load();

      expect(s.isFromCache, isFalse);
      expect(s.safeToSpend, const Money(1300000));
      expect(await store.read('dashboard.v1'), isNotNull);
    });

    test('sin señal devuelve la ultima lectura y lo dice', () async {
      // Es el criterio de la fase: abrir la app sin señal y ver el último
      // panel conocido, con la fecha de esa lectura.
      final store = MemoryStore();
      var falla = false;

      final repo = RemoteDashboardRepository(
        api: _Api(() async {
          if (falla) {
            throw const ApiFailure(ApiFailureKind.unreachable, 'sin red');
          }
          return respuesta();
        }),
        store: store,
        now: () => DateTime(2026, 8, 9, 7, 30),
      );

      await repo.load();
      falla = true;

      final s = await repo.load();

      expect(s.isFromCache, isTrue);
      expect(s.fetchedAt, DateTime(2026, 8, 9, 7, 30));
      expect(s.safeToSpend, const Money(1300000));
    });

    test('sin señal y sin caché no inventa un panel vacio', () async {
      final repo = RemoteDashboardRepository(
        api: _Api(
          () async =>
              throw const ApiFailure(ApiFailureKind.unreachable, 'sin red'),
        ),
        store: MemoryStore(),
      );

      await expectLater(repo.load(), throwsA(isA<ApiFailure>()));
    });

    test('un 401 no se tapa con datos de ayer', () async {
      // Enseñar la caché escondería que hay que volver a entrar, y el usuario
      // decidiría gastar sobre cifras que ya no se actualizan.
      final store = MemoryStore();
      var falla = false;

      final repo = RemoteDashboardRepository(
        api: _Api(() async {
          if (falla) {
            throw const ApiFailure(ApiFailureKind.unauthenticated, 'token');
          }
          return respuesta();
        }),
        store: store,
      );

      await repo.load();
      falla = true;

      await expectLater(
        repo.load(),
        throwsA(
          isA<ApiFailure>()
              .having((f) => f.kind, 'kind', ApiFailureKind.unauthenticated),
        ),
      );
    });

    test('un 409 tampoco', () async {
      final store = MemoryStore();
      var falla = false;

      final repo = RemoteDashboardRepository(
        api: _Api(() async {
          if (falla) {
            throw const ApiFailure(
              ApiFailureKind.unavailableData,
              'sin período',
            );
          }
          return respuesta();
        }),
        store: store,
      );

      await repo.load();
      falla = true;

      await expectLater(
        repo.load(),
        throwsA(
          isA<ApiFailure>()
              .having((f) => f.kind, 'kind', ApiFailureKind.unavailableData),
        ),
      );
    });

    test('una caché ilegible se ignora en vez de romper la app', () async {
      final store = MemoryStore();
      await store.write('dashboard.v1', 'esto ya no es json');

      final repo = RemoteDashboardRepository(
        api: _Api(
          () async =>
              throw const ApiFailure(ApiFailureKind.unreachable, 'sin red'),
        ),
        store: store,
      );

      // El fallo que llega es el de red, no uno de formato: la caché rota es
      // una caché que no existe.
      await expectLater(
        repo.load(),
        throwsA(
          isA<ApiFailure>()
              .having((f) => f.kind, 'kind', ApiFailureKind.unreachable),
        ),
      );
    });

    test('una respuesta que no se entiende no se guarda', () async {
      // Guardar un cuerpo ilegible convertiría la red de seguridad en una
      // segunda forma de fallar.
      final store = MemoryStore();
      final rota = respuesta()..remove('period');

      final repo = RemoteDashboardRepository(
        api: _Api(() async => rota),
        store: store,
      );

      await expectLater(repo.load(), throwsA(isA<ApiFailure>()));
      expect(await store.read('dashboard.v1'), isNull);
    });

    test('la lectura guardada sobrevive a reabrir la app', () async {
      // El escenario real: se guarda, el proceso muere, se abre sin señal.
      final store = MemoryStore();

      await RemoteDashboardRepository(
        api: _Api(() async => respuesta()),
        store: store,
        now: () => DateTime(2026, 8, 9, 7, 30),
      ).load();

      final otroArranque = RemoteDashboardRepository(
        api: _Api(
          () async =>
              throw const ApiFailure(ApiFailureKind.unreachable, 'sin red'),
        ),
        store: store,
      );

      final s = await otroArranque.load();

      expect(s.isFromCache, isTrue);
      expect(s.fetchedAt, DateTime(2026, 8, 9, 7, 30));
    });
  });

  group('FileStore', () {
    late Directory temporal;

    setUp(() => temporal = Directory.systemTemp.createTempSync('margen-test'));
    tearDown(() => temporal.deleteSync(recursive: true));

    test('escribe y vuelve a leer', () async {
      final store = FileStore(temporal);

      await store.write('panel', '{"a":1}');

      expect(await store.read('panel'), '{"a":1}');
    });

    test('una clave que no existe da nulo', () async {
      expect(await FileStore(temporal).read('nada'), isNull);
    });

    test('una clave con barras no escribe fuera del directorio', () async {
      final store = FileStore(temporal);

      await store.write('../../fuera', 'x');

      expect(await store.read('../../fuera'), 'x');
      expect(temporal.listSync().length, 1);
    });

    test('remove borra', () async {
      final store = FileStore(temporal);

      await store.write('panel', 'x');
      await store.remove('panel');

      expect(await store.read('panel'), isNull);
    });

    test('el contenido queda entero, no a medias', () async {
      // Se escribe a un temporal y se renombra: si la app muere a mitad, lo
      // que queda es la versión anterior completa, no media.
      final store = FileStore(temporal);
      final grande = jsonEncode(respuesta());

      await store.write('panel', grande);

      expect(await store.read('panel'), grande);
      expect(
        temporal.listSync().where((e) => e.path.endsWith('.tmp')),
        isEmpty,
      );
    });
  });
}
