import 'dart:ui' show FontFeature;

import 'package:flutter/widgets.dart';
import 'package:google_fonts/google_fonts.dart';

import 'tokens.dart';

/// Tres roles tipográficos, cada uno con un trabajo distinto:
///
/// * **display** — Bricolage Grotesque. Solo para la cifra principal y los
///   títulos de pantalla. Usado con restricción: si aparece en más de dos
///   lugares por pantalla, deja de significar algo.
/// * **body** — Public Sans. Toda la interfaz: etiquetas, botones, prosa.
/// * **data** — IBM Plex Mono. Montos en listas, fechas, referencias
///   bancarias, últimos cuatro dígitos. Alinea columnas sin tabular hacks y
///   viene del mismo mundo que los estados de cuenta que la app consume.
///
/// Todas las cifras usan `tabularFigures`: un monto que cambia de 999 a 1,000
/// no debe desplazar la fila.
///
/// **Las tres familias se empaquetan en `assets/fonts/`.** Esta app se abre en
/// la calle, a veces sin señal, y la primera cifra que muestra no puede
/// depender de una descarga. `google_fonts` queda solo como conveniencia de
/// desarrollo, con la descarga en tiempo de ejecución desactivada: si un
/// archivo falta, se nota en la primera ejecución y no en producción.
abstract final class Type {
  /// Se llama una vez desde `main`, antes de construir el primer widget.
  static void bootstrap() {
    GoogleFonts.config.allowRuntimeFetching = false;
  }

  static const _tabular = [FontFeature.tabularFigures()];

  static TextStyle display(double size, {FontWeight weight = FontWeight.w600}) =>
      GoogleFonts.bricolageGrotesque(
        fontSize: size,
        fontWeight: weight,
        height: 1.0,
        letterSpacing: size * -0.028,
        color: Tone.bone,
        fontFeatures: _tabular,
      );

  static TextStyle body(
    double size, {
    FontWeight weight = FontWeight.w400,
    Color color = Tone.bone,
    double height = 1.42,
  }) =>
      GoogleFonts.publicSans(
        fontSize: size,
        fontWeight: weight,
        height: height,
        color: color,
        fontFeatures: _tabular,
      );

  static TextStyle data(
    double size, {
    FontWeight weight = FontWeight.w400,
    Color color = Tone.bone,
  }) =>
      GoogleFonts.ibmPlexMono(
        fontSize: size,
        fontWeight: weight,
        height: 1.2,
        color: color,
        fontFeatures: _tabular,
      );

  /// Etiqueta de sección. Versalita ancha: separa bloques sin necesidad de
  /// líneas ni tarjetas.
  static TextStyle eyebrow({Color color = Tone.muted}) => GoogleFonts.ibmPlexMono(
        fontSize: 11,
        fontWeight: FontWeight.w500,
        height: 1.0,
        letterSpacing: 1.6,
        color: color,
      );
}
