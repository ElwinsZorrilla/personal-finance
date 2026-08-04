import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

import '../../core/money.dart';
import '../../data/api_client.dart';
import '../../data/setup_repository.dart';
import '../../design/components/action_button.dart';
import '../../design/components/choice_row.dart';
import '../../design/components/error_note.dart';
import '../../design/components/field_line.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';

/// La configuración inicial: categorías, una cuenta y el período abierto.
///
/// Aparece cuando el servidor responde que **no puede calcular** y dice qué le
/// falta. Antes de esta pantalla ese estado era un callejón sin salida: el
/// panel decía «todavía no hay cifras que enseñar» y no había nada que tocar,
/// porque los tres endpoints existían y ninguna pantalla los llamaba.
///
/// El orden no es decorativo: sin categorías la clasificación no tiene qué
/// asignar, sin cuenta los movimientos no tienen dónde colgarse, y sin período
/// no hay contra qué comparar. Es el mismo orden en que el servidor lista lo
/// que falta.
///
/// **Se enseña un paso a la vez.** La primera versión ponía los tres
/// formularios en la misma pantalla, y tres formularios seguidos en un teléfono
/// se leen como una página de trámite. Uno a la vez, con los anteriores
/// plegados en una línea, se lee como una app.
class SetupScreen extends StatefulWidget {
  const SetupScreen({
    super.key,
    required this.repository,
    required this.status,
    required this.onReady,
  });

  final SetupRepository repository;

  /// El estado con el que se entra, ya consultado. Se pasa hecho para que la
  /// pantalla no parpadee entre «cargando» y el primer paso.
  final SetupStatus status;

  final VoidCallback onReady;

  @override
  State<SetupScreen> createState() => _SetupScreenState();
}

class _SetupScreenState extends State<SetupScreen> {
  late SetupStatus _status = widget.status;
  bool _working = false;
  String? _error;

  /// Ejecuta un paso y vuelve a preguntar qué falta.
  ///
  /// **El estado lo decide el servidor, no esta pantalla.** Marcar el paso como
  /// hecho aquí y seguir dejaría la app creyendo que algo se guardó cuando la
  /// respuesta se perdió por el camino.
  Future<void> _paso(Future<void> Function() accion) async {
    setState(() {
      _working = true;
      _error = null;
    });

    try {
      await accion();
      final actualizado = await widget.repository.status();

      if (!mounted) return;

      setState(() {
        _status = actualizado;
        _working = false;
      });

      // Confirma que el paso entró. Va aquí y no antes porque este golpecito no
      // es acuse del toque —ese lo da el botón— sino de que el servidor guardó.
      //
      // **Sin esperarlo.** Es una vibración: nada de lo que viene después
      // depende de que termine, y esperarla ataba el avance de la pantalla a un
      // canal de plataforma que en un entorno sin vibrador no responde.
      unawaited(HapticFeedback.mediumImpact());

      if (actualizado.isReady) widget.onReady();
    } on ApiFailure catch (failure) {
      _fallo(failure.message);
    } catch (error) {
      _fallo('$error');
    }
  }

  void _fallo(String mensaje) {
    if (!mounted) return;

    HapticFeedback.heavyImpact();

    setState(() {
      _working = false;
      _error = mensaje;
    });
  }

  /// Cuál de los tres toca: el primero que el servidor no da por hecho.
  int get _actual {
    if (_status.categories == 0) return 1;
    if (_status.accounts == 0) return 2;
    return 3;
  }

  @override
  Widget build(BuildContext context) {
    final relleno = MediaQuery.paddingOf(context);

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
            Text('PASO $_actual DE 3', style: Type.eyebrow()),
            const SizedBox(height: Space.lg),
            Text(_titulo, style: Type.display(32)),
            const SizedBox(height: Space.lg),
            Text(_entrada, style: Type.body(15, color: Tone.muted)),
            const SizedBox(height: Space.xl),
            _Hechos(status: _status),
            const SizedBox(height: Space.xl),
            switch (_actual) {
              1 => _Categorias(
                  working: _working,
                  onCrear: () => _paso(widget.repository.seedCategories),
                ),
              2 => _Cuenta(
                  working: _working,
                  onCrear: (nombre, ultimos, tipo, saldo, limite) => _paso(
                    () => widget.repository.createAccount(
                      name: nombre,
                      lastFour: ultimos,
                      kind: tipo,
                      balance: saldo,
                      creditLimit: limite,
                    ),
                  ),
                ),
              _ => _Periodo(
                  working: _working,
                  onAbrir: (dia, ingreso, fondo, ahorro) => _paso(
                    () => widget.repository.openPeriod(
                      payDay: dia,
                      expectedIncome: ingreso,
                      safetyFund: fondo,
                      committedSavings: ahorro,
                    ),
                  ),
                ),
            },
            if (_error != null) ...[
              const SizedBox(height: Space.lg),
              ErrorNote(_error!),
            ],
            if (_status.emailsWaiting > 0) ...[
              const SizedBox(height: Space.xxl),
              Text(
                // Se dice porque cambia lo que hay que hacer al terminar: esos
                // correos no entran solos, y sin avisar se esperarían
                // movimientos que no van a aparecer.
                '${_status.emailsWaiting} correos del banco están guardados '
                'esperando una cuenta donde colgarse. No se pierden.',
                style: Type.body(12, color: Tone.faint),
              ),
            ],
          ],
        ),
      ),
    );
  }

  String get _titulo => switch (_actual) {
        1 => 'Primero,\nlas categorías',
        2 => 'Ahora,\ntu cuenta',
        _ => 'Y tu ciclo\nde dinero',
      };

  String get _entrada => switch (_actual) {
        1 => 'Son las etiquetas con las que se clasifica cada gasto. Se crean '
            'de una vez y se pueden renombrar después.',
        2 => 'La que recibe tu sueldo. Si tienes más, se añaden luego: empieza '
            'por la principal.',
        _ =>
          'El período no va del 1 al 30. Va de un cobro al siguiente, que es '
              'el ciclo real de tu dinero.',
      };
}

/// Los pasos ya dados, plegados en una línea cada uno.
///
/// No es decoración: es lo que dice cuánto falta. Sin esto, cada paso parece
/// una pantalla suelta y no se sabe si queda uno o quedan diez.
class _Hechos extends StatelessWidget {
  const _Hechos({required this.status});

  final SetupStatus status;

  @override
  Widget build(BuildContext context) {
    final hechos = <String>[
      if (status.categories > 0) '${status.categories} categorías',
      if (status.accounts > 0)
        '${status.accounts} ${status.accounts == 1 ? "cuenta" : "cuentas"}',
    ];

    if (hechos.isEmpty) return const SizedBox.shrink();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (final hecho in hechos)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.sm),
            child: Row(
              children: [
                // Símbolo y no solo color: quien no distingue el verde tiene
                // que poder saber qué ya está hecho.
                Text('✓', style: Type.body(13, color: Signal.credit)),
                const SizedBox(width: Space.md),
                Text(hecho, style: Type.body(13, color: Tone.muted)),
              ],
            ),
          ),
      ],
    );
  }
}

class _Categorias extends StatelessWidget {
  const _Categorias({required this.working, required this.onCrear});

  final bool working;
  final VoidCallback onCrear;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'Los nombres son fijos porque el clasificador devuelve exactamente '
          'esos. Uno distinto no clasificaría nada nunca.',
          style: Type.body(13, color: Tone.faint),
        ),
        const SizedBox(height: Space.xl),
        ActionButton(
          label: 'Crear las categorías',
          busyLabel: 'Creando…',
          busy: working,
          onPressed: onCrear,
        ),
      ],
    );
  }
}

/// Texto a centavos **sin pasar por punto flotante**.
///
/// Devuelve nulo en vez de cero cuando no se entiende. Un cero silencioso sería
/// un saldo dado de alta como si el usuario lo hubiera dicho, y esa cifra entra
/// directa en el cálculo del gasto seguro: es la única de todo el sistema que
/// no sale de un correo ni de una operación.
///
/// El formato es el del país y el de los correos del banco: coma para miles,
/// punto para decimales. `Money.parse` exige que se diga cuál es, porque
/// adivinar entre `1.234` como mil doscientos treinta y cuatro y como uno con
/// doscientos treinta y cuatro milésimas es una fuente de errores callados.
Money? _leerMonto(String texto) {
  if (texto.trim().isEmpty) return null;

  try {
    return Money.parse(texto, format: AmountFormat.commaThousands);
  } on FormatException {
    return null;
  }
}

class _Cuenta extends StatefulWidget {
  const _Cuenta({required this.working, required this.onCrear});

  final bool working;
  final void Function(
    String nombre,
    String ultimos,
    AccountKind tipo,
    Money saldo,
    Money? limite,
  ) onCrear;

  @override
  State<_Cuenta> createState() => _CuentaState();
}

class _CuentaState extends State<_Cuenta> {
  final _nombre = TextEditingController();
  final _ultimos = TextEditingController();
  final _saldo = TextEditingController();
  final _limite = TextEditingController();
  AccountKind _tipo = AccountKind.checking;
  String? _error;

  @override
  void dispose() {
    _nombre.dispose();
    _ultimos.dispose();
    _saldo.dispose();
    _limite.dispose();
    super.dispose();
  }

  void _enviar() {
    final nombre = _nombre.text.trim();
    final ultimos = _ultimos.text.trim();

    if (nombre.isEmpty) {
      setState(() => _error = 'La cuenta necesita un nombre.');
      return;
    }

    if (ultimos.length != 4 || int.tryParse(ultimos) == null) {
      setState(
        () => _error = 'Los últimos cuatro dígitos, exactamente cuatro.',
      );
      return;
    }

    final saldo = _leerMonto(_saldo.text);
    if (saldo == null) {
      setState(
        () => _error = 'El saldo no se entiende. Escríbelo como 12,500.00',
      );
      return;
    }

    // El límite es opcional y solo para tarjetas: vacío significa que no hay,
    // no que sea cero. Un cero diría que la tarjeta no tiene margen ninguno.
    Money? limite;
    if (_limite.text.trim().isNotEmpty) {
      limite = _leerMonto(_limite.text);
      if (limite == null) {
        setState(() => _error = 'El límite no se entiende.');
        return;
      }
    }

    setState(() => _error = null);
    widget.onCrear(nombre, ultimos, _tipo, saldo, limite);
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        FieldLine(
          label: 'Nombre',
          controller: _nombre,
          hint: 'Popular corriente',
          enabled: !widget.working,
        ),
        FieldLine(
          label: 'Últimos cuatro dígitos',
          controller: _ultimos,
          mono: true,
          enabled: !widget.working,
          keyboardType: TextInputType.number,
          inputFormatters: [
            FilteringTextInputFormatter.digitsOnly,
            LengthLimitingTextInputFormatter(4),
          ],
          // Es lo único que trae el correo del banco para saber de qué cuenta
          // habla, así que sin ellos la cuenta no recibiría un solo movimiento.
          help: 'Así identifica el banco la cuenta en sus correos',
        ),
        ChoiceRow<AccountKind>(
          label: 'Tipo',
          selected: _tipo,
          enabled: !widget.working,
          options: [for (final k in AccountKind.values) (k, k.label)],
          onChanged: (valor) => setState(() => _tipo = valor),
        ),
        FieldLine(
          label: 'Saldo actual',
          controller: _saldo,
          mono: true,
          prefix: r'RD$',
          hint: '12,500.00',
          enabled: !widget.working,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
        ),
        if (_tipo == AccountKind.credit)
          FieldLine(
            label: 'Límite de la tarjeta',
            controller: _limite,
            mono: true,
            prefix: r'RD$',
            enabled: !widget.working,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
          ),
        if (_error != null) ...[
          ErrorNote(_error!),
          const SizedBox(height: Space.lg),
        ],
        ActionButton(
          label: 'Dar de alta la cuenta',
          busyLabel: 'Guardando…',
          busy: widget.working,
          onPressed: _enviar,
        ),
      ],
    );
  }
}

class _Periodo extends StatefulWidget {
  const _Periodo({required this.working, required this.onAbrir});

  final bool working;
  final void Function(int dia, Money ingreso, Money? fondo, Money? ahorro)
      onAbrir;

  @override
  State<_Periodo> createState() => _PeriodoState();
}

class _PeriodoState extends State<_Periodo> {
  final _dia = TextEditingController();
  final _ingreso = TextEditingController();
  final _fondo = TextEditingController();
  final _ahorro = TextEditingController();
  String? _error;

  @override
  void dispose() {
    _dia.dispose();
    _ingreso.dispose();
    _fondo.dispose();
    _ahorro.dispose();
    super.dispose();
  }

  void _enviar() {
    final dia = int.tryParse(_dia.text.trim());

    if (dia == null || dia < 1 || dia > 31) {
      setState(() => _error = 'El día de cobro va de 1 a 31.');
      return;
    }

    final ingreso = _leerMonto(_ingreso.text);
    if (ingreso == null || ingreso.cents <= 0) {
      setState(
        () => _error = 'El ingreso esperado tiene que ser mayor que cero.',
      );
      return;
    }

    final fondo = _opcional(_fondo.text);
    if (fondo == null && _fondo.text.trim().isNotEmpty) {
      setState(() => _error = 'El fondo de seguridad no se entiende.');
      return;
    }

    final ahorro = _opcional(_ahorro.text);
    if (ahorro == null && _ahorro.text.trim().isNotEmpty) {
      setState(() => _error = 'El ahorro comprometido no se entiende.');
      return;
    }

    setState(() => _error = null);
    widget.onAbrir(dia, ingreso, fondo, ahorro);
  }

  static Money? _opcional(String texto) =>
      texto.trim().isEmpty ? null : _leerMonto(texto);

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        FieldLine(
          label: 'Día de cobro',
          controller: _dia,
          mono: true,
          enabled: !widget.working,
          keyboardType: TextInputType.number,
          inputFormatters: [
            FilteringTextInputFormatter.digitsOnly,
            LengthLimitingTextInputFormatter(2),
          ],
          help: 'Si cobras el 30 y el mes tiene 28, se usa el último día',
        ),
        FieldLine(
          label: 'Ingreso del período',
          controller: _ingreso,
          mono: true,
          prefix: r'RD$',
          hint: '85,000.00',
          enabled: !widget.working,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
        ),
        FieldLine(
          label: 'Fondo de seguridad',
          controller: _fondo,
          mono: true,
          prefix: r'RD$',
          enabled: !widget.working,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          help: 'Opcional. Lo que no se toca: sale del gasto seguro',
        ),
        FieldLine(
          label: 'Ahorro comprometido',
          controller: _ahorro,
          mono: true,
          prefix: r'RD$',
          enabled: !widget.working,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          help: 'Opcional',
        ),
        if (_error != null) ...[
          ErrorNote(_error!),
          const SizedBox(height: Space.lg),
        ],
        ActionButton(
          label: 'Abrir el período',
          busyLabel: 'Abriendo…',
          busy: widget.working,
          onPressed: _enviar,
        ),
      ],
    );
  }
}
