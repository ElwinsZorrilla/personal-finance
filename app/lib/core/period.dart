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

/// Fechas en prosa, en español.
///
/// No usa `DateFormat`. Con una configuración regional que no sea la de por
/// defecto, `intl` exige llamar antes a `initializeDateFormatting` y, si no se
/// llama, **lanza al formatear**. Estos tres formatos se invocan al pintar el
/// panel, así que el síntoma no habría sido una fecha fea: habría sido una
/// pantalla en blanco.
///
/// La app tiene un solo idioma y un solo país. Doce nombres de mes y siete de
/// día escritos aquí no pueden faltar, no dependen de que alguien recuerde
/// inicializar nada y no arrastran datos regionales al binario.
abstract final class DateLabel {
  static const _months = [
    'enero',
    'febrero',
    'marzo',
    'abril',
    'mayo',
    'junio',
    'julio',
    'agosto',
    'septiembre',
    'octubre',
    'noviembre',
    'diciembre',
  ];

  static const _monthsShort = [
    'ene',
    'feb',
    'mar',
    'abr',
    'may',
    'jun',
    'jul',
    'ago',
    'sep',
    'oct',
    'nov',
    'dic',
  ];

  /// `DateTime.weekday` va de 1 (lunes) a 7 (domingo).
  static const _weekdaysShort = [
    'lun',
    'mar',
    'mié',
    'jue',
    'vie',
    'sáb',
    'dom',
  ];

  /// `9 de agosto`.
  static String long(DateTime d) => '${d.day} de ${_months[d.month - 1]}';

  /// `9 ago`.
  static String short(DateTime d) => '${d.day} ${_monthsShort[d.month - 1]}';

  /// `dom 9 ago`.
  static String weekday(DateTime d) =>
      '${_weekdaysShort[d.weekday - 1]} ${short(d)}';

  /// `faltan 12 días` / `falta 1 día` / `último día`.
  static String countdown(int days) => switch (days) {
        <= 0 => 'período cerrado',
        1 => 'último día',
        _ => 'faltan $days días',
      };
}
