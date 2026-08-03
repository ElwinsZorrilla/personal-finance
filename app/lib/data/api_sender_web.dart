import 'dart:async';
import 'dart:convert';
import 'dart:js_interop';

import 'package:web/web.dart' as web;

import 'api_client.dart';

/// El transporte del navegador. **Este archivo no se compila fuera de web.**
Future<ApiResponse> Function(ApiRequest) createSender() =>
    const WebHttpSender().call;

/// Habla con el API desde el navegador.
///
/// Usa `XMLHttpRequest` y no `fetch`. `fetch` es lo moderno y aquí no aporta
/// nada: lo que hace falta es mandar una petición y leer un cuerpo de texto, y
/// `XMLHttpRequest` lo hace con una superficie de interoperación que no cambia
/// entre versiones de `package:web`. Menos cosas que se rompan al actualizar.
///
/// **No se traga los errores de red.** Un fallo de conexión completa con un
/// código 0, y `ApiClient` lo traduce a `ApiFailureKind.unreachable`, que es lo
/// único que autoriza a caer en la caché. Devolver aquí un 500 fabricado haría
/// que un problema de red pareciera un problema del servidor, y la app dejaría
/// de enseñar el último panel conocido justo cuando es lo único que hay.
class WebHttpSender {
  const WebHttpSender();

  /// Tope de espera.
  ///
  /// El navegador tiene el suyo y es de minutos. Una app que se abre para mirar
  /// una cifra no puede quedarse dos minutos en blanco: pasado esto, se trata
  /// como si no hubiera red y se enseña lo guardado.
  static const timeout = Duration(seconds: 15);

  Future<ApiResponse> call(ApiRequest request) {
    final completer = Completer<ApiResponse>();
    final xhr = web.XMLHttpRequest();

    xhr.open(request.method, request.path);
    xhr.timeout = timeout.inMilliseconds;

    // Con lambda y no por referencia: `package:web` prohíbe tomar la
    // referencia de un miembro de interoperación externo.
    request.headers.forEach((nombre, valor) {
      xhr.setRequestHeader(nombre, valor);
    });

    void terminar(ApiResponse response) {
      if (!completer.isCompleted) completer.complete(response);
    }

    xhr.onload = (web.Event _) {
      terminar(ApiResponse(xhr.status, xhr.responseText));
    }.toJS;

    // Sin red, tiempo agotado o petición cancelada. El código 0 es lo que
    // `ApiClient` reconoce como «no se pudo llegar».
    void sinRed(web.Event _) => terminar(const ApiResponse(0, ''));

    xhr.onerror = sinRed.toJS;
    xhr.ontimeout = sinRed.toJS;
    xhr.onabort = sinRed.toJS;

    if (request.body != null) {
      xhr.send(jsonEncode(request.body).toJS);
    } else {
      xhr.send();
    }

    return completer.future;
  }
}
