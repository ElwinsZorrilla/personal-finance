import '../core/money.dart';
import '../core/period.dart';
import '../domain/models.dart';

/// Datos de desarrollo. Existe para que la capa visual se pueda construir y
/// revisar antes de que el API responda, y para que las pruebas de widget
/// tengan un estado conocido. No se compila en release: ver `Env.useMocks`.
abstract final class MockRepository {
  static final _today = DateTime(2026, 8, 13);
  static final _period = BudgetPeriod(
    start: DateTime(2026, 7, 25),
    end: DateTime(2026, 8, 24),
  );

  /// Estado sano: el dinero alcanza hasta el próximo ingreso. Sin color.
  static DashboardSnapshot healthy() => _build();

  /// Estado tensionado: al ritmo actual el dinero se agota tres días antes.
  static DashboardSnapshot strained() =>
      _build(depletion: DateTime(2026, 8, 21));

  static DashboardSnapshot _build({DateTime? depletion}) {
    return DashboardSnapshot(
      period: _period,
      today: _today,
      fetchedAt: _today,
      safeToSpend: const Money.fromUnits(12000),
      safeToday: const Money.fromUnits(1000),
      spentSoFar: const Money.fromUnits(38200),
      committed: const Money.fromUnits(52500),
      projectedClose: const Money.fromUnits(53400),
      projectedDepletion: depletion,
      commitments: [
        Commitment(
          label: 'Alquiler',
          amount: const Money.fromUnits(20000),
          priority: Priority.essential,
          dueOn: DateTime(2026, 8, 25),
        ),
        Commitment(
          label: 'Pago de tarjetas',
          amount: const Money.fromUnits(18000),
          priority: Priority.essential,
          dueOn: DateTime(2026, 8, 2),
        ),
        Commitment(
          label: 'Servicios',
          amount: const Money.fromUnits(7500),
          priority: Priority.essential,
          dueOn: DateTime(2026, 8, 18),
        ),
        const Commitment(
          label: 'Ahorro',
          amount: Money.fromUnits(8000),
          priority: Priority.important,
        ),
        const Commitment(
          label: 'Fondo de seguridad',
          amount: Money.fromUnits(5000),
          priority: Priority.important,
        ),
      ],
      categories: [
        const CategoryLine(
          name: 'Supermercado',
          priority: Priority.essential,
          budget: Money.fromUnits(14400),
          spent: Money.fromUnits(9100),
          projected: Money.fromUnits(13800),
        ),
        const CategoryLine(
          name: 'Restaurantes',
          priority: Priority.flexible,
          budget: Money.fromUnits(8000),
          spent: Money.fromUnits(6500),
          projected: Money.fromUnits(11300),
        ),
        const CategoryLine(
          name: 'Combustible',
          priority: Priority.important,
          budget: Money.fromUnits(6000),
          spent: Money.fromUnits(3200),
          projected: Money.fromUnits(5900),
        ),
        const CategoryLine(
          name: 'Entretenimiento',
          priority: Priority.optional,
          budget: Money.fromUnits(3000),
          spent: Money.fromUnits(900),
          projected: Money.fromUnits(2000),
        ),
      ],
      attention: [
        const AttentionItem(
          id: 'a1',
          kind: AttentionKind.possibleDuplicate,
          title: 'Dos cargos iguales en Supermercado Bravo',
          detail: 'RD\$ 2,450 dos veces con 4 minutos de diferencia.',
          isUrgent: true,
        ),
        const AttentionItem(
          id: 'a2',
          kind: AttentionKind.subscriptionChange,
          title: 'Una suscripción subió de precio',
          detail: 'Pasó de RD\$ 649 a RD\$ 799 este mes.',
          isUrgent: false,
        ),
      ],
      recent: [
        TxRecord(
          id: 't1',
          merchant: 'Supermercado Bravo',
          amount: const Money.fromUnits(2450),
          occurredAt: DateTime(2026, 8, 13, 18, 39),
          kind: TxKind.purchase,
          status: TxStatus.posted,
          source: TxSource.email,
          category: 'Supermercado',
          accountLastFour: '4582',
          direction: TxDirection.outflow,
          directionLabel: 'Egreso',
        ),
        TxRecord(
          id: 't2',
          merchant: 'Almuerzo',
          amount: const Money.fromUnits(450),
          occurredAt: DateTime(2026, 8, 13, 13, 10),
          kind: TxKind.cash,
          status: TxStatus.posted,
          source: TxSource.shortcut,
          category: 'Restaurantes',
          accountLastFour: '0000',
          direction: TxDirection.outflow,
          directionLabel: 'Egreso',
        ),
        TxRecord(
          id: 't3',
          merchant: 'Estación Sunix',
          amount: const Money.fromUnits(1800),
          occurredAt: DateTime(2026, 8, 12, 8, 2),
          kind: TxKind.purchase,
          status: TxStatus.pending,
          source: TxSource.email,
          category: 'Combustible',
          accountLastFour: '4582',
          direction: TxDirection.outflow,
          directionLabel: 'Egreso',
        ),
        TxRecord(
          id: 't4',
          merchant: 'Pala Pizza',
          amount: const Money.fromUnits(1290),
          occurredAt: DateTime(2026, 8, 11, 20, 44),
          kind: TxKind.purchase,
          status: TxStatus.needsReview,
          source: TxSource.email,
          category: 'Sin categoría',
          accountLastFour: '4582',
          direction: TxDirection.outflow,
          directionLabel: 'Egreso',
          confidenceBasisPoints: 4100,
        ),
        TxRecord(
          id: 't5',
          merchant: 'Devolución Amazon',
          amount: const Money.fromUnits(3120),
          occurredAt: DateTime(2026, 8, 10, 9, 15),
          kind: TxKind.refund,
          status: TxStatus.posted,
          source: TxSource.email,
          category: 'Compras personales',
          accountLastFour: '4582',
          direction: TxDirection.outflow,
          directionLabel: 'Egreso',
        ),
        TxRecord(
          id: 't6',
          merchant: 'Pago de tarjeta',
          amount: const Money.fromUnits(18000),
          occurredAt: DateTime(2026, 8, 2, 11),
          kind: TxKind.payment,
          status: TxStatus.posted,
          source: TxSource.email,
          category: 'Transferencia',
          accountLastFour: '4582',
          direction: TxDirection.outflow,
          directionLabel: 'Egreso',
        ),
      ],
    );
  }
}
