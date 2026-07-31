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

    test('las comparaciones ordenan por centavos', () {
      expect(const Money(200) > const Money(100), isTrue);
      expect(const Money(-200) < const Money(-100), isTrue);
      expect(const Money(100) >= const Money(100), isTrue);
      expect(const Money(100) <= const Money(100), isTrue);
      expect(const Money(100).compareTo(const Money(200)), lessThan(0));
    });

    test('los predicados de signo dicen lo que prometen', () {
      expect(const Money(-1).isNegative, isTrue);
      expect(Money.zero.isNegative, isFalse);
      expect(Money.zero.isZero, isTrue);
      expect(const Money(1).isZero, isFalse);
      expect(const Money(-1205).abs, const Money(1205));
    });

    test('la resta y el menos unario conservan el signo', () {
      expect(const Money(1000) - const Money(1500), const Money(-500));
      expect(-const Money(500), const Money(-500));
      expect(-const Money(-500), const Money(500));
    });

    test('fromUnits multiplica por cien', () {
      expect(const Money.fromUnits(2450).cents, 245000);
    });

    test('scaled redondea de forma explicita', () {
      // Lo usa un widget para dibujar una barra, no para decidir una cifra.
      // Aun así el redondeo se escribe: `(cents * factor).round()`.
      expect(const Money(1000).scaled(0.5).cents, 500);
      expect(const Money(5).scaled(0.5).cents, 3);
      expect(const Money(1000).scaled(0).cents, 0);
    });

    test('la igualdad es por valor y no por identidad', () {
      expect(const Money(1234), const Money(1234));
      expect(const Money(1234), isNot(const Money(1235)));
      expect(const Money(1234).hashCode, const Money(1234).hashCode);
    });

    test('parse rechaza mas de dos decimales en lugar de truncar', () {
      // Truncar en silencio convertiría 1.239 en 1.23 y la conciliación
      // diferiría por un centavo sin explicar por qué.
      expect(
        () => Money.parse('1.239', format: AmountFormat.commaThousands),
        throwsFormatException,
      );
    });

    test('parse acepta un decimal solo y lo completa', () {
      expect(
        Money.parse('12.5', format: AmountFormat.commaThousands).cents,
        1250,
      );
    });

    test('parse rechaza un monto sin parte entera', () {
      expect(
        () => Money.parse('.50', format: AmountFormat.commaThousands),
        throwsFormatException,
      );
    });

    test('toString usa el formato de auditoria', () {
      expect(const Money(-1205).toString(), r'-RD$ 12.05');
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

    test('agrupa con coma y no con punto', () {
      // `es_DO` en los datos de ICU agrupa al estilo europeo y produce
      // `12.000`, que en República Dominicana se lee «doce». Los separadores
      // se escriben, no se heredan de la configuración regional.
      expect(MoneyFormat.bare(const Money(100)), '1');
      expect(MoneyFormat.bare(const Money(99900)), '999');
      expect(MoneyFormat.bare(const Money(100000)), '1,000');
      expect(MoneyFormat.bare(const Money(123456789)), '1,234,567.89');
      expect(MoneyFormat.bare(const Money(100000000000)), '1,000,000,000');
    });

    test('no pierde el ultimo centavo en montos grandes', () {
      // La versión anterior hacía `cents / 100` para dárselo al formateador.
      // Con un monto de once dígitos, ese `double` redondea y el último
      // centavo desaparece del texto sin que nada avise.
      expect(
        MoneyFormat.exact(const Money(12345678901)),
        r'RD$ 123,456,789.01',
      );
      expect(
        MoneyFormat.exact(const Money(9007199254740993)),
        r'RD$ 90,071,992,547,409.93',
      );
    });

    test('exact siempre lleva dos decimales', () {
      expect(MoneyFormat.exact(const Money(1200000)), r'RD$ 12,000.00');
      expect(MoneyFormat.exact(const Money(5)), r'RD$ 0.05');
      expect(MoneyFormat.exact(const Money(-5)), r'-RD$ 0.05');
    });

    test('display antepone el simbolo y el signo', () {
      expect(MoneyFormat.display(const Money(0)), r'RD$ 0');
      expect(MoneyFormat.display(const Money(-50)), r'-RD$ 0.50');
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
      expect(period.progressAt(DateTime(2026, 12)), 1.0);
    });

    test('posicion de una fecha dentro del riel', () {
      expect(period.positionOf(DateTime(2026, 7, 25)), 0.0);
      expect(period.positionOf(DateTime(2026, 8, 24)), 1.0);
      expect(period.positionOf(DateTime(2026, 7)), 0.0);
    });
  });
}
