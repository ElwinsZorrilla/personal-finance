import 'api_client.dart';
import 'api_sender_io.dart' if (dart.library.js_interop) 'api_sender_web.dart'
    as plataforma;

/// El transporte que corresponde a la plataforma donde corre la app.
///
/// En el teléfono y el escritorio, `HttpClient` de `dart:io`. **En el
/// navegador, `XMLHttpRequest`**, porque `dart:io` no existe ahí.
///
/// Se resuelve con importación condicional por la misma razón que el almacén:
/// `import 'dart:io'` en un archivo que llega al build de web es un error de
/// compilación, así que la rama que no toca ni se compila.
Future<ApiResponse> Function(ApiRequest) defaultSender() =>
    plataforma.createSender();
