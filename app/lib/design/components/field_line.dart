import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

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
          Row(
            crossAxisAlignment: CrossAxisAlignment.baseline,
            textBaseline: TextBaseline.alphabetic,
            children: [
              if (widget.prefix != null) ...[
                Text(widget.prefix!, style: Type.data(17, color: Tone.faint)),
                const SizedBox(width: Space.sm),
              ],
              Expanded(
                child: EditableText(
                  controller: widget.controller,
                  focusNode: _focus,
                  style: estilo,
                  cursorColor: Tone.bone,
                  backgroundCursorColor: Tone.line,
                  readOnly: !widget.enabled,
                  autofocus: widget.autofocus,
                  keyboardType: widget.keyboardType,
                  inputFormatters: widget.inputFormatters,
                  onSubmitted: widget.onSubmitted,
                  textInputAction: widget.onSubmitted == null
                      ? TextInputAction.next
                      : TextInputAction.go,
                  selectionColor: Tone.line,
                  // Ni corrector ni sugerencias: aquí se escriben nombres de
                  // cuenta, dígitos y cifras. El corrector solo puede estorbar.
                  autocorrect: false,
                  enableSuggestions: false,
                ),
              ),
            ],
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
