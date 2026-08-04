import '../core/money.dart';
import 'api_client.dart';

/// Una categoría, con su identificador para poder elegirla.
class Category {
  const Category({
    required this.id,
    required this.name,
    required this.priority,
    required this.isSystem,
  });

  factory Category.fromJson(Map<String, dynamic> json) => Category(
        id: json['id'] as String,
        name: json['name'] as String,
        priority: json['priority'] as String? ?? 'Flexible',
        isSystem: json['isSystem'] as bool? ?? false,
      );

  final String id;
  final String name;

  /// `Essential`, `Committed`, `Flexible` o `Discretionary`. El servidor lo
  /// escribe en inglés porque es el nombre del `enum` del dominio.
  final String priority;

  final bool isSystem;
}

/// Un movimiento tal como lo devuelve el servidor.
///
/// **Ninguna cifra se calcula aquí.** El signo, la etiqueta de dirección y el
/// nombre de la categoría los escribe el servidor: dos implementaciones del
/// mismo cálculo en dos lenguajes es garantía de que se desincronizan.
class Movement {
  const Movement({
    required this.id,
    required this.merchant,
    required this.amount,
    required this.occurredAt,
    required this.isIncome,
    required this.status,
    required this.accountLastFour,
    this.categoryId,
    this.categoryName,
  });

  factory Movement.fromJson(Map<String, dynamic> json) => Movement(
        id: json['id'] as String,
        merchant: json['merchant'] as String? ?? '',
        amount: Money(json['amountCents'] as int? ?? 0),
        occurredAt: DateTime.parse(json['occurredAt'] as String).toLocal(),
        isIncome: json['isIncome'] as bool? ?? false,
        status: json['status'] as String? ?? '',
        accountLastFour: json['accountLastFour'] as String? ?? '',
        categoryId: json['categoryId'] as String?,
        categoryName: json['categoryName'] as String?,
      );

  final String id;
  final String merchant;
  final Money amount;
  final DateTime occurredAt;
  final bool isIncome;

  /// `Posted`, `Pending`, `NeedsReview`… Lo decide el servidor.
  final String status;

  final String accountLastFour;
  final String? categoryId;
  final String? categoryName;

  /// Sin clasificar todavía. Es lo que la pantalla destaca: un gasto sin
  /// categoría no entra en ningún presupuesto.
  bool get needsCategory => categoryId == null;
}

/// Movimientos y categorías.
class TransactionsRepository {
  const TransactionsRepository(this._api);

  final ApiClient _api;

  /// Los movimientos, del más reciente al más antiguo.
  ///
  /// [limit] lo acota el servidor a 100. Se pide menos a propósito: en una
  /// pantalla de teléfono, cien filas son más de las que nadie recorre, y
  /// pedirlas gasta batería y datos para nada.
  Future<List<Movement>> list({
    String? categoryId,
    String? status,
    int limit = 50,
  }) async {
    final query = <String>[
      'limit=$limit',
      if (categoryId != null) 'categoryId=$categoryId',
      if (status != null) 'status=$status',
    ].join('&');

    final page = await _api.getJson('/transactions?$query');
    final items = page['items'] as List<dynamic>? ?? const [];

    return [
      for (final item in items)
        if (item is Map<String, dynamic>) Movement.fromJson(item),
    ];
  }

  Future<List<Category>> categories() async {
    final lista = await _api.getList('/setup/categories');
    return [for (final item in lista) Category.fromJson(item)];
  }

  /// Cambia la categoría de un movimiento.
  ///
  /// [createRule] hace que el servidor recuerde la corrección: la próxima
  /// compra del mismo comercio se clasifica sola y no vuelve a preguntar. Es la
  /// cascada de la Fase 8 —una corrección del usuario genera una regla— y es lo
  /// que evita corregir el mismo comercio todos los meses.
  Future<Movement> setCategory({
    required String id,
    required String categoryId,
    bool createRule = false,
  }) async =>
      Movement.fromJson(
        await _api.putJson('/transactions/$id', {
          'categoryId': categoryId,
          'createRule': createRule,
        }),
      );
}
