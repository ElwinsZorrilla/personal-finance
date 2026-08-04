import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/money.dart';
import '../../data/api_client.dart';
import '../../data/setup_repository.dart';
import '../../design/tokens.dart';

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

      if (actualizado.isReady) widget.onReady();
    } on ApiFailure catch (failure) {
      _fallo(failure.message);
    } catch (error) {
      _fallo('$error');
    }
  }

  void _fallo(String mensaje) {
    if (!mounted) return;

    setState(() {
      _working = false;
      _error = mensaje;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Tone.ink,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 28),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 460),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    'Falta configurar',
                    style: Theme.of(context).textTheme.headlineMedium,
                  ),
                  const SizedBox(height: 10),
                  Text(
                    'Tres cosas, una vez. Después la app calcula sola.',
                    style: Theme.of(context)
                        .textTheme
                        .bodyMedium
                        ?.copyWith(color: Tone.muted),
                  ),
                  const SizedBox(height: 28),
                  _Paso(
                    numero: 1,
                    titulo: 'Categorías',
                    hecho: _status.categories > 0,
                    resumen: '${_status.categories} creadas',
                    child: _Categorias(
                      working: _working,
                      onCrear: () => _paso(widget.repository.seedCategories),
                    ),
                  ),
                  _Paso(
                    numero: 2,
                    titulo: 'Una cuenta',
                    hecho: _status.accounts > 0,
                    resumen: '${_status.accounts} dada de alta',
                    child: _Cuenta(
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
                  ),
                  _Paso(
                    numero: 3,
                    titulo: 'El período',
                    hecho: _status.hasOpenPeriod,
                    resumen: 'abierto',
                    child: _Periodo(
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
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 20),
                    Text(
                      _error!,
                      style: Theme.of(context)
                          .textTheme
                          .bodySmall
                          ?.copyWith(color: Signal.risk),
                    ),
                  ],
                  if (_status.emailsWaiting > 0) ...[
                    const SizedBox(height: 24),
                    Text(
                      // Se dice porque cambia lo que hay que hacer al terminar:
                      // esos correos no entran solos, y sin avisar se esperarían
                      // movimientos que no van a aparecer.
                      'Hay ${_status.emailsWaiting} correos guardados esperando '
                      'una cuenta. No se pierden, pero hay que volver a '
                      'procesarlos cuando termines.',
                      style: Theme.of(context)
                          .textTheme
                          .bodySmall
                          ?.copyWith(color: Tone.faint),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
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

/// Un paso: cerrado con una marca cuando está hecho, abierto cuando toca.
class _Paso extends StatelessWidget {
  const _Paso({
    required this.numero,
    required this.titulo,
    required this.hecho,
    required this.resumen,
    required this.child,
  });

  final int numero;
  final String titulo;
  final bool hecho;
  final String resumen;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Semantics(
            header: true,
            label: hecho
                ? 'Paso $numero, $titulo: hecho, $resumen'
                : 'Paso $numero, $titulo: pendiente',
            child: Row(
              children: [
                // La marca no es solo color: quien no distingue el verde ve
                // igualmente el símbolo y el resumen.
                Text(
                  hecho ? '✓' : '$numero',
                  style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                        color: hecho ? Signal.credit : Tone.muted,
                      ),
                ),
                const SizedBox(width: 10),
                Text(titulo, style: Theme.of(context).textTheme.titleMedium),
                if (hecho) ...[
                  const SizedBox(width: 8),
                  Text(
                    resumen,
                    style: Theme.of(context)
                        .textTheme
                        .bodySmall
                        ?.copyWith(color: Tone.faint),
                  ),
                ],
              ],
            ),
          ),
          if (!hecho) ...[
            const SizedBox(height: 12),
            child,
          ],
        ],
      ),
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
          'Se crean con nombres fijos porque el clasificador devuelve esos '
          'mismos nombres. Se pueden renombrar después.',
          style: Theme.of(context)
              .textTheme
              .bodySmall
              ?.copyWith(color: Tone.muted),
        ),
        const SizedBox(height: 12),
        FilledButton(
          onPressed: working ? null : onCrear,
          child: Text(working ? 'Creando…' : 'Crear las categorías'),
        ),
      ],
    );
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
        () => _error = 'El saldo no se entiende. Escribe algo como '
            '12,500.00',
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
        TextField(
          controller: _nombre,
          enabled: !widget.working,
          decoration: const InputDecoration(
            labelText: 'Nombre',
            hintText: 'Popular corriente',
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _ultimos,
          enabled: !widget.working,
          keyboardType: TextInputType.number,
          inputFormatters: [
            FilteringTextInputFormatter.digitsOnly,
            LengthLimitingTextInputFormatter(4),
          ],
          decoration: const InputDecoration(
            labelText: 'Últimos cuatro dígitos',
            // Es lo único que trae el correo del banco para saber de qué
            // cuenta habla.
            helperText: 'Es como el banco identifica la cuenta en sus correos',
          ),
        ),
        const SizedBox(height: 12),
        DropdownButtonFormField<AccountKind>(
          initialValue: _tipo,
          decoration: const InputDecoration(labelText: 'Tipo'),
          items: [
            for (final k in AccountKind.values)
              DropdownMenuItem(value: k, child: Text(k.label)),
          ],
          onChanged: widget.working
              ? null
              : (valor) => setState(() => _tipo = valor ?? _tipo),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _saldo,
          enabled: !widget.working,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
            labelText: 'Saldo actual',
            prefixText: 'RD\$ ',
            hintText: '12,500.00',
          ),
        ),
        if (_tipo == AccountKind.credit) ...[
          const SizedBox(height: 12),
          TextField(
            controller: _limite,
            enabled: !widget.working,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(
              labelText: 'Límite de la tarjeta',
              prefixText: 'RD\$ ',
            ),
          ),
        ],
        if (_error != null) ...[
          const SizedBox(height: 12),
          Text(
            _error!,
            style: Theme.of(context)
                .textTheme
                .bodySmall
                ?.copyWith(color: Signal.risk),
          ),
        ],
        const SizedBox(height: 16),
        FilledButton(
          onPressed: widget.working ? null : _enviar,
          child: Text(widget.working ? 'Guardando…' : 'Dar de alta la cuenta'),
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
        () => _error = 'El ingreso esperado tiene que ser mayor que '
            'cero.',
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
        Text(
          // Es la decisión de diseño de la Fase 3 y conviene que se lea aquí:
          // el mes del banco no es el mes del dinero de una persona.
          'El período no va del 1 al 30: va de un cobro al siguiente.',
          style: Theme.of(context)
              .textTheme
              .bodySmall
              ?.copyWith(color: Tone.muted),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _dia,
          enabled: !widget.working,
          keyboardType: TextInputType.number,
          inputFormatters: [
            FilteringTextInputFormatter.digitsOnly,
            LengthLimitingTextInputFormatter(2),
          ],
          decoration: const InputDecoration(
            labelText: 'Día de cobro',
            helperText: 'Si cobras el 30 y el mes tiene 28, se usa el último',
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _ingreso,
          enabled: !widget.working,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
            labelText: 'Ingreso esperado del período',
            prefixText: 'RD\$ ',
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _fondo,
          enabled: !widget.working,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
            labelText: 'Fondo de seguridad (opcional)',
            prefixText: 'RD\$ ',
            helperText: 'Lo que no se toca. Sale del gasto seguro',
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _ahorro,
          enabled: !widget.working,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
            labelText: 'Ahorro comprometido (opcional)',
            prefixText: 'RD\$ ',
          ),
        ),
        if (_error != null) ...[
          const SizedBox(height: 12),
          Text(
            _error!,
            style: Theme.of(context)
                .textTheme
                .bodySmall
                ?.copyWith(color: Signal.risk),
          ),
        ],
        const SizedBox(height: 16),
        FilledButton(
          onPressed: widget.working ? null : _enviar,
          child: Text(widget.working ? 'Abriendo…' : 'Abrir el período'),
        ),
      ],
    );
  }
}
