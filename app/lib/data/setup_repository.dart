import '../core/money.dart';
import 'api_client.dart';

/// Qué le falta al servidor para poder calcular.
///
/// Lleva la lista de motivos y no solo un sí o un no. El servidor la escribe en
/// frases enteras —«No hay ninguna cuenta. Los movimientos no tienen dónde
/// colgarse.»— y la app las enseña tal cual: quien sabe por qué no puede
/// calcular es el servidor, y traducirlas aquí sería mantener dos versiones de
/// la misma explicación.
class SetupStatus {
  const SetupStatus({
    required this.isReady,
    required this.accounts,
    required this.categories,
    required this.hasOpenPeriod,
    required this.emailsWaiting,
    required this.missing,
  });

  factory SetupStatus.fromJson(Map<String, dynamic> json) => SetupStatus(
        isReady: json['isReady'] as bool? ?? false,
        accounts: json['accounts'] as int? ?? 0,
        categories: json['categories'] as int? ?? 0,
        hasOpenPeriod: json['hasOpenPeriod'] as bool? ?? false,
        emailsWaiting: json['emailsWaiting'] as int? ?? 0,
        missing: (json['missing'] as List<dynamic>? ?? const [])
            .whereType<String>()
            .toList(growable: false),
      );

  final bool isReady;
  final int accounts;
  final int categories;
  final bool hasOpenPeriod;

  /// Correos ya guardados que todavía no tienen cuenta donde colgarse.
  ///
  /// Se enseña al terminar porque cambia lo que hay que hacer después: no se
  /// pierden, pero tampoco entran solos. Sin decirlo, quien configura la app
  /// esperaría ver movimientos que no van a aparecer.
  final int emailsWaiting;

  final List<String> missing;
}

/// El tipo de una cuenta, con el nombre que entiende el servidor.
///
/// El valor que viaja es el nombre en inglés porque es el del `enum` del
/// dominio; la etiqueta es lo único que se lee en pantalla.
enum AccountKind {
  checking('Checking', 'Cuenta corriente'),
  savings('Savings', 'Ahorros'),
  credit('Credit', 'Tarjeta de crédito'),
  cash('Cash', 'Efectivo');

  const AccountKind(this.wire, this.label);

  final String wire;
  final String label;
}

/// Las llamadas de configuración inicial.
///
/// **Las tres son idempotentes en el servidor**, y eso decide cómo se comporta
/// la pantalla: repetir un paso porque la respuesta se perdió por el camino no
/// crea nada duplicado, así que reintentar es seguro y no hay que preguntar
/// «¿seguro que no lo hiciste ya?».
class SetupRepository {
  const SetupRepository(this._api);

  final ApiClient _api;

  Future<SetupStatus> status() async =>
      SetupStatus.fromJson(await _api.getJson('/setup/status'));

  /// Crea las categorías predeterminadas.
  ///
  /// No lleva parámetros a propósito: los nombres tienen que ser exactamente
  /// los que devuelve el clasificador, y dejar escribirlos aquí sería dejar
  /// escribir uno que no case con ninguno y que nunca clasifique nada.
  Future<void> seedCategories() =>
      _api.postJson('/setup/categories/defaults', const <String, Object?>{});

  Future<void> createAccount({
    required String name,
    required String lastFour,
    required AccountKind kind,
    required Money balance,
    Money? creditLimit,
  }) =>
      _api.postJson('/setup/accounts', {
        'name': name,
        'lastFour': lastFour,
        'kind': kind.wire,
        // Centavos enteros. La cifra sale de `Money.parse`, que no pasa por
        // punto flotante en ningún momento: es la única cifra del sistema que
        // escribe una persona y entra directa en la fórmula del dinero seguro.
        'balanceCents': balance.cents,
        'creditLimitCents': creditLimit?.cents,
      });

  /// Abre el período que contiene hoy.
  ///
  /// [payDays] son los días del mes en que entra el sueldo: uno si se cobra una
  /// vez al mes, dos con quincena y fin de mes. **Fin de mes se escribe 31** y
  /// el servidor lo corre al último día que exista en cada mes.
  ///
  /// [expectedIncome] es lo que se cobra **en cada uno** de esos días, no el
  /// total del mes: un período es un ciclo, y su ingreso es el de ese ciclo.
  Future<void> openPeriod({
    required List<int> payDays,
    required Money expectedIncome,
    Money? safetyFund,
    Money? committedSavings,
  }) =>
      _api.postJson('/setup/periods', {
        'payDays': payDays,
        'expectedIncomeCents': expectedIncome.cents,
        'safetyFundCents': safetyFund?.cents,
        'committedSavingsCents': committedSavings?.cents,
      });
}
