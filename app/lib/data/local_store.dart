import 'dart:convert';
import 'dart:io';

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

/// Un archivo por clave, dentro de un directorio.
///
/// La escritura es a un archivo temporal y después un renombrado. Escribir
/// encima directamente deja el archivo a medias si la app muere a mitad —y la
/// app muere a mitad todo el tiempo: el sistema la mata en segundo plano—. Un
/// panel guardado a medias no se puede interpretar y la app abriría sin nada
/// justo cuando la caché es lo único que hay.
class FileStore implements LocalStore {
  FileStore(this.directory);

  final Directory directory;

  @override
  Future<String?> read(String key) async {
    final file = _fileOf(key);
    if (!file.existsSync()) return null;

    try {
      return await file.readAsString();
    } on FileSystemException {
      return null;
    }
  }

  @override
  Future<void> write(String key, String value) async {
    if (!directory.existsSync()) {
      await directory.create(recursive: true);
    }

    final temporal = File('${_fileOf(key).path}.tmp');
    await temporal.writeAsString(value, flush: true);
    await temporal.rename(_fileOf(key).path);
  }

  @override
  Future<void> remove(String key) async {
    final file = _fileOf(key);
    if (file.existsSync()) await file.delete();
  }

  /// La clave se codifica: un nombre de archivo no admite los mismos
  /// caracteres que una clave, y una clave con `..` escribiría fuera del
  /// directorio.
  File _fileOf(String key) =>
      File('${directory.path}${Platform.pathSeparator}${_safe(key)}');

  static String _safe(String key) =>
      base64Url.encode(utf8.encode(key)).replaceAll('=', '');
}
