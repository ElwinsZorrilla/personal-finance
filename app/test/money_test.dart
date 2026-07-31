import 'package:flutter_test/flutter_test.dart';
import 'package:margen/core/money.dart';
import 'package:margen/core/period.dart';

void main() {
  group('Money', () {
    test('suma cien centavos sin error de punto flotante', () {
      var total = Money.zero;
      for (var i = 0; i < 100; i++) {
        total = total + const Money(10);
      }
      expect(total.cents, 1000);
    });

    test('split reparte sin perder ni inventar centavos', () {
      final parts = const Money(1000).split(3);
      expect(parts.map((p) => p.cents).reduce((a, b) => a + b), 1000);
      expect(parts.map((p) => p.cents).toList(), [334, 333, 333]);
    });

    test('split conserva el signo en montos negativos', () {
      final parts = const Money(-1000).split(3);
      expect(parts.map((p) => p.cents).reduce((a, b) => a + b), -1000);
    });

    test('parse acepta el formato del banco', () {
      expect(
        Money.parse('RD\$ 2,450.00', format: AmountFormat.commaThousands).cents,
        245000,
      );
      expect(
        Money.parse('-1.05', format: AmountFormat.commaThousands).cents,
        -105,
      );
    });

    test('parse respeta el formato declarado y no lo adivina', () {
      expect(
        Money.parse('1.234,56', format: AmountFormat.dotThousands).cents,
        123456,
      );
      expect(
        Money.parse('1,234.56', format: AmountFormat.commaThousands).cents,
        123456,
      );
    });

    test('parse rechaza texto no numerico en lugar de asumir cero', () {
      expect(
        () => Money.parse('pendiente', format: AmountFormat.commaThousands),
        throwsFormatException,
      );
    });

    test('split rechaza cero partes en lugar de dividir por cero', () {
      expect(() => const Money(100).split(0), throwsArgumentError);
    });
  });

  group('MoneyFormat', () {
    test('el signo va delante del simbolo, no dentro del numero', () {
      expect(MoneyFormat.display(const Money(-1200000)), '-RD\$ 12,000');
      expect(MoneyFormat.display(const Money(1200000)), 'RD\$ 12,000');
    });

    test('omite centavos en cero y los muestra cuando existen', () {
      expect(MoneyFormat.bare(const Money(1200000)), '12,000');
      expect(MoneyFormat.bare(const Money(1200050)), '12,000.50');
    });
  });

  group('BudgetPeriod', () {
    final period = BudgetPeriod(
      start: DateTime(2026, 7, 25),
      end: DateTime(2026, 8, 24),
    );

    test('nunca reporta cero dias restantes', () {
      expect(period.daysRemainingFrom(DateTime(2026, 8, 24)), 1);
      expect(period.daysRemainingFrom(DateTime(2026, 9, 30)), 1);
    });

    test('el progreso queda entre cero y uno', () {
      expect(period.progressAt(DateTime(2026, 7, 25)), lessThan(0.1));
      expect(period.progressAt(DateTime(2026, 8, 24)), 1.0);
      expect(period.progressAt(DateTime(2026, 12, 1)), 1.0);
    });

    test('posicion de una fecha dentro del riel', () {
      expect(period.positionOf(DateTime(2026, 7, 25)), 0.0);
      expect(period.positionOf(DateTime(2026, 8, 24)), 1.0);
      expect(period.positionOf(DateTime(2026, 7, 1)), 0.0);
    });
  });
}
