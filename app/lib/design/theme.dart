import 'package:flutter/material.dart';

import 'tokens.dart';
import 'typography.dart';

/// Tema único de la app. No hay modo claro: la app se consulta de noche, en la
/// calle, en tres segundos. Un solo tema es un compromiso de mantenimiento
/// menor y una decisión de producto, no una limitación.
ThemeData buildTheme() {
  const scheme = ColorScheme.dark(
    primary: Tone.bone,
    onPrimary: Tone.ink,
    secondary: Tone.muted,
    onSecondary: Tone.ink,
    surface: Tone.surface,
    onSurface: Tone.bone,
    error: Signal.risk,
    onError: Tone.bone,
    outline: Tone.line,
  );

  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    scaffoldBackgroundColor: Tone.ink,
    canvasColor: Tone.ink,
    splashFactory: InkSparkle.splashFactory,
    dividerTheme: const DividerThemeData(
      color: Tone.line,
      thickness: 1,
      space: 1,
    ),
    textTheme: TextTheme(
      displayLarge: Type.display(56),
      headlineMedium: Type.display(24),
      titleMedium: Type.body(15, weight: FontWeight.w600),
      bodyMedium: Type.body(14),
      bodySmall: Type.body(13, color: Tone.muted),
      labelSmall: Type.eyebrow(),
    ),
    appBarTheme: AppBarTheme(
      backgroundColor: Tone.ink,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      centerTitle: false,
      titleTextStyle: Type.display(20),
    ),
    // **Red de seguridad, no la forma de trabajar.** Los campos de la app son
    // `FieldLine`, construido sobre `widgets`. Esto existe para que un
    // `TextField` de Material que se cuele en el futuro no llegue con su caja
    // rellena y su etiqueta flotante, que es exactamente lo que hacía que las
    // dos primeras pantallas se leyeran como un formulario web.
    inputDecorationTheme: InputDecorationTheme(
      filled: false,
      contentPadding: const EdgeInsets.symmetric(vertical: Space.md),
      border: const UnderlineInputBorder(
        borderSide: BorderSide(color: Tone.line),
      ),
      enabledBorder: const UnderlineInputBorder(
        borderSide: BorderSide(color: Tone.line),
      ),
      focusedBorder: const UnderlineInputBorder(
        borderSide: BorderSide(color: Tone.bone),
      ),
      labelStyle: Type.eyebrow(),
      floatingLabelStyle: Type.eyebrow(color: Tone.bone),
      hintStyle: Type.body(15, color: Tone.faint),
      helperStyle: Type.body(12, color: Tone.faint),
      errorStyle: Type.body(12, color: Signal.risk),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: Tone.bone,
        foregroundColor: Tone.ink,
        minimumSize: const Size(0, 48),
        shape: const RoundedRectangleBorder(borderRadius: Radii.brSm),
        textStyle: Type.body(15, weight: FontWeight.w600),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: Tone.bone,
        minimumSize: const Size(0, 44),
        textStyle: Type.body(14, weight: FontWeight.w600),
      ),
    ),
  );
}
