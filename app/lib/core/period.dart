import 'package:intl/intl.dart';

/// Período presupuestario. No va del 1 al 30: va de un ingreso al siguiente,
/// porque ese es el ciclo real del dinero de una persona asalariada.
class BudgetPeriod {
  const BudgetPeriod({required this.start, required this.end});

  final DateTime start;
  final DateTime end;

  int get totalDays => end.difference(start).inDays + 1;

  /// Días que faltan contando hoy. Nunca menos de 1: dividir el disponible
  /// entre cero produce infinito y una pantalla rota el último día del ciclo.
  int daysRemainingFrom(DateTime today) {
    final remaining = end.difference(_dateOnly(today)).inDays + 1;
    return remaining.clamp(1, totalDays);
  }

  int elapsedDaysAt(DateTime today) => totalDays - daysRemainingFrom(today) + 1;

  /// Fracción del período consumida, entre 0 y 1. Es la base del cálculo de
  /// ritmo: gastar 60 % del presupuesto con 40 % del período transcurrido es
  /// la señal que la app vigila.
  double progressAt(DateTime today) =>
      (elapsedDaysAt(today) / totalDays).clamp(0.0, 1.0);

  /// Posición horizontal de una fecha dentro del período, entre 0 y 1.
  /// La usa el riel de autonomía para ubicar hoy y la fecha de agotamiento.
  double positionOf(DateTime date) {
    final offset = _dateOnly(date).difference(start).inDays;
    if (totalDays <= 1) return 0;
    return (offset / (totalDays - 1)).clamp(0.0, 1.0);
  }

  bool contains(DateTime date) {
    final d = _dateOnly(date);
    return !d.isBefore(_dateOnly(start)) && !d.isAfter(_dateOnly(end));
  }

  static DateTime _dateOnly(DateTime d) => DateTime(d.year, d.month, d.day);

  @override
  bool operator ==(Object other) =>
      other is BudgetPeriod && other.start == start && other.end == end;

  @override
  int get hashCode => Object.hash(start, end);
}

abstract final class DateLabel {
  static final _dayMonth = DateFormat("d 'de' MMMM", 'es_DO');
  static final _shortDay = DateFormat('d MMM', 'es_DO');
  static final _weekday = DateFormat('EEE d MMM', 'es_DO');

  static String long(DateTime d) => _dayMonth.format(d);
  static String short(DateTime d) => _shortDay.format(d);
  static String weekday(DateTime d) => _weekday.format(d);

  /// `faltan 12 días` / `falta 1 día` / `último día`.
  static String countdown(int days) => switch (days) {
        <= 0 => 'período cerrado',
        1 => 'último día',
        _ => 'faltan $days días',
      };
}
