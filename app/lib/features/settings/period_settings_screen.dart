import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

import '../../core/money.dart';
import '../../core/period.dart';
import '../../data/api_client.dart';
import '../../data/setup_repository.dart';
import '../../design/components/action_button.dart';
import '../../design/components/choice_row.dart';
import '../../design/components/error_note.dart';
import '../../design/components/field_line.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';
import 'accounts_screen.dart';

/// Corregir el ciclo de cobro sin esperar a que termine el período.
///
/// Existe por un caso concreto: el período de producción se abrió con un solo
/// cobro el 15, antes de que existieran los dos, y `POST /setup/periods` es
/// idempotente —devuelve el que hay en vez de cambiarlo—. Sin esta pantalla, un
/// período con el calendario equivocado no se podía arreglar hasta que
/// terminara.
///
/// **Enseña las fechas resultantes antes de aplicar nada.** Cambiar el
/// calendario recalcula el período, y al acortarse los movimientos anteriores al
/// inicio nuevo salen de él. Eso es correcto —pertenecen al ciclo anterior— pero
/// no puede ser una sorpresa.
class PeriodSettingsScreen extends StatefulWidget {
  const PeriodSettingsScreen({
    super.key,
    required this.repository,
    required this.onDone,
  });

  final SetupRepository repository;

  /// Se llama al guardar y al salir sin guardar. Quien la abrió decide qué
  /// hacer: aquí no se sabe si hay que recargar el panel.
  final VoidCallback onDone;

  @override
  State<PeriodSettingsScreen> createState() => _PeriodSettingsScreenState();
}

class _PeriodSettingsScreenState extends State<PeriodSettingsScreen> {
  final _dia = TextEditingController();
  final _segundoDia = TextEditingController(text: '31');
  final _ingreso = TextEditingController();

  _Frecuencia _frecuencia = _Frecuencia.quincenal;
  OpenPeriod? _actual;
  bool _cargando = true;
  bool _guardando = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_cargar());
  }

  @override
  void dispose() {
    _dia.dispose();
    _segundoDia.dispose();
    _ingreso.dispose();
    super.dispose();
  }

  Future<void> _cargar() async {
    try {
      final periodo = await widget.repository.currentPeriod();

      if (!mounted) return;

      setState(() {
        _actual = periodo;
        _cargando = false;

        // Se rellena con lo que hay. Un formulario vacío obligaría a reescribir
        // el ingreso para cambiar solo los días de cobro, y reescribir una cifra
        // de dinero es una ocasión de equivocarse que no hacía falta crear.
        if (periodo != null) {
          _ingreso.text = MoneyFormat.bare(periodo.expectedIncome);
        }
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _cargando = false;
        _error = failure.message;
      });
    }
  }

  Future<void> _guardar() async {
    final dias = <int>[];

    final primero = int.tryParse(_dia.text.trim());
    if (primero == null || primero < 1 || primero > 31) {
      setState(() => _error = 'El día de cobro va de 1 a 31.');
      return;
    }
    dias.add(primero);

    if (_frecuencia == _Frecuencia.quincenal) {
      final segundo = int.tryParse(_segundoDia.text.trim());
      if (segundo == null || segundo < 1 || segundo > 31) {
        setState(() => _error = 'El segundo día de cobro va de 1 a 31.');
        return;
      }
      if (segundo == primero) {
        setState(
          () => _error = 'Los dos días de cobro no pueden ser el mismo.',
        );
        return;
      }
      dias.add(segundo);
    }

    final ingreso = leerMonto(_ingreso.text);
    if (ingreso == null || ingreso.cents <= 0) {
      setState(
        () => _error = 'El ingreso tiene que ser mayor que cero.',
      );
      return;
    }

    setState(() {
      _guardando = true;
      _error = null;
    });

    try {
      final periodo = await widget.repository.updatePeriod(
        payDays: dias,
        expectedIncome: ingreso,
      );

      if (!mounted) return;

      setState(() {
        _actual = periodo;
        _guardando = false;
      });

      unawaited(HapticFeedback.mediumImpact());
      widget.onDone();
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      unawaited(HapticFeedback.heavyImpact());
      setState(() {
        _guardando = false;
        _error = failure.message;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final relleno = MediaQuery.paddingOf(context);
    final actual = _actual;

    return ColoredBox(
      color: Tone.ink,
      child: SafeArea(
        child: ListView(
          padding: EdgeInsets.only(
            left: Space.gutter,
            right: Space.gutter,
            top: relleno.top + Space.xxl,
            bottom: Space.xxxl,
          ),
          children: [
            Text('AJUSTES', style: Type.eyebrow()),
            const SizedBox(height: Space.lg),
            Text('Tu ciclo\nde cobro', style: Type.display(32)),
            const SizedBox(height: Space.lg),
            Text(
              'Cambiar esto recalcula el período que está abierto. Los '
              'movimientos anteriores al inicio nuevo pasan al ciclo anterior.',
              style: Type.body(15, color: Tone.muted),
            ),
            const SizedBox(height: Space.xl),
            if (_cargando)
              Text(
                'Leyendo el período…',
                style: Type.body(14, color: Tone.faint),
              )
            else if (actual == null)
              Text(
                'No hay ningún período abierto que contenga hoy.',
                style: Type.body(14, color: Tone.faint),
              )
            else ...[
              _Ahora(periodo: actual),
              const SizedBox(height: Space.xl),
              ChoiceRow<_Frecuencia>(
                label: 'Cada cuánto cobras',
                selected: _frecuencia,
                enabled: !_guardando,
                options: [for (final f in _Frecuencia.values) (f, f.label)],
                onChanged: (valor) => setState(() {
                  _frecuencia = valor;
                  if (valor == _Frecuencia.quincenal && _dia.text.isEmpty) {
                    _dia.text = '15';
                  }
                }),
              ),
              FieldLine(
                label: _frecuencia == _Frecuencia.quincenal
                    ? 'Primer cobro'
                    : 'Día de cobro',
                controller: _dia,
                mono: true,
                enabled: !_guardando,
                keyboardType: TextInputType.number,
                inputFormatters: [
                  FilteringTextInputFormatter.digitsOnly,
                  LengthLimitingTextInputFormatter(2),
                ],
              ),
              if (_frecuencia == _Frecuencia.quincenal)
                FieldLine(
                  label: 'Segundo cobro',
                  controller: _segundoDia,
                  mono: true,
                  enabled: !_guardando,
                  keyboardType: TextInputType.number,
                  inputFormatters: [
                    FilteringTextInputFormatter.digitsOnly,
                    LengthLimitingTextInputFormatter(2),
                  ],
                  help: 'Fin de mes se escribe 31, y se ajusta a cada mes',
                ),
              FieldLine(
                label: _frecuencia == _Frecuencia.quincenal
                    ? 'Lo que cobras cada quincena'
                    : 'Ingreso del período',
                controller: _ingreso,
                mono: true,
                prefix: r'RD$',
                enabled: !_guardando,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                help: _frecuencia == _Frecuencia.quincenal
                    ? 'De un cobro, no del mes entero'
                    : null,
              ),
              if (_error != null) ...[
                ErrorNote(_error!),
                const SizedBox(height: Space.lg),
              ],
              ActionButton(
                label: 'Guardar',
                busyLabel: 'Guardando…',
                busy: _guardando,
                onPressed: _guardar,
              ),
            ],
            const SizedBox(height: Space.md),
            ActionButton(
              label: 'Volver',
              quiet: true,
              onPressed: widget.onDone,
            ),
          ],
        ),
      ),
    );
  }
}

/// Cómo está el período ahora mismo, antes de tocar nada.
class _Ahora extends StatelessWidget {
  const _Ahora({required this.periodo});

  final OpenPeriod periodo;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('AHORA', style: Type.eyebrow(color: Tone.faint)),
        const SizedBox(height: Space.sm),
        Text(
          'Del ${DateLabel.long(periodo.startDate)} al '
          '${DateLabel.long(periodo.endDate)}',
          style: Type.body(14),
        ),
        const SizedBox(height: 2),
        Text(
          '${periodo.totalDays} días · '
          '${MoneyFormat.display(periodo.expectedIncome)} de ingreso',
          style: Type.data(12, color: Tone.muted),
        ),
      ],
    );
  }
}

/// Cada cuánto entra el sueldo.
enum _Frecuencia {
  mensual('Una vez al mes'),
  quincenal('Quincena y fin de mes');

  const _Frecuencia(this.label);

  final String label;
}
