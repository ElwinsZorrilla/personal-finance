import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../tokens.dart';
import '../typography.dart';

/// Un campo de escritura del sistema visual de la app.
///
/// **No es un `TextField` de Material con otro color.** Los de Material llegan
/// con caja rellena, etiqueta flotante y subrayado grueso, y ese conjunto es
/// exactamente lo que se lee como «un formulario web pegado en una página». El
/// resto de la app no tiene una sola tarjeta ni una sola caja: es una superficie
/// continua separada por versalitas y filetes de un píxel. Un campo tenía que
/// hablar ese idioma.
///
/// La etiqueta va **arriba y fija**, no flotando: flotar es una animación que se
/// ve decenas de veces al configurar y no aporta nada, y deja el campo sin
/// etiqueta mientras se escribe.
///
/// El filete de abajo se ilumina al enfocar. Es el único movimiento del
/// componente y sí sirve: dice dónde va a caer lo que se escriba.
class FieldLine extends StatefulWidget {
  const FieldLine({
    super.key,
    required this.label,
    required this.controller,
    this.hint,
    this.prefix,
    this.help,
    this.enabled = true,
    this.mono = false,
    this.keyboardType,
    this.inputFormatters,
    this.onSubmitted,
    this.autofocus = false,
  });

  final String label;
  final TextEditingController controller;
  final String? hint;

  /// Va pegado a la cifra, en tono apagado. `RD$` no es parte de lo que se
  /// escribe y no debe parecerlo.
  final String? prefix;

  /// Una línea que explica el campo cuando el nombre no basta.
  final String? help;

  final bool enabled;

  /// Monoespaciada para cifras y dígitos: alinea y viene del mismo mundo que
  /// los estados de cuenta que la app lee.
  final bool mono;

  final TextInputType? keyboardType;
  final List<TextInputFormatter>? inputFormatters;
  final ValueChanged<String>? onSubmitted;
  final bool autofocus;

  @override
  State<FieldLine> createState() => _FieldLineState();
}

class _FieldLineState extends State<FieldLine> {
  final _focus = FocusNode();

  @override
  void initState() {
    super.initState();
    _focus.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _focus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final activo = _focus.hasFocus && widget.enabled;

    // 17 puntos y no menos. Por debajo de 16, Safari en iOS **hace zoom** al
    // enfocar un campo, y esa sacudida es de las cosas que más delatan que algo
    // se está viendo en un navegador.
    final estilo = widget.mono
        ? Type.data(17, color: widget.enabled ? Tone.bone : Tone.faint)
        : Type.body(17, color: widget.enabled ? Tone.bone : Tone.faint);

    return Padding(
      padding: const EdgeInsets.only(bottom: Space.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            widget.label.toUpperCase(),
            style: Type.eyebrow(color: activo ? Tone.bone : Tone.muted),
          ),
          const SizedBox(height: Space.md),

          // Toda la fila enfoca, no solo el texto. Un campo de una línea es un
          // blanco diminuto para un pulgar, y fallar el toque en el campo del
          // código de alta es fallar la única acción de esa pantalla.
          GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: widget.enabled ? _focus.requestFocus : null,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.baseline,
              textBaseline: TextBaseline.alphabetic,
              children: [
                if (widget.prefix != null) ...[
                  Text(widget.prefix!, style: Type.data(17, color: Tone.faint)),
                  const SizedBox(width: Space.sm),
                ],
                Expanded(
                  // **`TextField` y no `EditableText`.** La primera versión usaba
                  // el segundo, que es la primitiva de dibujo del texto y nada
                  // más: no enfoca al tocarlo, no ofrece el menú de pegar y no
                  // maneja la selección.
                  //
                  // El resultado fue que **no se podía escribir el código de
                  // alta**: el campo se veía perfecto, el cursor parpadeaba por el
                  // `autofocus`, y al tocarlo no pasaba nada. Pegar, que es lo que
                  // se hace con un código, era directamente imposible.
                  //
                  // Lo que hacía sospechoso a `TextField` era su decoración de
                  // fábrica —caja rellena, etiqueta flotante—, y eso se quita con
                  // `InputDecoration.collapsed`. El aspecto es el mismo; los
                  // gestos vuelven.
                  //
                  // **`Material` transparente.** `TextField` lo exige como
                  // ancestro —lo usa para la tinta y para el tema de selección— y
                  // las pantallas de esta app están hechas sobre `widgets`, sin
                  // `Scaffold`. Ponerlo aquí deja el componente autosuficiente en
                  // vez de obligar a cada pantalla a arrastrar un `Scaffold` que
                  // no necesita para nada más.
                  //
                  // Transparente: no pinta nada. Lo único que aporta es el
                  // contexto que `TextField` va a buscar.
                  child: Material(
                    type: MaterialType.transparency,
                    child: TextField(
                      controller: widget.controller,
                      focusNode: _focus,
                      style: estilo,
                      cursorColor: Tone.bone,
                      enabled: widget.enabled,
                      autofocus: widget.autofocus,
                      keyboardType: widget.keyboardType,
                      inputFormatters: widget.inputFormatters,
                      onSubmitted: widget.onSubmitted,
                      textInputAction: widget.onSubmitted == null
                          ? TextInputAction.next
                          : TextInputAction.go,
                      // Ni corrector ni sugerencias: aquí se escriben nombres de
                      // cuenta, dígitos y cifras. El corrector solo puede estorbar.
                      autocorrect: false,
                      enableSuggestions: false,
                      decoration:
                          const InputDecoration.collapsed(hintText: null),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: Space.md),
          AnimatedContainer(
            duration: Motion.quick,
            curve: Motion.ease,
            height: 1,
            color: activo ? Tone.bone : Tone.line,
          ),
          if (widget.hint != null && widget.controller.text.isEmpty) ...[
            const SizedBox(height: Space.sm),
            Text(widget.hint!, style: Type.body(12, color: Tone.faint)),
          ] else if (widget.help != null) ...[
            const SizedBox(height: Space.sm),
            Text(widget.help!, style: Type.body(12, color: Tone.faint)),
          ],
        ],
      ),
    );
  }
}
