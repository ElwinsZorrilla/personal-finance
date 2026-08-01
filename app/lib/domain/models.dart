import '../core/money.dart';
import '../core/period.dart';

enum TxKind { purchase, withdrawal, payment, refund, transfer, fee, cash }

enum TxStatus { pending, posted, needsReview, duplicate, rejected, reconciled }

enum TxSource { email, shortcut, manual, statement }

/// Prioridad de una categoría. Determina qué puede recortar el motor de
/// redistribución y qué es intocable.
enum Priority {
  essential(1, 'Indispensable'),
  important(2, 'Importante'),
  flexible(3, 'Flexible'),
  optional(4, 'Prescindible');

  const Priority(this.level, this.label);
  final int level;
  final String label;

  bool get canRedistribute => level >= 3;
}

class Account {
  const Account({
    required this.id,
    required this.name,
    required this.lastFour,
    required this.balance,
  });

  final String id;
  final String name;
  final String lastFour;
  final Money balance;
}

class TxRecord {
  const TxRecord({
    required this.id,
    required this.merchant,
    required this.amount,
    required this.occurredAt,
    required this.kind,
    required this.status,
    required this.source,
    required this.category,
    required this.accountLastFour,
    this.confidenceBasisPoints = 10000,
  });

  final String id;
  final String merchant;
  final Money amount;
  final DateTime occurredAt;
  final TxKind kind;
  final TxStatus status;
  final TxSource source;
  final String category;
  final String accountLastFour;

  /// Confianza de la clasificación automática, en puntos básicos: 10000 es
  /// certeza. Es entero y no `double` por la misma razón que el dinero: se
  /// compara contra un umbral, y un umbral en coma flotante da resultados
  /// distintos según por dónde entró el número. El servidor la envía así.
  final int confidenceBasisPoints;

  /// Por debajo del 75 % la clasificación no se da por buena. Quién decide que
  /// un movimiento va a Revisión es el servidor, vía `status`; esto solo sirve
  /// para matizar cómo se dibuja.
  bool get isUncertain => confidenceBasisPoints < 7500;

  /// Un movimiento suma al gasto salvo que devuelva dinero o solo mueva saldo
  /// entre cuentas propias.
  bool get affectsSpending =>
      status != TxStatus.rejected &&
      status != TxStatus.duplicate &&
      kind != TxKind.transfer &&
      kind != TxKind.payment;

  bool get isCredit => kind == TxKind.refund;
}

/// Dinero ya comprometido: no está disponible aunque esté en la cuenta.
class Commitment {
  const Commitment({
    required this.label,
    required this.amount,
    required this.priority,
    this.dueOn,
  });

  final String label;
  final Money amount;
  final Priority priority;
  final DateTime? dueOn;
}

class CategoryLine {
  const CategoryLine({
    required this.name,
    required this.priority,
    required this.budget,
    required this.spent,
    required this.projected,
  });

  final String name;
  final Priority priority;
  final Money budget;
  final Money spent;
  final Money projected;

  Money get available => budget - spent;

  /// Cuánto se esperaría haber gastado a estas alturas del período.
  Money expectedAt(double periodProgress) => budget.scaled(periodProgress);

  /// Positivo = por encima del ritmo esperado.
  Money paceGapAt(double periodProgress) => spent - expectedAt(periodProgress);

  bool get willOverrun => projected > budget;
}

enum AttentionKind {
  unparsedEmail,
  lowConfidence,
  possibleDuplicate,
  unusualAmount,
  overBudget,
  subscriptionChange,
  fundingRisk
}

class AttentionItem {
  const AttentionItem({
    required this.id,
    required this.kind,
    required this.title,
    required this.detail,
    required this.isUrgent,
  });

  final String id;
  final AttentionKind kind;
  final String title;
  final String detail;
  final bool isUrgent;
}

/// Todo lo que la pantalla principal necesita, resuelto en el servidor.
/// El cliente no calcula presupuesto: solo lo presenta. Un cálculo duplicado
/// en dos lenguajes es un cálculo que se desincroniza.
class DashboardSnapshot {
  const DashboardSnapshot({
    required this.period,
    required this.today,
    required this.safeToSpend,
    required this.safeToday,
    required this.spentSoFar,
    required this.committed,
    required this.projectedClose,
    required this.commitments,
    required this.categories,
    required this.attention,
    required this.recent,
    required this.fetchedAt,
    this.projectedDepletion,
    this.isFromCache = false,
  });

  final BudgetPeriod period;
  final DateTime today;

  /// La cifra principal: lo que queda tras apartar todo lo comprometido.
  final Money safeToSpend;

  /// La cifra secundaria: [safeToSpend] repartido entre los días restantes.
  final Money safeToday;

  final Money spentSoFar;
  final Money committed;
  final Money projectedClose;

  final List<Commitment> commitments;
  final List<CategoryLine> categories;
  final List<AttentionItem> attention;
  final List<TxRecord> recent;

  /// Fecha en la que el dinero se agota al ritmo actual. `null` cuando alcanza
  /// hasta el cierre del período — el caso sano.
  final DateTime? projectedDepletion;

  /// Cuándo se leyó esto del servidor. No es «ahora»: cuando la lectura viene
  /// de la caché puede ser de ayer, y la pantalla tiene que poder decirlo. Una
  /// cifra de dinero sin fecha es una cifra que el usuario cree actual.
  final DateTime fetchedAt;

  /// Se está enseñando la última lectura guardada porque no se pudo hablar con
  /// el servidor.
  final bool isFromCache;

  bool get runsOutEarly => projectedDepletion != null;
  int get daysLeft => period.daysRemainingFrom(today);
}
