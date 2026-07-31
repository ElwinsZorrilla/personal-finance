import 'package:flutter/widgets.dart';

/// Paleta y escalas del sistema visual.
///
/// Regla que gobierna toda la paleta: **el color es un sistema de aviso, no
/// decoración.** Un período sano se ve casi monocromo. Cuando aparece color,
/// significa que algo pide atención. Ningún componente puede introducir un
/// color fuera de [Signal] sin una razón semántica.
abstract final class Tone {
  /// Fondo base. Azul petróleo profundo, no negro: el negro puro sobre OLED
  /// hace que los números grandes vibren al hacer scroll.
  static const ink = Color(0xFF0B1418);

  /// Superficie elevada (tarjetas, hojas, filas agrupadas).
  static const surface = Color(0xFF121F25);

  /// Superficie elevada un segundo nivel (campos, chips).
  static const surfaceRaised = Color(0xFF18282F);

  /// Filetes de 1 lógico px. Nunca usar como texto.
  static const line = Color(0xFF23373F);

  /// Texto y cifras principales. Hueso cálido, no blanco puro:
  /// baja la fatiga en lectura nocturna y da temperatura al fondo frío.
  static const bone = Color(0xFFEDE7DC);

  /// Texto secundario: etiquetas, fechas, comercios en listas densas.
  static const muted = Color(0xFF7E939B);

  /// Texto terciario y estados vacíos.
  static const faint = Color(0xFF4E656E);
}

/// Colores con significado. Solo aparecen cuando hay algo que atender.
abstract final class Signal {
  /// Ritmo por encima de lo esperado. Aún no es un problema.
  static const caution = Color(0xFFD9A441);

  /// Riesgo real: no alcanza, se excedió, posible duplicado.
  static const risk = Color(0xFFB4462F);

  /// Dinero que entra: ingreso, devolución, reverso.
  static const credit = Color(0xFF4E8C7A);
}

/// Escala de espaciado en base 4. Usar siempre las constantes, nunca literales.
abstract final class Space {
  static const xs = 4.0;
  static const sm = 8.0;
  static const md = 12.0;
  static const lg = 16.0;
  static const xl = 24.0;
  static const xxl = 32.0;
  static const xxxl = 48.0;

  /// Margen lateral de pantalla. Único valor permitido para el gutter.
  static const gutter = 20.0;
}

abstract final class Radii {
  static const sm = Radius.circular(6);
  static const md = Radius.circular(12);
  static const lg = Radius.circular(18);

  static const brSm = BorderRadius.all(sm);
  static const brMd = BorderRadius.all(md);
  static const brLg = BorderRadius.all(lg);
}

abstract final class Motion {
  /// Único momento orquestado de la app: la entrada del panel.
  static const entrance = Duration(milliseconds: 820);
  static const quick = Duration(milliseconds: 160);
  static const settle = Duration(milliseconds: 280);

  static const ease = Curves.easeOutCubic;
}
