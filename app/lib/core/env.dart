/// Configuración inyectada en tiempo de compilación.
///
/// `--dart-define=API_BASE_URL=https://finanzas.midominio.com`
/// Ninguna URL ni token vive en el código fuente.
abstract final class Env {
  static const apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
  );

  /// Activa datos de desarrollo. En release debe compilarse en `false` para
  /// que el árbol de mocks se elimine del binario.
  static const useMocks = bool.fromEnvironment('USE_MOCKS', defaultValue: true);

  static bool get isConfigured => apiBaseUrl.isNotEmpty || useMocks;
}
