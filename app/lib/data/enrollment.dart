import 'api_client.dart';
import 'device_key.dart';
import 'local_store.dart';

/// Da de alta este dispositivo y consigue un token.
///
/// El servidor no guarda contraseñas: guarda la clave pública de cada
/// dispositivo, y entrar consiste en firmar un reto con la privada. El
/// recorrido tiene cuatro pasos y **solo el primero se hace una vez**:
///
/// 1. `POST /auth/devices` — con la pública y el código de alta.
/// 2. `POST /auth/challenges` — el servidor manda un reto.
/// 3. Firmarlo con la clave del dispositivo.
/// 4. `POST /auth/tokens` — a cambio de la firma, un token.
///
/// Los pasos 2 a 4 se repiten cada vez que el token caduca, **sin volver a
/// pedir el código de alta**. Confundir las dos cosas obligaría a guardar ese
/// código en el teléfono para poder renovar, que es justo lo que este diseño
/// evita: el código sirve una vez y después puede borrarse del servidor.
class Enrollment {
  Enrollment({
    required ApiClient api,
    required DeviceKey key,
    required LocalStore store,
  })  : _api = api,
        _key = key,
        _store = store;

  static const _claveToken = 'auth.token.v1';
  static const _claveDispositivo = 'auth.device.v1';

  final ApiClient _api;
  final DeviceKey _key;
  final LocalStore _store;

  /// Recupera el token guardado y se lo pone al cliente.
  ///
  /// Devuelve si había uno. No comprueba que siga siendo válido: eso lo dirá el
  /// servidor con un 401 en la primera petición, y preguntarlo antes sería una
  /// llamada de más en cada arranque para adelantar una respuesta que llega
  /// sola.
  Future<bool> restore() async {
    final guardado = await _store.read(_claveToken);

    if (guardado == null || guardado.isEmpty) return false;

    _api.token = guardado;
    return true;
  }

  /// Da de alta el dispositivo con el código y guarda el token.
  ///
  /// Idempotente en lo que importa: si el dispositivo ya tenía clave, se
  /// reutiliza. Generar una nueva dejaría al servidor con una pública que ya no
  /// se corresponde con nada y al dispositivo sin poder firmar.
  Future<void> enroll({
    required String code,
    required String deviceName,
  }) async {
    final publica = await _key.ensureKeyPair();

    final alta = await _api.postJson(
      '/auth/devices',
      {'name': deviceName, 'publicKeySpki': publica},
      headers: {'X-Margen-Enrollment': code},
    );

    final deviceId = alta['deviceId'] as String?;

    if (deviceId == null || deviceId.isEmpty) {
      throw const ApiFailure(
        ApiFailureKind.badRequest,
        'El servidor no devolvió el identificador del dispositivo.',
      );
    }

    await _store.write(_claveDispositivo, deviceId);
    await _refresh(deviceId);
  }

  /// Vuelve a firmar un reto con la clave que ya hay y consigue otro token.
  ///
  /// Es lo que se llama cuando el token caduca. Devuelve falso si este
  /// dispositivo nunca se dio de alta, que es distinto de que haya fallado:
  /// el primero pide el código, el segundo pide reintentar.
  Future<bool> renew() async {
    final deviceId = await _store.read(_claveDispositivo);

    if (deviceId == null || deviceId.isEmpty) return false;
    if (!await _key.exists()) return false;

    await _refresh(deviceId);
    return true;
  }

  /// Borra el token y el dispositivo guardados.
  ///
  /// **No borra la clave.** Si se borrara, volver a entrar exigiría un código de
  /// alta nuevo aunque el dispositivo sea el mismo de siempre.
  Future<void> forget() async {
    _api.token = null;
    await _store.remove(_claveToken);
  }

  Future<void> _refresh(String deviceId) async {
    final reto =
        await _api.postJson('/auth/challenges', {'deviceId': deviceId});

    final nonce = reto['nonce'] as String?;

    if (nonce == null || nonce.isEmpty) {
      throw const ApiFailure(
        ApiFailureKind.badRequest,
        'El servidor no devolvió un reto que firmar.',
      );
    }

    final firma = await _key.sign(nonce);

    final canje = await _api.postJson(
      '/auth/tokens',
      {'deviceId': deviceId, 'nonce': nonce, 'signature': firma},
    );

    final token = canje['token'] as String?;

    if (token == null || token.isEmpty) {
      throw const ApiFailure(
        ApiFailureKind.badRequest,
        'El servidor no devolvió un token.',
      );
    }

    // Se guarda **antes** de ponérselo al cliente: si la escritura falla, la
    // app sigue sin token y lo pide otra vez, que es recuperable. Al revés
    // quedaría funcionando hasta cerrarla y pidiendo el código al reabrir, sin
    // que nada explicara por qué.
    await _store.write(_claveToken, token);
    _api.token = token;
  }

  /// Comprueba que un código de alta tiene forma de código antes de gastarlo.
  ///
  /// El servidor limita los intentos de alta, así que mandar un código vacío
  /// —porque el campo se quedó sin rellenar— consume uno de esos intentos para
  /// nada.
  static bool looksLikeCode(String code) => code.trim().length >= 8;

  /// Para las pruebas: la clave con que se guarda el token.
  static String get tokenKey => _claveToken;

  /// Para las pruebas: la clave con que se guarda el dispositivo.
  static String get deviceKeyName => _claveDispositivo;
}

/// Lo que la pantalla necesita saber del estado de la sesión.
enum SessionState {
  /// No hay token: hay que dar de alta el dispositivo con un código.
  needsEnrollment,

  /// Hay clave y dispositivo, pero el token caducó. Se renueva sin código.
  needsRenewal,

  /// Hay token.
  ready,
}
