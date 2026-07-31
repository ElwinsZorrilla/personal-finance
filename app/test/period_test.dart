import 'package:flutter_test/flutter_test.dart';
import 'package:margen/core/period.dart';

void main() {
  final period = BudgetPeriod(
    start: DateTime(2026, 7, 25),
    end: DateTime(2026, 8, 24),
  );

  group('BudgetPeriod', () {
    test('cuenta los dos extremos dentro del total', () {
      // Del 25 de julio al 24 de agosto, ambos inclusive.
      expect(period.totalDays, 31);
    });

    test('un periodo de un solo dia dura un dia', () {
      final unDia = BudgetPeriod(
        start: DateTime(2026, 7, 25),
        end: DateTime(2026, 7, 25),
      );
      expect(unDia.totalDays, 1);
      expect(unDia.daysRemainingFrom(DateTime(2026, 7, 25)), 1);
    });

    test('transcurridos mas restantes suman el total mas uno', () {
      for (var d = 0; d < period.totalDays; d++) {
        final hoy = period.start.add(Duration(days: d));
        expect(
          period.elapsedDaysAt(hoy) + period.daysRemainingFrom(hoy),
          period.totalDays + 1,
        );
      }
    });

    test('una fecha anterior al periodo se acota al principio', () {
      expect(period.daysRemainingFrom(DateTime(2026)), period.totalDays);
      expect(period.elapsedDaysAt(DateTime(2026)), 1);
    });

    test('contains incluye los dos extremos y excluye lo de fuera', () {
      expect(period.contains(DateTime(2026, 7, 25)), isTrue);
      expect(period.contains(DateTime(2026, 8, 24)), isTrue);
      expect(period.contains(DateTime(2026, 7, 24)), isFalse);
      expect(period.contains(DateTime(2026, 8, 25)), isFalse);
    });

    test('contains ignora la hora del dia', () {
      // Una compra de las 23:30 del último día sigue siendo del último día.
      expect(period.contains(DateTime(2026, 8, 24, 23, 30)), isTrue);
      expect(period.contains(DateTime(2026, 7, 25, 0, 1)), isTrue);
    });

    test('la igualdad es por valor', () {
      // Sin esto, `shouldRepaint` compara identidad y repinta siempre.
      final otro = BudgetPeriod(
        start: DateTime(2026, 7, 25),
        end: DateTime(2026, 8, 24),
      );
      expect(period, equals(otro));
      expect(period.hashCode, otro.hashCode);
      expect(
        period,
        isNot(
          equals(
            BudgetPeriod(
              start: DateTime(2026, 7, 26),
              end: DateTime(2026, 8, 24),
            ),
          ),
        ),
      );
    });

    test('positionOf se mantiene entre cero y uno', () {
      expect(period.positionOf(DateTime(2026, 8, 9)), closeTo(0.5, 0.02));
      expect(period.positionOf(DateTime(2026, 12)), 1.0);
    });

    test('positionOf de un periodo de un dia no divide por cero', () {
      final unDia = BudgetPeriod(
        start: DateTime(2026, 7, 25),
        end: DateTime(2026, 7, 25),
      );
      expect(unDia.positionOf(DateTime(2026, 7, 25)), 0.0);
    });
  });

  group('DateLabel', () {
    test('la cuenta atras distingue singular, plural y cierre', () {
      // Es texto que se lee en voz alta en la pantalla principal: «último día»
      // no es lo mismo que «faltan 1 días».
      expect(DateLabel.countdown(12), 'faltan 12 días');
      expect(DateLabel.countdown(1), 'último día');
      expect(DateLabel.countdown(0), 'período cerrado');
      expect(DateLabel.countdown(-3), 'período cerrado');
    });

    test('los formatos de fecha no lanzan por falta de datos regionales', () {
      // `DateFormat` con una configuración regional sin cargar lanza en
      // tiempo de ejecución, y estas tres se llaman al pintar el panel: sería
      // una pantalla en blanco, no un texto feo.
      final fecha = DateTime(2026, 8, 9);

      expect(DateLabel.long(fecha), isNotEmpty);
      expect(DateLabel.short(fecha), isNotEmpty);
      expect(DateLabel.weekday(fecha), isNotEmpty);
    });
  });
}
