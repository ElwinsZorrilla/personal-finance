import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:margen/data/api_client.dart';
import 'package:margen/data/device_auth.dart';
import 'package:margen/data/device_key.dart';
import 'package:margen/data/enrollment.dart';
import 'package:margen/data/local_store.dart';

/// Una clave de dispositivo de mentira, para probar el recorrido sin navegador.
class _ClaveFalsa implements DeviceKey {
  _ClaveFalsa({this.tiene = false});

  bool tiene;
  int generaciones = 0;
  final List<List<int>> firmados = [];

  @override
  Future<bool> exists() async => tiene;

  @override
  Future<String> ensureKeyPair() async {
    if (!tiene) {
      tiene = true;
      generaciones++;
    }
    return base64.encode(List<int>.filled(91, 7));
  }

  @override
  Future<String> sign(List<int> payload) async {
    if (!tiene) throw StateError('sin clave');
    firmados.add(payload);
    return base64.encode(List<int>.filled(64, 3));
  }
}

/// Responde a cada petición según su ruta.
class _Servidor {
  _Servidor({this.altaFalla = false, this.nonce});

  final bool altaFalla;
  final String? nonce;
  final List<ApiRequest> peticiones = [];

  Future<ApiResponse> call(ApiRequest request) async {
    peticiones.add(request);

    if (request.path.endsWith('/auth/devices')) {
      if (altaFalla) return const ApiResponse(401, '{"detail":"código malo"}');
      return const ApiResponse(
        200,
        '{"deviceId":"11111111-1111-1111-1111-111111111111"}',
      );
    }

    if (request.path.endsWith('/auth/challenges')) {
      return ApiResponse(
        200,
        '{"nonce":"${nonce ?? base64.encode([1, 2, 3, 4])}"}',
      );
    }

    if (request.path.endsWith('/auth/tokens')) {
      return const ApiResponse(200, '{"token":"un-token","scopes":["full"]}');
    }

    return const ApiResponse(404, '{}');
  }
}

void main() {
  ({
    Enrollment alta,
    ApiClient api,
    MemoryStore almacen,
    _ClaveFalsa clave,
    _Servidor servidor
  }) montar({bool altaFalla = false, bool conClave = false, String? nonce}) {
    final servidor = _Servidor(altaFalla: altaFalla, nonce: nonce);
    final api = ApiClient(baseUrl: 'https://api.ejemplo', send: servidor.call);
    final almacen = MemoryStore();
    final clave = _ClaveFalsa(tiene: conClave);

    return (
      alta: Enrollment(api: api, key: clave, store: almacen),
      api: api,
      almacen: almacen,
      clave: clave,
      servidor: servidor,
    );
  }

  group('Enrollment', () {
    test('el alta genera clave, firma el reto y guarda el token', () async {
      final m = montar();

      await m.alta.enroll(code: 'un-codigo-largo', deviceName: 'iPhone');

      expect(m.clave.generaciones, 1);
      expect(m.clave.firmados, hasLength(1));
      expect(m.api.hasToken, isTrue);
      expect(await m.almacen.read(Enrollment.tokenKey), 'un-token');
    });

    test('el codigo de alta viaja en la cabecera y no en el cuerpo', () async {
      // Va en la cabecera para que no quede escrito en un registro de
      // peticiones que incluya cuerpos.
      final m = montar();

      await m.alta.enroll(code: 'un-codigo-largo', deviceName: 'iPhone');

      final devices = m.servidor.peticiones
          .firstWhere((p) => p.path.endsWith('/auth/devices'));

      expect(devices.headers['X-Margen-Enrollment'], 'un-codigo-largo');
      expect(jsonEncode(devices.body), isNot(contains('un-codigo-largo')));
    });

    test('se firma el mensaje que el servidor verifica, no el reto', () async {
      // El servidor no verifica la firma sobre el reto: la verifica sobre
      // «contexto, dispositivo y reto» en texto. La primera versión firmaba el
      // reto decodificado porque un comentario lo afirmaba, y esa firma no
      // validaba nunca —con un error que decía «firma inválida», que es lo
      // único que no era—.
      final m = montar();

      await m.alta.enroll(code: 'un-codigo-largo', deviceName: 'iPhone');

      final firmado = utf8.decode(m.clave.firmados.single);

      expect(firmado, startsWith(DeviceAuth.context));
      expect(firmado, contains('11111111-1111-1111-1111-111111111111'));
      expect(firmado, endsWith(base64.encode([1, 2, 3, 4])));
    });

    test('el reto entra en el mensaje tal como llego, sin decodificar',
        () async {
      // Decodificarlo produciría un mensaje distinto del que se verifica. Y el
      // servidor lo manda en base64url sin relleno, que el decodificador
      // estándar rechaza con «Invalid length».
      final m = montar(nonce: 'abc-_XYZ');

      await m.alta.enroll(code: 'un-codigo-largo', deviceName: 'iPhone');

      expect(utf8.decode(m.clave.firmados.single), endsWith('abc-_XYZ'));
    });

    test('un codigo malo no deja token guardado', () async {
      // Fallar a medias sería peor que fallar: la app arrancaría con un token
      // que no vale y pediría el código otra vez sin decir por qué.
      final m = montar(altaFalla: true);

      await expectLater(
        m.alta.enroll(code: 'un-codigo-largo', deviceName: 'iPhone'),
        throwsA(isA<ApiFailure>()),
      );

      expect(m.api.hasToken, isFalse);
      expect(await m.almacen.read(Enrollment.tokenKey), isNull);
    });

    test('al arrancar se recupera el token guardado', () async {
      final m = montar();
      await m.almacen.write(Enrollment.tokenKey, 'guardado');

      expect(await m.alta.restore(), isTrue);
      expect(m.api.hasToken, isTrue);
    });

    test('sin token guardado no se recupera nada', () async {
      final m = montar();

      expect(await m.alta.restore(), isFalse);
      expect(m.api.hasToken, isFalse);
    });

    test('renovar no vuelve a pedir el codigo de alta', () async {
      // Es la propiedad que hace que el código se escriba una vez en la vida
      // del dispositivo. Si renovar lo pidiera, habría que guardarlo en el
      // teléfono para siempre.
      final m = montar(conClave: true);
      await m.almacen.write(Enrollment.deviceKeyName, 'un-dispositivo');

      expect(await m.alta.renew(), isTrue);
      expect(m.api.hasToken, isTrue);

      expect(
        m.servidor.peticiones.any((p) => p.path.endsWith('/auth/devices')),
        isFalse,
        reason: 'renovar no debe volver a dar de alta el dispositivo',
      );
    });

    test('sin dispositivo guardado no se puede renovar', () async {
      final m = montar(conClave: true);

      expect(await m.alta.renew(), isFalse);
    });

    test('sin clave tampoco', () async {
      final m = montar();
      await m.almacen.write(Enrollment.deviceKeyName, 'un-dispositivo');

      expect(await m.alta.renew(), isFalse);
    });

    test('olvidar quita el token pero conserva el dispositivo', () async {
      // Borrar el dispositivo obligaría a un código de alta nuevo aunque sea
      // el mismo teléfono de siempre.
      final m = montar(conClave: true);
      await m.almacen.write(Enrollment.deviceKeyName, 'un-dispositivo');
      await m.alta.renew();

      await m.alta.forget();

      expect(m.api.hasToken, isFalse);
      expect(await m.almacen.read(Enrollment.tokenKey), isNull);
      expect(await m.almacen.read(Enrollment.deviceKeyName), 'un-dispositivo');
    });

    test('un codigo vacio no llega al servidor', () async {
      // El servidor limita los intentos de alta: gastar uno con el campo sin
      // rellenar es gastarlo para nada.
      expect(Enrollment.looksLikeCode(''), isFalse);
      expect(Enrollment.looksLikeCode('   '), isFalse);
      expect(Enrollment.looksLikeCode('corto'), isFalse);
      expect(
        Enrollment.looksLikeCode('un-codigo-suficientemente-largo'),
        isTrue,
      );
    });
  });
}
