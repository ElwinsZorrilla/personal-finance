import 'package:flutter_test/flutter_test.dart';
import 'package:margen/core/money.dart';
import 'package:margen/core/period.dart';
import 'package:margen/domain/models.dart';

TxRecord _tx({
  TxKind kind = TxKind.purchase,
  TxStatus status = TxStatus.posted,
  int cents = 100000,
}) {
  return TxRecord(
    id: 'x',
    merchant: 'SUPERMERCADO NACIONAL',
    amount: Money(cents),
    occurredAt: DateTime(2026, 8, 9),
    kind: kind,
    status: status,
    source: TxSource.email,
    category: 'Comida',
    accountLastFour: '1234',
  );
}

void main() {
  group('Priority', () {
    test('solo lo flexible y lo prescindible se redistribuye', () {
      // Es la regla que impide que un algoritmo recorte el alquiler a mitad
      // de mes. La misma que aplica el motor en el servidor.
      expect(Priority.essential.canRedistribute, isFalse);
      expect(Priority.important.canRedistribute, isFalse);
      expect(Priority.flexible.canRedistribute, isTrue);
      expect(Priority.optional.canRedistribute, isTrue);
    });

    test('cada prioridad lleva su nivel y su etiqueta', () {
      expect(Priority.essential.level, 1);
      expect(Priority.optional.level, 4);
      expect(Priority.essential.label, 'Indispensable');
    });
  });

  group('TxRecord', () {
    test('una compra cuenta como gasto', () {
      expect(_tx().affectsSpending, isTrue);
    });

    test('un pago de tarjeta no cuenta como gasto', () {
      // Mueve saldo entre cuentas propias. Contarlo duplicaría el gasto: una
      // vez al comprar con la tarjeta y otra al pagarla.
      expect(_tx(kind: TxKind.payment).affectsSpending, isFalse);
      expect(_tx(kind: TxKind.transfer).affectsSpending, isFalse);
    });

    test('un movimiento descartado no cuenta', () {
      expect(_tx(status: TxStatus.rejected).affectsSpending, isFalse);
      expect(_tx(status: TxStatus.duplicate).affectsSpending, isFalse);
    });

    test('una devolucion cuenta y se marca como credito', () {
      // Cuenta restando: dejarla fuera del gasto la sacaría de su categoría,
      // que es justo lo contrario de lo que hace una devolución.
      final refund = _tx(kind: TxKind.refund);
      expect(refund.affectsSpending, isTrue);
      expect(refund.isCredit, isTrue);
      expect(_tx().isCredit, isFalse);
    });

    test('la confianza nace al maximo salvo que se diga otra cosa', () {
      expect(_tx().confidence, 1.0);
    });
  });

  group('CategoryLine', () {
    const line = CategoryLine(
      name: 'Comida',
      priority: Priority.flexible,
      budget: Money(2000000),
      spent: Money(1200000),
      projected: Money(2400000),
    );

    test('lo disponible es lo asignado menos lo gastado', () {
      expect(line.available, const Money(800000));
    });

    test('lo esperado escala con el avance del periodo', () {
      expect(line.expectedAt(0.5), const Money(1000000));
      expect(line.expectedAt(0), Money.zero);
      expect(line.expectedAt(1), const Money(2000000));
    });

    test('la brecha de ritmo es positiva por encima de lo esperado', () {
      // 60 % gastado con 50 % del período consumido: la señal que la app
      // vigila.
      expect(line.paceGapAt(0.5), const Money(200000));
      expect(line.paceGapAt(0.7).isNegative, isTrue);
    });

    test('avisa cuando la proyeccion supera el presupuesto', () {
      expect(line.willOverrun, isTrue);
      expect(
        const CategoryLine(
          name: 'Ocio',
          priority: Priority.optional,
          budget: Money(1000000),
          spent: Money(100000),
          projected: Money(400000),
        ).willOverrun,
        isFalse,
      );
    });
  });

  group('DashboardSnapshot', () {
    DashboardSnapshot snapshot({DateTime? depletion}) {
      return DashboardSnapshot(
        period: BudgetPeriod(
          start: DateTime(2026, 7, 25),
          end: DateTime(2026, 8, 24),
        ),
        today: DateTime(2026, 8, 9),
        safeToSpend: const Money(1300000),
        safeToday: const Money(80000),
        spentSoFar: const Money(2450000),
        committed: const Money(5200000),
        projectedClose: const Money(4900000),
        commitments: const [],
        categories: const [],
        attention: const [],
        recent: const [],
        projectedDepletion: depletion,
      );
    }

    test('sin fecha de agotamiento el periodo es sano', () {
      // El nulo es la respuesta, no la fecha de cierre: son cosas distintas y
      // confundirlas pondría un aviso de riesgo en todos los períodos sanos.
      expect(snapshot().runsOutEarly, isFalse);
    });

    test('con fecha de agotamiento avisa', () {
      expect(snapshot(depletion: DateTime(2026, 8, 18)).runsOutEarly, isTrue);
    });

    test('los dias restantes salen del periodo y nunca son cero', () {
      expect(snapshot().daysLeft, 16);
    });
  });
}
