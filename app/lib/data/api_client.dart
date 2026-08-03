import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// Por qué falló una petición. El motivo importa: la pantalla dice cosas
/// distintas según si el teléfono está sin señal o el token caducó.
enum ApiFailureKind {
  /// Sin red, servidor inalcanzable o expiró la espera.
  unreachable,

  /// 401: el token ya no sirve. Hay que volver a firmar un reto.
  unauthenticated,

  /// 403: el token es válido pero no alcanza.
  forbidden,

  /// 409: el servidor entendió y no puede responder con una cifra.
  unavailableData,

  /// Cualquier otro 4xx.
  badRequest,

  /// 5xx, o una respuesta que no se pudo interpretar.
  serverError,
}

class ApiFailure implements Exception {
  const ApiFailure(this.kind, this.message, {this.statusCode});

  final ApiFailureKind kind;
  final String message;
  final int? statusCode;

  /// Reintentar solo tiene sentido cuando el fallo puede ser pasajero.
  /// Reintentar un 401 gasta la batería y no arregla nada.
  bool get isTransient =>
      kind == ApiFailureKind.unreachable || kind == ApiFailureKind.serverError;

  @override
  String toString() => 'ApiFailure(${kind.name}, $statusCode): $message';
}

/// Lo mínimo del transporte que la capa de datos necesita. Existe para que las
/// pruebas no levanten un servidor: se sustituye por una función.
typedef HttpSend = Future<ApiResponse> Function(ApiRequest request);

class ApiRequest {
  const ApiRequest({
    required this.method,
    required this.path,
    this.body,
    this.headers = const {},
  });

  final String method;
  final String path;
  final Object? body;
  final Map<String, String> headers;
}

class ApiResponse {
  const ApiResponse(this.statusCode, this.body);

  final int statusCode;
  final String body;
}

/// Cliente HTTP con reintento y expiración.
///
/// El reintento es con espera creciente y solo sobre fallos pasajeros. Sin
/// espera creciente, un servidor que se está levantando recibe tres peticiones
/// en cien milisegundos y sigue sin poder responder; con ella, el tercer
/// intento llega cuando ya arrancó.
class ApiClient {
  ApiClient({
    required this.baseUrl,
    required HttpSend send,
    this.timeout = const Duration(seconds: 10),
    this.maxAttempts = 3,
    this.backoff = const Duration(milliseconds: 400),
    Future<void> Function(Duration)? sleep,
  })  : _send = send,
        _sleep = sleep ?? Future.delayed;

  final String baseUrl;
  final Duration timeout;

  /// Tres intentos: el original y dos más. Con más, una app en el bolsillo sin
  /// señal se queda medio minuto colgada antes de poder enseñar la caché.
  final int maxAttempts;

  final Duration backoff;
  final HttpSend _send;
  final Future<void> Function(Duration) _sleep;

  String? _token;

  // ignore: avoid_setters_without_getters
  set token(String? value) => _token = value;

  bool get hasToken => _token != null && _token!.isNotEmpty;

  /// Ejecuta y devuelve el JSON ya interpretado.
  Future<Map<String, dynamic>> getJson(String path) async {
    final response = await _run(ApiRequest(method: 'GET', path: path));
    return _decode(response);
  }

  /// [headers] existe por el alta de dispositivo, que manda el código en una
  /// cabecera y no en el cuerpo: así no queda escrito en un registro de
  /// peticiones que incluya cuerpos.
  Future<Map<String, dynamic>> postJson(
    String path,
    Object body, {
    Map<String, String> headers = const {},
  }) async {
    final response = await _run(
      ApiRequest(method: 'POST', path: path, body: body, headers: headers),
    );
    return _decode(response);
  }

  Future<ApiResponse> _run(ApiRequest request) async {
    ApiFailure? last;

    for (var attempt = 1; attempt <= maxAttempts; attempt++) {
      try {
        final response = await _send(_authorize(request)).timeout(timeout);
        final failure = _failureOf(response);

        if (failure == null) return response;
        if (!failure.isTransient) throw failure;

        last = failure;
      } on TimeoutException {
        last = const ApiFailure(
          ApiFailureKind.unreachable,
          'El servidor no respondió a tiempo.',
        );
      } on SocketException catch (e) {
        last = ApiFailure(ApiFailureKind.unreachable, e.message);
      } on HttpException catch (e) {
        last = ApiFailure(ApiFailureKind.unreachable, e.message);
      }

      // No se espera después del último intento: sería medio segundo de
      // retraso antes de un fallo que ya está decidido.
      if (attempt < maxAttempts) {
        await _sleep(backoff * attempt);
      }
    }

    throw last ??
        const ApiFailure(ApiFailureKind.unreachable, 'No se pudo conectar.');
  }

  ApiRequest _authorize(ApiRequest request) {
    return ApiRequest(
      method: request.method,
      path: '$baseUrl${request.path}',
      body: request.body,
      headers: {
        'Accept': 'application/json',
        if (request.body != null) 'Content-Type': 'application/json',
        if (hasToken) 'Authorization': 'Bearer $_token',
        ...request.headers,
      },
    );
  }

  /// Traduce el código a un motivo, o nulo si la respuesta es buena.
  static ApiFailure? _failureOf(ApiResponse response) {
    final code = response.statusCode;
    if (code >= 200 && code < 300) return null;

    final kind = switch (code) {
      // El cero no es un código HTTP: es lo que devuelve el transporte del
      // navegador cuando **no llegó a hablar con nadie** —sin red, tiempo
      // agotado, petición cancelada—. Sin esta rama caía en `badRequest`, y
      // como el panel solo usa la caché ante `unreachable`, abrir la app sin
      // señal daba pantalla de error en vez del último panel conocido.
      //
      // Es el criterio de la Fase 5 —«abrir sin señal y ver lo último»— roto
      // solo en web y sin que nada fallara: el transporte de `dart:io` lanza
      // una excepción y esa sí se reconocía.
      0 => ApiFailureKind.unreachable,
      401 => ApiFailureKind.unauthenticated,
      403 => ApiFailureKind.forbidden,
      409 => ApiFailureKind.unavailableData,
      >= 500 => ApiFailureKind.serverError,
      _ => ApiFailureKind.badRequest,
    };

    return ApiFailure(kind, _detailOf(response.body), statusCode: code);
  }

  /// El servidor responde ProblemDetails. Se saca `detail`, que es la frase
  /// escrita para una persona; si no viene, no se inventa una.
  static String _detailOf(String body) {
    if (body.isEmpty) return 'El servidor respondió con un error.';

    try {
      final decoded = jsonDecode(body);
      if (decoded is Map && decoded['detail'] is String) {
        return decoded['detail'] as String;
      }
    } on FormatException {
      // Un cuerpo que no es JSON no dice nada útil y no se enseña en crudo:
      // podría ser una página de error de un intermediario.
    }

    return 'El servidor respondió con un error.';
  }

  static Map<String, dynamic> _decode(ApiResponse response) {
    try {
      final decoded = jsonDecode(response.body);
      if (decoded is Map<String, dynamic>) return decoded;

      throw const ApiFailure(
        ApiFailureKind.serverError,
        'El servidor respondió algo que no es un objeto JSON.',
      );
    } on FormatException {
      throw const ApiFailure(
        ApiFailureKind.serverError,
        'El servidor respondió algo que no es JSON.',
      );
    }
  }
}

/// Transporte real, sobre `dart:io`. Se aísla aquí para que todo lo de arriba
/// se pueda probar sin red.
class IoHttpSender {
  IoHttpSender({HttpClient? client}) : _client = client ?? HttpClient();

  final HttpClient _client;

  Future<ApiResponse> call(ApiRequest request) async {
    final uri = Uri.parse(request.path);
    final connection = await _client.openUrl(request.method, uri);

    request.headers.forEach(connection.headers.set);

    if (request.body != null) {
      connection.write(jsonEncode(request.body));
    }

    final response = await connection.close();
    final body = await response.transform(utf8.decoder).join();

    return ApiResponse(response.statusCode, body);
  }

  void close() => _client.close(force: true);
}
