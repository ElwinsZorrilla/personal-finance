import '../core/money.dart';
import '../core/period.dart';
import '../domain/models.dart';
import 'api_client.dart';

/// Traduce el JSON del servidor al dominio del cliente.
///
/// No calcula nada. Cada cifra de [DashboardSnapshot] tiene un campo que la
/// respalda en el contrato: si alguna vez hiciera falta sumar o proyectar algo
/// aquí, lo que falta es un campo en el servidor, no una operación en este
/// archivo. Dos implementaciones del mismo cálculo en dos lenguajes es garantía
/// de que se desincronizan.
///
/// Lo único que hace es interpretar: entero de centavos a [Money], cadena a
/// enumerado, `yyyy-MM-dd` a fecha.
abstract final class DashboardDto {
  static DashboardSnapshot parse(
    Map<String, dynamic> json, {
    required DateTime fetchedAt,
    bool isFromCache = false,
  }) {
    final period = _object(json, 'period');

    return DashboardSnapshot(
      period: BudgetPeriod(
        start: _date(period, 'start'),
        end: _date(period, 'end'),
      ),
      today: _date(period, 'today'),
      fetchedAt: fetchedAt,
      isFromCache: isFromCache,
      safeToSpend: _money(json, 'safeToSpendCents'),
      safeToday: _money(json, 'safeTodayCents'),
      spentSoFar: _money(json, 'spentSoFarCents'),
      committed: _money(json, 'totalDeductedCents'),
      projectedClose: _money(json, 'projectedCloseCents'),
      projectedDepletion: _dateOrNull(json, 'projectedDepletion'),
      commitments: _list(json, 'commitments').map(_commitment).toList(),
      categories: _list(json, 'categories').map(_category).toList(),
      attention: _list(json, 'attention').map(_alert).toList(),
      recent: _list(json, 'recent').map(_transaction).toList(),
    );
  }

  static Commitment _commitment(Map<String, dynamic> json) {
    return Commitment(
      label: _string(json, 'label'),
      amount: _money(json, 'amountCents'),
      priority: _priority(_string(json, 'priority')),
      dueOn: _dateOrNull(json, 'dueOn'),
    );
  }

  static CategoryLine _category(Map<String, dynamic> json) {
    return CategoryLine(
      name: _string(json, 'name'),
      priority: _priority(_string(json, 'priority')),
      budget: _money(json, 'allocatedCents'),
      spent: _money(json, 'spentCents'),
      projected: _money(json, 'projectedCents'),
    );
  }

  static AttentionItem _alert(Map<String, dynamic> json) {
    return AttentionItem(
      id: _string(json, 'id'),
      kind: _attentionKind(_string(json, 'kind')),
      title: _string(json, 'title'),
      detail: _string(json, 'detail'),
      isUrgent: json['isUrgent'] == true,
    );
  }

  static TxRecord _transaction(Map<String, dynamic> json) {
    return TxRecord(
      id: _string(json, 'id'),
      merchant: _string(json, 'merchant'),
      amount: _money(json, 'amountCents'),

      // `occurredOn` es el día local que ya calculó el servidor. Se usa ese y
      // no `occurredAt`, que es el instante UTC: convertirlo aquí sería el
      // segundo sitio del sistema que conoce la zona horaria, y el que se
      // equivoca con la compra de las once de la noche.
      occurredAt: _date(json, 'occurredOn'),
      kind: _txKind(_string(json, 'kind')),
      status: _txStatus(_string(json, 'status')),
      source: _txSource(_string(json, 'source')),
      category: json['categoryName'] as String? ?? 'Sin categoría',
      accountLastFour: _string(json, 'accountLastFour'),
      confidenceBasisPoints: _int(json, 'confidenceBasisPoints'),
    );
  }

  // ---------- Interpretación de tipos ----------

  /// Un enumerado que el servidor amplíe y el cliente no conozca todavía no
  /// puede tumbar la pantalla: cae en el valor más conservador. Es la misma
  /// regla de fallo cerrado del servidor, aplicada al revés.
  static Priority _priority(String raw) => switch (raw) {
        'Essential' => Priority.essential,
        'Important' => Priority.important,
        'Flexible' => Priority.flexible,
        'Optional' => Priority.optional,

        // Lo desconocido se trata como intocable, no como recortable.
        _ => Priority.essential,
      };

  static TxKind _txKind(String raw) => switch (raw) {
        'Purchase' => TxKind.purchase,
        'Withdrawal' => TxKind.withdrawal,
        'Payment' => TxKind.payment,
        'Refund' => TxKind.refund,
        'Transfer' => TxKind.transfer,
        'Fee' => TxKind.fee,
        'Cash' => TxKind.cash,
        _ => TxKind.purchase,
      };

  static TxStatus _txStatus(String raw) => switch (raw) {
        'Pending' => TxStatus.pending,
        'Posted' => TxStatus.posted,
        'NeedsReview' => TxStatus.needsReview,
        'Duplicate' => TxStatus.duplicate,
        'Rejected' => TxStatus.rejected,
        'Reconciled' => TxStatus.reconciled,

        // Un estado que no se reconoce va a Revisión, no se da por asentado.
        _ => TxStatus.needsReview,
      };

  static TxSource _txSource(String raw) => switch (raw) {
        'Email' => TxSource.email,
        'Shortcut' => TxSource.shortcut,
        'Manual' => TxSource.manual,
        'Statement' => TxSource.statement,
        _ => TxSource.manual,
      };

  static AttentionKind _attentionKind(String raw) => switch (raw) {
        'UnparsedEmail' => AttentionKind.unparsedEmail,
        'LowConfidence' => AttentionKind.lowConfidence,
        'PossibleDuplicate' => AttentionKind.possibleDuplicate,
        'UnusualAmount' => AttentionKind.unusualAmount,
        'OverBudget' => AttentionKind.overBudget,
        'SubscriptionChange' => AttentionKind.subscriptionChange,
        'FundingRisk' => AttentionKind.fundingRisk,

        // `MissingRecurring` y lo que venga después: un aviso que no se sabe
        // dibujar sigue siendo un aviso que hay que enseñar.
        _ => AttentionKind.fundingRisk,
      };

  // ---------- Lectura defensiva ----------
  //
  // Un campo ausente o de otro tipo es un contrato roto, y se dice con una
  // excepción con nombre. La alternativa —un cero por defecto— pondría una
  // cifra inventada en la pantalla que decide cuánto gastar.

  static Money _money(Map<String, dynamic> json, String key) =>
      Money(_int(json, key));

  static int _int(Map<String, dynamic> json, String key) {
    final value = json[key];
    if (value is int) return value;

    throw ApiFailure(
      ApiFailureKind.serverError,
      'El campo «$key» no llegó como entero.',
    );
  }

  static String _string(Map<String, dynamic> json, String key) {
    final value = json[key];
    if (value is String) return value;

    throw ApiFailure(
      ApiFailureKind.serverError,
      'El campo «$key» no llegó como texto.',
    );
  }

  static Map<String, dynamic> _object(Map<String, dynamic> json, String key) {
    final value = json[key];
    if (value is Map<String, dynamic>) return value;

    throw ApiFailure(
      ApiFailureKind.serverError,
      'El campo «$key» no llegó como objeto.',
    );
  }

  static List<Map<String, dynamic>> _list(
    Map<String, dynamic> json,
    String key,
  ) {
    final value = json[key];
    if (value is! List) {
      throw ApiFailure(
        ApiFailureKind.serverError,
        'El campo «$key» no llegó como lista.',
      );
    }

    return value.cast<Map<String, dynamic>>();
  }

  static DateTime _date(Map<String, dynamic> json, String key) {
    final parsed = _dateOrNull(json, key);
    if (parsed != null) return parsed;

    throw ApiFailure(
      ApiFailureKind.serverError,
      'El campo «$key» no llegó como fecha.',
    );
  }

  /// `yyyy-MM-dd` sin zona: son días locales, no instantes. Construirlos con
  /// `DateTime.parse` de una cadena con zona los correría cuatro horas.
  static DateTime? _dateOrNull(Map<String, dynamic> json, String key) {
    final value = json[key];
    if (value is! String || value.isEmpty) return null;

    final soloFecha = value.length >= 10 ? value.substring(0, 10) : value;
    final partes = soloFecha.split('-');
    if (partes.length != 3) return null;

    final year = int.tryParse(partes[0]);
    final month = int.tryParse(partes[1]);
    final day = int.tryParse(partes[2]);
    if (year == null || month == null || day == null) return null;

    return DateTime(year, month, day);
  }
}
