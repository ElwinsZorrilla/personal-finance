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
import '../../design/components/pressable.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';

/// Tus cuentas y tarjetas: ver, corregir, añadir y dar de baja.
///
/// La puesta en marcha solo sabía crear **una**, y después no había forma de ver
/// las demás ni de tocar ninguna. Quien tiene tres bancos —que es el caso— se
/// quedaba con una cuenta y dos que no existen para la app.
///
/// El saldo es la razón principal de esta pantalla: **es la única cifra del
/// sistema que escribe una persona**, entra directa en la fórmula del gasto
/// seguro, y se desvía de la realidad en cuanto un movimiento no llega por
/// correo. Sin poder corregirlo, esa desviación no se arregla nunca.
class AccountsScreen extends StatefulWidget {
  const AccountsScreen({
    super.key,
    required this.repository,
    required this.onChanged,
  });

  final SetupRepository repository;

  /// El saldo entra en el gasto seguro, así que la cifra del panel deja de
  /// valer en cuanto algo cambia aquí.
  final VoidCallback onChanged;

  @override
  State<AccountsScreen> createState() => _AccountsScreenState();
}

class _AccountsScreenState extends State<AccountsScreen> {
  List<Account>? _cuentas;
  String? _error;
  bool _cargando = true;
  bool _trabajando = false;

  /// Cuál está abierta para editar. Nula si ninguna.
  String? _editando;

  /// Si el formulario de cuenta nueva está abierto.
  bool _agregando = false;

  @override
  void initState() {
    super.initState();
    unawaited(_cargar());
  }

  Future<void> _cargar() async {
    try {
      final cuentas = await widget.repository.accounts();
      if (!mounted) return;

      setState(() {
        _cuentas = cuentas;
        _cargando = false;
        _error = null;
      });
    } on ApiFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _cargando = false;
        _error = failure.message;
      });
    }
  }

  /// Ejecuta algo y recarga la lista desde el servidor.
  ///
  /// **La lista se vuelve a pedir, no se parchea en memoria.** El servidor puede
  /// haber hecho más de lo que se le pidió —dar de alta una cuenta con unos
  /// dígitos que ya existen devuelve la que hay— y una lista parcheada a mano
  /// enseñaría algo que no está en la base.
  Future<void> _accion(Future<void> Function() accion) async {
    setState(() {
      _trabajando = true;
      _error = null;
    });

    try {
      await accion();
      await _cargar();

      if (!mounted) return;

      setState(() {
        _trabajando = false;
        _editando = null;
        _agregando = false;
      });

      unawaited(HapticFeedback.mediumImpact());
      widget.onChanged();
    } on ApiFailure catch (failure) {
      _fallo(failure.message);
    } catch (error) {
      // **Se atrapa todo.** Una respuesta que no encaja con lo que se espera
      // —un campo que falta, un tipo distinto— lanza un `TypeError` al
      // interpretarla, no un `ApiFailure`. Capturar solo lo segundo dejaba la
      // pantalla reventada en vez de con un mensaje.
      _fallo('$error');
    }
  }

  void _fallo(String mensaje) {
    if (!mounted) return;

    unawaited(HapticFeedback.heavyImpact());

    setState(() {
      _trabajando = false;
      _error = mensaje;
    });
  }

  @override
  Widget build(BuildContext context) {
    final cuentas = _cuentas;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('TUS CUENTAS', style: Type.eyebrow()),
        const SizedBox(height: Space.md),
        if (_cargando)
          Text('Leyendo…', style: Type.body(14, color: Tone.faint))
        else if (cuentas == null || cuentas.isEmpty)
          Text(
            'No hay ninguna cuenta activa.',
            style: Type.body(14, color: Tone.faint),
          )
        else
          for (final cuenta in cuentas)
            _Fila(
              cuenta: cuenta,
              abierta: _editando == cuenta.id,
              trabajando: _trabajando,
              // Dar de baja la única cuenta lo rechaza el servidor; aquí se
              // oculta el botón para no ofrecer algo que va a fallar.
              puedeDarseDeBaja: cuentas.length > 1,
              onAbrir: () => setState(
                () => _editando = _editando == cuenta.id ? null : cuenta.id,
              ),
              onGuardar: (nombre, saldo, limite) => _accion(
                () => widget.repository.updateAccount(
                  id: cuenta.id,
                  name: nombre,
                  balance: saldo,
                  creditLimit: limite,
                ),
              ),
              onBaja: () => _accion(
                () => widget.repository.deactivateAccount(cuenta.id),
              ),
            ),
        if (_error != null) ...[
          const SizedBox(height: Space.md),
          ErrorNote(_error!),
        ],
        const SizedBox(height: Space.lg),
        if (_agregando)
          _Nueva(
            trabajando: _trabajando,
            onCrear: (nombre, ultimos, tipo, saldo, limite) => _accion(
              () => widget.repository.createAccount(
                name: nombre,
                lastFour: ultimos,
                kind: tipo,
                balance: saldo,
                creditLimit: limite,
              ),
            ),
            onCancelar: () => setState(() => _agregando = false),
          )
        else
          ActionButton(
            label: 'Añadir una cuenta',
            quiet: true,
            onPressed: () => setState(() => _agregando = true),
          ),
      ],
    );
  }
}

/// Una cuenta en la lista. Se despliega al tocarla.
class _Fila extends StatefulWidget {
  const _Fila({
    required this.cuenta,
    required this.abierta,
    required this.trabajando,
    required this.puedeDarseDeBaja,
    required this.onAbrir,
    required this.onGuardar,
    required this.onBaja,
  });

  final Account cuenta;
  final bool abierta;
  final bool trabajando;
  final bool puedeDarseDeBaja;
  final VoidCallback onAbrir;
  final void Function(String nombre, Money saldo, Money? limite) onGuardar;
  final VoidCallback onBaja;

  @override
  State<_Fila> createState() => _FilaState();
}

class _FilaState extends State<_Fila> {
  late final _nombre = TextEditingController(text: widget.cuenta.name);
  late final _saldo =
      TextEditingController(text: MoneyFormat.bare(widget.cuenta.balance));
  late final _limite = TextEditingController(
    text: widget.cuenta.creditLimit == null
        ? ''
        : MoneyFormat.bare(widget.cuenta.creditLimit!),
  );

  String? _error;
  bool _confirmandoBaja = false;

  @override
  void dispose() {
    _nombre.dispose();
    _saldo.dispose();
    _limite.dispose();
    super.dispose();
  }

  void _guardar() {
    final nombre = _nombre.text.trim();
    if (nombre.isEmpty) {
      setState(() => _error = 'La cuenta necesita un nombre.');
      return;
    }

    final saldo = leerMonto(_saldo.text);
    if (saldo == null) {
      setState(
        () => _error = 'El saldo no se entiende. Escríbelo como 12,500.00',
      );
      return;
    }

    Money? limite;
    if (_limite.text.trim().isNotEmpty) {
      limite = leerMonto(_limite.text);
      if (limite == null) {
        setState(() => _error = 'El límite no se entiende.');
        return;
      }
    }

    setState(() => _error = null);
    widget.onGuardar(nombre, saldo, limite);
  }

  @override
  Widget build(BuildContext context) {
    final c = widget.cuenta;

    return Padding(
      padding: const EdgeInsets.only(bottom: Space.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Pressable(
            onTap: widget.trabajando ? null : widget.onAbrir,
            semanticLabel: '${c.name}, terminada en ${c.lastFour}, '
                '${MoneyFormat.display(c.balance)}',
            child: Container(
              padding: const EdgeInsets.symmetric(vertical: Space.md),
              decoration: const BoxDecoration(
                border: Border(bottom: BorderSide(color: Tone.line)),
              ),
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(c.name, style: Type.body(15)),
                        const SizedBox(height: 2),
                        Text(
                          '${c.kind.label} · ····${c.lastFour}',
                          style: Type.data(11, color: Tone.muted),
                        ),
                      ],
                    ),
                  ),
                  Text(
                    MoneyFormat.display(c.balance),
                    style: Type.data(
                      15,
                      color: c.balance.isNegative ? Signal.risk : Tone.bone,
                    ),
                  ),
                ],
              ),
            ),
          ),
          if (widget.abierta) ...[
            const SizedBox(height: Space.lg),
            FieldLine(
              label: 'Nombre',
              controller: _nombre,
              enabled: !widget.trabajando,
            ),
            FieldLine(
              label: 'Saldo actual',
              controller: _saldo,
              mono: true,
              prefix: r'RD$',
              enabled: !widget.trabajando,
              keyboardType:
                  const TextInputType.numberWithOptions(decimal: true),
            ),
            if (c.kind == AccountKind.credit)
              FieldLine(
                label: 'Límite de la tarjeta',
                controller: _limite,
                mono: true,
                prefix: r'RD$',
                enabled: !widget.trabajando,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
              ),
            if (_error != null) ...[
              ErrorNote(_error!),
              const SizedBox(height: Space.md),
            ],
            ActionButton(
              label: 'Guardar',
              busyLabel: 'Guardando…',
              busy: widget.trabajando,
              onPressed: _guardar,
            ),
            const SizedBox(height: Space.sm),
            if (widget.puedeDarseDeBaja)
              if (_confirmandoBaja)
                // **Se pregunta antes.** Dar de baja no borra nada, pero saca la
                // cuenta del dinero líquido, y esa cifra es la que decide cuánto
                // se puede gastar hoy.
                Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      'Dejará de contar para el dinero disponible. Sus '
                      'movimientos se quedan: lo que pasó, pasó.',
                      style: Type.body(12, color: Tone.faint),
                    ),
                    const SizedBox(height: Space.sm),
                    Row(
                      children: [
                        Expanded(
                          child: ActionButton(
                            label: 'Sí, dar de baja',
                            quiet: true,
                            onPressed: widget.onBaja,
                          ),
                        ),
                        Expanded(
                          child: ActionButton(
                            label: 'Cancelar',
                            quiet: true,
                            onPressed: () =>
                                setState(() => _confirmandoBaja = false),
                          ),
                        ),
                      ],
                    ),
                  ],
                )
              else
                ActionButton(
                  label: 'Dar de baja',
                  quiet: true,
                  onPressed: () => setState(() => _confirmandoBaja = true),
                ),
            const SizedBox(height: Space.lg),
          ],
        ],
      ),
    );
  }
}

/// El formulario de cuenta nueva.
class _Nueva extends StatefulWidget {
  const _Nueva({
    required this.trabajando,
    required this.onCrear,
    required this.onCancelar,
  });

  final bool trabajando;
  final void Function(
    String nombre,
    String ultimos,
    AccountKind tipo,
    Money saldo,
    Money? limite,
  ) onCrear;
  final VoidCallback onCancelar;

  @override
  State<_Nueva> createState() => _NuevaState();
}

class _NuevaState extends State<_Nueva> {
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

    final saldo = leerMonto(_saldo.text);
    if (saldo == null) {
      setState(
        () => _error = 'El saldo no se entiende. Escríbelo como 12,500.00',
      );
      return;
    }

    Money? limite;
    if (_limite.text.trim().isNotEmpty) {
      limite = leerMonto(_limite.text);
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
        Text('CUENTA NUEVA', style: Type.eyebrow()),
        const SizedBox(height: Space.lg),
        FieldLine(
          label: 'Nombre',
          controller: _nombre,
          hint: 'Banreservas nómina',
          enabled: !widget.trabajando,
        ),
        FieldLine(
          label: 'Últimos cuatro dígitos',
          controller: _ultimos,
          mono: true,
          enabled: !widget.trabajando,
          keyboardType: TextInputType.number,
          inputFormatters: [
            FilteringTextInputFormatter.digitsOnly,
            LengthLimitingTextInputFormatter(4),
          ],
          help: 'Así identifica el banco la cuenta en sus correos',
        ),
        ChoiceRow<AccountKind>(
          label: 'Tipo',
          selected: _tipo,
          enabled: !widget.trabajando,
          options: [for (final k in AccountKind.values) (k, k.label)],
          onChanged: (valor) => setState(() => _tipo = valor),
        ),
        FieldLine(
          label: 'Saldo actual',
          controller: _saldo,
          mono: true,
          prefix: r'RD$',
          hint: '12,500.00',
          enabled: !widget.trabajando,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
        ),
        if (_tipo == AccountKind.credit)
          FieldLine(
            label: 'Límite de la tarjeta',
            controller: _limite,
            mono: true,
            prefix: r'RD$',
            enabled: !widget.trabajando,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
          ),
        if (_error != null) ...[
          ErrorNote(_error!),
          const SizedBox(height: Space.md),
        ],
        ActionButton(
          label: 'Dar de alta',
          busyLabel: 'Guardando…',
          busy: widget.trabajando,
          onPressed: _enviar,
        ),
        const SizedBox(height: Space.sm),
        ActionButton(
          label: 'Cancelar',
          quiet: true,
          onPressed: widget.onCancelar,
        ),
      ],
    );
  }
}

/// Texto a centavos **sin pasar por punto flotante**.
///
/// Devuelve nulo en vez de cero cuando no se entiende. Un cero silencioso sería
/// un saldo dado por bueno como si el usuario lo hubiera dicho, y esa cifra
/// entra directa en el cálculo del gasto seguro.
Money? leerMonto(String texto) {
  if (texto.trim().isEmpty) return null;

  try {
    return Money.parse(texto, format: AmountFormat.commaThousands);
  } on FormatException {
    return null;
  }
}
