import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:margen/design/components/field_line.dart';
import 'package:margen/design/theme.dart';

/// El campo de escritura del sistema visual.
///
/// Estas pruebas existen por un fallo que dejó la app **inservible**: el campo
/// estaba construido sobre `EditableText`, que es la primitiva de dibujo del
/// texto y no maneja gestos. Se veía perfecto y al tocarlo no pasaba nada, así
/// que no había forma de escribir el código de alta ni de pegarlo.
///
/// Ninguna prueba lo detectó porque todas las demás usan `enterText`, que
/// **enfoca el campo por su cuenta** y por tanto no comprueba lo único que
/// estaba roto.
void main() {
  Future<void> pintar(WidgetTester tester, TextEditingController c) =>
      tester.pumpWidget(
        MaterialApp(
          theme: buildTheme(),
          home: Scaffold(
            body: FieldLine(label: 'Código de alta', controller: c),
          ),
        ),
      );

  testWidgets('al tocarlo se enfoca y admite escritura', (tester) async {
    final controller = TextEditingController();
    addTearDown(controller.dispose);

    await pintar(tester, controller);

    // El toque es lo que fallaba. `enterText` habría enfocado solo y la prueba
    // habría pasado con el campo roto.
    await tester.tap(find.byType(TextField));
    await tester.pumpAndSettle();

    final campo = tester.widget<TextField>(find.byType(TextField));

    expect(
      campo.focusNode!.hasFocus,
      isTrue,
      reason: 'tocar el campo tiene que enfocarlo',
    );

    await tester.enterText(find.byType(TextField), 'un-codigo-de-alta');
    expect(controller.text, 'un-codigo-de-alta');
  });

  testWidgets('deja seleccionar para poder pegar', (tester) async {
    // Un código de alta se pega, no se teclea. Sin selección interactiva no hay
    // menú de pegar, que era el otro efecto del mismo fallo.
    final controller = TextEditingController();
    addTearDown(controller.dispose);

    await pintar(tester, controller);

    final campo = tester.widget<TextField>(find.byType(TextField));

    expect(campo.enableInteractiveSelection, isNot(false));
  });

  testWidgets('deshabilitado no se enfoca', (tester) async {
    final controller = TextEditingController();
    addTearDown(controller.dispose);

    await tester.pumpWidget(
      MaterialApp(
        theme: buildTheme(),
        home: Scaffold(
          body: FieldLine(
            label: 'Código de alta',
            controller: controller,
            enabled: false,
          ),
        ),
      ),
    );

    await tester.tap(find.byType(TextField), warnIfMissed: false);
    await tester.pumpAndSettle();

    final campo = tester.widget<TextField>(find.byType(TextField));

    expect(campo.focusNode!.hasFocus, isFalse);
  });
}
