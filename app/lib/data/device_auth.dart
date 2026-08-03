import 'dart:convert';

/// Lo que el dispositivo firma para probar que tiene la clave privada.
///
/// **Es un espejo exacto de `DeviceAuth` en `Margen.Domain`.** El servidor lo
/// describe como «un contrato entre tres partes: quien firma, quien verifica y
/// quien escribe la prueba», y esta es la cuarta: el navegador.
///
/// Que esté duplicado en dos lenguajes no es descuido, es la consecuencia de
/// que las dos partes se compilan por separado. Lo que sí sería descuido es
/// **suponer** qué firma la otra parte en vez de leerlo: la primera versión de
/// este cliente firmaba los bytes del reto porque un comentario mío lo afirmaba,
/// y ese comentario no lo había comprobado nadie. El servidor firma esto otro, y
/// la firma resultante no validaba nunca.
class DeviceAuth {
  const DeviceAuth._();

  /// Etiqueta de propósito y versión.
  ///
  /// Va delante para que una firma emitida aquí no pueda reutilizarse en otro
  /// protocolo que también firme con esta clave. Si cambia, cambia en los dos
  /// lados o nada valida.
  static const context = 'margen-device-auth-v1';

  /// Los bytes que se firman: contexto, dispositivo y reto.
  ///
  /// El reto entra **tal como llegó**, sin decodificar: el servidor lo pone en
  /// el mensaje como texto, y decodificarlo aquí produciría un mensaje distinto
  /// del que se verifica.
  ///
  /// El identificador del dispositivo entra a propósito. Sin él, una firma
  /// válida de un dispositivo sobre un reto podría presentarse como si fuera de
  /// otro.
  static List<int> payload({required String deviceId, required String nonce}) =>
      utf8.encode('$context\n$deviceId\n$nonce');
}
