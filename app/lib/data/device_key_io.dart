import 'device_key.dart';

/// Fuera del navegador todavía no hay identidad de dispositivo.
///
/// La app es una PWA por ADR-001. Escribir aquí una implementación con el
/// Enclave Seguro sin que exista un empaquetado nativo que la use sería código
/// que nadie ejecuta y que nadie puede probar.
///
/// **Lanza en vez de devolver algo vacío.** Un `DeviceKey` que devuelve cadenas
/// en blanco dejaría que el alta siguiera adelante y fallara más tarde, en el
/// servidor, con un mensaje sobre una clave mal formada. Fallar aquí dice dónde
/// está el problema de verdad.
DeviceKey createDeviceKey() => const UnsupportedDeviceKey();

/// Ver [createDeviceKey].
class UnsupportedDeviceKey implements DeviceKey {
  const UnsupportedDeviceKey();

  static Never _no() => throw UnsupportedError(
        'El alta de dispositivo solo está implementada para el navegador. '
        'Ver ADR-001: la app se distribuye como PWA.',
      );

  @override
  Future<String> ensureKeyPair() async => _no();

  @override
  Future<String> sign(List<int> payload) async => _no();

  @override
  Future<bool> exists() async => false;
}
