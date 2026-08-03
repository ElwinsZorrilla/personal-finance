import 'device_key_io.dart' if (dart.library.js_interop) 'device_key_web.dart'
    as plataforma;

/// La identidad de este dispositivo frente al servidor.
///
/// El servidor no guarda contraseñas: guarda la **clave pública** de cada
/// dispositivo dado de alta, y para entrar hay que firmar un reto con la
/// privada. Eso significa que una copia de la base de datos no permite
/// suplantar a nadie, y que un token robado se revoca sin cambiar credenciales.
///
/// La privada **no sale nunca de aquí**, ni siquiera hacia el resto de la app.
/// Esta interfaz solo deja hacer dos cosas: dar la pública y firmar. No hay
/// forma de pedirle la privada, y esa ausencia es deliberada.
abstract interface class DeviceKey {
  /// La clave pública en SPKI y base64, generando el par si no existía.
  ///
  /// Es lo que se manda al servidor al darse de alta, y es idempotente: llamarla
  /// dos veces devuelve la misma clave, no genera otra. Generar un par nuevo
  /// dejaría al dispositivo sin poder firmar los retos de su propia alta.
  Future<String> ensureKeyPair();

  /// Firma unos bytes y devuelve la firma en base64.
  ///
  /// Recibe los bytes ya construidos y no el reto: **qué se firma exactamente
  /// es un contrato con el servidor** y vive en `DeviceAuth`, no aquí. Esta
  /// capa solo sabe de claves.
  Future<String> sign(List<int> payload);

  /// Si este dispositivo ya tiene una clave.
  Future<bool> exists();
}

/// La implementación de la plataforma donde corre la app.
///
/// En el navegador, WebCrypto con la clave en IndexedDB. Fuera, todavía no hay:
/// la app es una PWA por ADR-001, y escribir una implementación nativa sin
/// plataforma nativa que la use sería código que nadie ejecuta.
DeviceKey defaultDeviceKey() => plataforma.createDeviceKey();
