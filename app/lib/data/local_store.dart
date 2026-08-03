import 'local_store_io.dart' if (dart.library.js_interop) 'local_store_web.dart'
    as plataforma;

/// Almacenamiento de clave y texto.
///
/// Es una interfaz y no una llamada directa al disco para que la caché y su
/// caducidad se puedan probar sin tocar el sistema de archivos ni depender de
/// un complemento con canal de plataforma. La implementación real está debajo;
/// las pruebas usan [MemoryStore].
abstract interface class LocalStore {
  Future<String?> read(String key);
  Future<void> write(String key, String value);
  Future<void> remove(String key);
}

/// Para pruebas y para el primer arranque antes de que haya disco.
class MemoryStore implements LocalStore {
  final Map<String, String> _values = {};

  @override
  Future<String?> read(String key) async => _values[key];

  @override
  Future<void> write(String key, String value) async => _values[key] = value;

  @override
  Future<void> remove(String key) async => _values.remove(key);
}

/// El almacén que corresponde a la plataforma donde corre la app.
///
/// En el teléfono y en el escritorio, un archivo por clave. **En el navegador,
/// `localStorage`**, porque `dart:io` no existe ahí.
///
/// Se resuelve con importación condicional y no con una comprobación en tiempo
/// de ejecución: `import 'dart:io'` en un archivo que llega al build de web es
/// un error de compilación, así que la rama que no toca ni siquiera se compila.
///
/// Antes de esto, `main.dart` construía un `FileStore` sobre
/// `Directory.systemTemp` en todas las plataformas. Compilaba —y en el
/// navegador se caía al arrancar, que es exactamente cómo fallaron `MoneyFormat`
/// y `DateLabel` en la Fase 5: el compilador dijo que sí y la pantalla salió en
/// blanco.
LocalStore defaultStore() => plataforma.createStore();
