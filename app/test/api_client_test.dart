import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:margen/data/api_client.dart';

/// Transporte de mentira: guarda lo que se le pide y devuelve lo que se le
/// programó. Cada llamada consume una respuesta; si se acaban, repite la
/// última.
class FakeSender {
  FakeSender(this.responses);

  final List<Object> responses;
  final List<ApiRequest> received = [];
  int calls = 0;

  Future<ApiResponse> call(ApiRequest request) async {
    received.add(request);
    final item =
        responses[calls < responses.length ? calls : responses.length - 1];
    calls++;

    if (item is ApiResponse) return item;
    if (item is Duration) {
      await Future<void>.delayed(item);
      return const ApiResponse(200, '{}');
    }

    throw item;
  }
}

ApiClient _client(
  FakeSender sender, {
  Duration timeout = const Duration(seconds: 10),
  int maxAttempts = 3,
  List<Duration>? sleeps,
}) {
  return ApiClient(
    baseUrl: 'https://ejemplo.invalid',
    send: sender.call,
    timeout: timeout,
    maxAttempts: maxAttempts,
    // Las esperas se registran en lugar de dormirse: una prueba que espera de
    // verdad tarda segundos y nadie la corre.
    sleep: (d) async => sleeps?.add(d),
  );
}

void main() {
  group('ApiClient', () {
    test('devuelve el json de una respuesta buena', () async {
      final sender =
          FakeSender([const ApiResponse(200, '{"safeToSpendCents":1300000}')]);

      final json = await _client(sender).getJson('/dashboard');

      expect(json['safeToSpendCents'], 1300000);
      expect(sender.calls, 1);
      expect(sender.received.single.path, 'https://ejemplo.invalid/dashboard');
    });

    test('reintenta un fallo pasajero y se queda con el que funciona',
        () async {
      final sender = FakeSender([
        const ApiResponse(503, ''),
        const ApiResponse(200, '{"ok":1}'),
      ]);

      final json = await _client(sender).getJson('/dashboard');

      expect(json['ok'], 1);
      expect(sender.calls, 2);
    });

    test('la espera entre reintentos crece', () async {
      // Sin espera creciente, un servidor que se está levantando recibe tres
      // peticiones en cien milisegundos y sigue sin poder responder.
      final sleeps = <Duration>[];
      final sender = FakeSender([const ApiResponse(500, '')]);

      await expectLater(
        _client(sender, sleeps: sleeps).getJson('/dashboard'),
        throwsA(isA<ApiFailure>()),
      );

      expect(sender.calls, 3);
      expect(sleeps.length, 2, reason: 'no se espera tras el último intento');
      expect(sleeps[1], greaterThan(sleeps[0]));
    });

    test('no reintenta un 401', () async {
      // Reintentar un token caducado gasta batería y no arregla nada.
      final sender = FakeSender([const ApiResponse(401, '')]);

      await expectLater(
        _client(sender).getJson('/dashboard'),
        throwsA(
          isA<ApiFailure>()
              .having((f) => f.kind, 'kind', ApiFailureKind.unauthenticated),
        ),
      );

      expect(sender.calls, 1);
    });

    test('no reintenta un 409', () async {
      final sender = FakeSender([const ApiResponse(409, '')]);

      await expectLater(
        _client(sender).getJson('/dashboard'),
        throwsA(
          isA<ApiFailure>()
              .having((f) => f.kind, 'kind', ApiFailureKind.unavailableData),
        ),
      );

      expect(sender.calls, 1);
    });

    test('un 403 se distingue de un 401', () async {
      final sender = FakeSender([const ApiResponse(403, '')]);

      await expectLater(
        _client(sender).getJson('/dashboard'),
        throwsA(
          isA<ApiFailure>()
              .having((f) => f.kind, 'kind', ApiFailureKind.forbidden),
        ),
      );
    });

    test('una respuesta que tarda de mas expira y se reintenta', () async {
      final sender = FakeSender([
        const Duration(seconds: 5),
        const ApiResponse(200, '{"ok":1}'),
      ]);

      final json = await _client(
        sender,
        timeout: const Duration(milliseconds: 50),
      ).getJson('/dashboard');

      expect(json['ok'], 1);
      expect(sender.calls, 2);
    });

    test('sin red el motivo es inalcanzable', () async {
      final sender = FakeSender([const SocketException('sin ruta al host')]);

      await expectLater(
        _client(sender).getJson('/dashboard'),
        throwsA(
          isA<ApiFailure>()
              .having((f) => f.kind, 'kind', ApiFailureKind.unreachable),
        ),
      );

      expect(sender.calls, 3, reason: 'un fallo de red sí se reintenta');
    });

    test('el detalle sale del ProblemDetails del servidor', () async {
      // Es una frase escrita para una persona; el cliente no inventa otra.
      final sender = FakeSender([
        const ApiResponse(
          409,
          '{"title":"Conflict","status":409,'
          '"detail":"No hay un período presupuestario abierto."}',
        ),
      ]);

      await expectLater(
        _client(sender).getJson('/dashboard'),
        throwsA(
          isA<ApiFailure>().having(
            (f) => f.message,
            'message',
            'No hay un período presupuestario abierto.',
          ),
        ),
      );
    });

    test('un cuerpo que no es json no se enseña en crudo', () async {
      // Podría ser la página de error de un intermediario.
      final sender = FakeSender([
        const ApiResponse(502, '<html><body>Bad Gateway</body></html>'),
      ]);

      await expectLater(
        _client(sender).getJson('/dashboard'),
        throwsA(
          isA<ApiFailure>().having(
            (f) => f.message,
            'message',
            isNot(contains('html')),
          ),
        ),
      );
    });

    test('una respuesta buena con json roto es un error del servidor',
        () async {
      final sender = FakeSender([const ApiResponse(200, 'esto no es json')]);

      await expectLater(
        _client(sender).getJson('/dashboard'),
        throwsA(
          isA<ApiFailure>()
              .having((f) => f.kind, 'kind', ApiFailureKind.serverError),
        ),
      );
    });

    test('el token viaja en la cabecera y solo si existe', () async {
      final sender = FakeSender([const ApiResponse(200, '{}')]);
      final client = _client(sender);

      await client.getJson('/dashboard');
      expect(
        sender.received.last.headers.containsKey('Authorization'),
        isFalse,
      );

      client.token = 'abc123';
      await client.getJson('/dashboard');
      expect(sender.received.last.headers['Authorization'], 'Bearer abc123');
    });

    test('un post lleva tipo de contenido y cuerpo', () async {
      final sender = FakeSender([const ApiResponse(200, '{}')]);

      await _client(sender)
          .postJson('/transactions/cash', {'amountCents': 45000});

      expect(sender.received.single.method, 'POST');
      expect(
        sender.received.single.headers['Content-Type'],
        'application/json',
      );
    });

    test('con un solo intento no reintenta nada', () async {
      final sender = FakeSender([const ApiResponse(500, '')]);

      await expectLater(
        _client(sender, maxAttempts: 1).getJson('/dashboard'),
        throwsA(isA<ApiFailure>()),
      );

      expect(sender.calls, 1);
    });
  });
}
