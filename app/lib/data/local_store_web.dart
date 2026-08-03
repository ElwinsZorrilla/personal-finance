import 'package:web/web.dart' as web;

import 'local_store.dart';

/// El almacén del navegador. **Este archivo no se compila fuera de web.**
LocalStore createStore() => const WebStore();

/// Guarda en `localStorage`.
///
/// No en IndexedDB, que sería lo indicado para datos grandes: aquí se guarda un
/// solo panel en JSON, unos kilobytes, y `localStorage` es síncrono y no tiene
/// versión de esquema que migrar. Con un dato pequeño, la base de datos del
/// navegador es maquinaria para un problema que no existe.
///
/// El límite es de unos cinco megabytes por origen, y una escritura que lo pase
/// lanza. Se atrapa: una caché que no se pudo guardar es una caché que no
/// existe, y eso ya lo sabe manejar quien la lee. Reventar al guardar tiraría la
/// app por no poder escribir algo que es una red de seguridad, no un dato.
///
/// **Se borra con los datos del sitio.** Si el usuario limpia el almacenamiento
/// de Safari, esto se va; la app pedirá el panel al servidor la próxima vez, que
/// es lo correcto. Lo que no se puede es guardar aquí nada que no se pueda
/// volver a pedir.
class WebStore implements LocalStore {
  const WebStore();

  /// Prefijo del origen compartido.
  ///
  /// Una PWA vive en un dominio que puede alojar otras cosas, y `localStorage`
  /// es del origen entero, no de la aplicación. Sin prefijo, dos cosas del
  /// mismo dominio con la misma clave se pisan.
  static const _prefix = 'margen.';

  @override
  Future<String?> read(String key) async =>
      web.window.localStorage.getItem('$_prefix$key');

  @override
  Future<void> write(String key, String value) async {
    try {
      web.window.localStorage.setItem('$_prefix$key', value);
    } catch (_) {
      // Cuota llena, o almacenamiento desactivado en modo privado. Ver arriba:
      // no poder guardar la red de seguridad no es motivo para tirar la app.
    }
  }

  @override
  Future<void> remove(String key) async =>
      web.window.localStorage.removeItem('$_prefix$key');
}
