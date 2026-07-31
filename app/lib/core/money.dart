import 'package:intl/intl.dart';

/// Formato en el que un origen externo entrega los montos.
enum AmountFormat {
  /// `2,450.00` — coma como separador de miles. Formato del banco y del CSV.
  commaThousands,

  /// `2.450,00` — punto como separador de miles.
  dotThousands,
}

/// Cantidad monetaria en unidades menores (centavos).
///
/// Nunca se usa `double` para dinero. Un `double` no puede representar 0.10
/// exactamente; sumar cien transacciones de RD$0.10 produce
/// 9.999999999999998 y una conciliación que no cuadra por un centavo. Toda la
/// aritmética ocurre sobre enteros y la conversión a texto es el último paso.
///
/// Es una clase y no un `extension type` a propósito: se necesita identidad en
/// tiempo de ejecución para que un `int` crudo no pueda colarse por un
/// deserializador ni por una firma de función.
final class Money implements Comparable<Money> {
  const Money(this.cents);

  const Money.fromUnits(int units) : cents = units * 100;

  /// Convierte texto de un origen externo. El formato es obligatorio: adivinar
  /// entre `1.234` (mil doscientos treinta y cuatro) y `1.234` (uno con
  /// doscientos treinta y cuatro milésimas) es una fuente de errores callados.
  factory Money.parse(String raw, {required AmountFormat format}) {
    var cleaned = raw.replaceAll(RegExp(r'[^0-9.,\-]'), '');
    cleaned = switch (format) {
      AmountFormat.commaThousands => cleaned.replaceAll(',', ''),
      AmountFormat.dotThousands =>
        cleaned.replaceAll('.', '').replaceAll(',', '.'),
    };

    final negative = cleaned.startsWith('-');
    if (negative) cleaned = cleaned.substring(1);

    final parts = cleaned.split('.');
    if (parts.length > 2 || parts.first.isEmpty) {
      throw FormatException('Monto no interpretable', raw);
    }
    final units = int.tryParse(parts.first);
    if (units == null) throw FormatException('Monto no interpretable', raw);

    var fraction = 0;
    if (parts.length == 2) {
      final decimals = parts[1].padRight(2, '0');
      if (decimals.length > 2) {
        throw FormatException('Más de dos decimales', raw);
      }
      final parsed = int.tryParse(decimals);
      if (parsed == null) throw FormatException('Monto no interpretable', raw);
      fraction = parsed;
    }

    final total = units * 100 + fraction;
    return Money(negative ? -total : total);
  }

  static const zero = Money(0);

  final int cents;

  Money operator +(Money other) => Money(cents + other.cents);
  Money operator -(Money other) => Money(cents - other.cents);
  Money operator -() => Money(-cents);

  /// Multiplicación por un factor (proyecciones, porcentajes de tolerancia).
  /// El redondeo es explícito para que no ocurra en silencio.
  Money scaled(double factor) => Money((cents * factor).round());

  bool operator >(Money other) => cents > other.cents;
  bool operator <(Money other) => cents < other.cents;
  bool operator >=(Money other) => cents >= other.cents;
  bool operator <=(Money other) => cents <= other.cents;

  bool get isNegative => cents < 0;
  bool get isZero => cents == 0;
  Money get abs => Money(cents.abs());

  /// Reparte una cantidad en [parts] partes sin perder ni inventar centavos.
  /// Los centavos sobrantes van a las primeras partes.
  List<Money> split(int parts) {
    if (parts <= 0) {
      throw ArgumentError.value(parts, 'parts', 'debe ser mayor que cero');
    }
    final base = cents ~/ parts;
    final remainder = cents.remainder(parts).abs();
    final sign = cents.isNegative ? -1 : 1;
    return List.generate(
      parts,
      (i) => Money(base + (i < remainder ? sign : 0)),
      growable: false,
    );
  }

  @override
  int compareTo(Money other) => cents.compareTo(other.cents);

  @override
  bool operator ==(Object other) => other is Money && other.cents == cents;

  @override
  int get hashCode => cents.hashCode;

  @override
  String toString() => MoneyFormat.exact(this);
}

/// Formateo para República Dominicana: `RD$` antepuesto, coma como separador
/// de miles, punto decimal.
abstract final class MoneyFormat {
  static final _whole = NumberFormat('#,##0', 'es_DO');
  static final _precise = NumberFormat('#,##0.00', 'es_DO');

  /// Formato de pantalla: `RD$ 12,000`. Los centavos se omiten cuando son
  /// cero porque en la vista de decisión sobran; la vista de detalle usa
  /// [exact].
  ///
  /// El signo se antepone al resultado ya formateado en valor absoluto: dejar
  /// que el formateador coloque el menos hace que la posición dependa de la
  /// configuración regional del dispositivo.
  static String display(Money m) => '${_sign(m)}RD\$ ${bare(m)}';

  /// Formato de auditoría: siempre dos decimales.
  static String exact(Money m) =>
      '${_sign(m)}RD\$ ${_precise.format(m.cents.abs() / 100)}';

  /// Solo la cifra en valor absoluto, sin símbolo ni signo. Para columnas
  /// donde la moneda ya está en el encabezado.
  static String bare(Money m) {
    final abs = m.cents.abs();
    return abs.remainder(100) == 0
        ? _whole.format(abs ~/ 100)
        : _precise.format(abs / 100);
  }

  static String _sign(Money m) => m.isNegative ? '-' : '';
}
