import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

import '../../core/money.dart';
import '../../core/period.dart';
import '../../data/api_client.dart';
import '../../data/transactions_repository.dart';
import '../../design/components/action_button.dart';
import '../../design/components/error_note.dart';
import '../../design/components/pressable.dart';
import '../../design/tokens.dart';
import '../../design/typography.dart';

/// Los movimientos de verdad, contra el servidor.
///
/// La pestaña anterior enseñaba los seis que trae el panel y nada más: sin
/// filtro, sin paginación y **sin poder tocar nada**. Un gasto mal clasificado
/// se quedaba mal clasificado para siempre, y con él el presupuesto de su
/// categoría.
///
/// Lo primero que se ve es **lo que está sin clasificar**, porque es lo único
/// que pide una decisión. Un movimiento ya clasificado no necesita atención: la
/// lista completa está debajo para quien la busque.
class MovementsScreen extends StatefulWidget {
  const MovementsScreen({
    super.key,
    required this.repository,
    required this.onChanged,
  });

  final TransactionsRepository repository;

  /// Cambiar una categoría mueve el gasto de un presupuesto a otro, así que la
  /// cifra del panel deja de valer.
  final VoidCallback onChanged;

  @override
  State<MovementsScreen> createState() => _MovementsScreenState();
}

class _MovementsScreenState extends State<MovementsScreen> {
  List<Movement>? _movimientos;
  List<Category> _categorias = const [];
  String? _error;
  bool _cargando = true;
  bool _trabajando = false;

  /// Cuál está abierto para clasificar. Nulo si ninguno.
  String? _abierto;

  /// Si se ve solo lo pendiente de clasificar.
  bool _soloPendientes = false;

  @override
  void initState() {
    super.initState();
    unawaited(_cargar());
  }

  Future<void> _cargar() async {
    try {
      // Las dos salen a la vez: son independientes, y en un teléfono con mala
      // señal la diferencia con hacerlas en fila se nota.
      //
      // `Future.wait` y no `.wait` de registro: el segundo **envuelve los
      // fallos** en un `ParallelWaitError`, y con él se pierde el `ApiFailure`
      // de dentro —la pantalla enseñaría el `toString` del envoltorio en vez de
      // la frase que escribió el servidor—.
      //
      // Y no se esperan una detrás de otra: si la primera falla, nadie llega a
      // esperar la segunda, su error se queda **sin recoger** y Flutter lo
      // denuncia como excepción no capturada. `Future.wait` observa las dos.
      final resultados = await Future.wait<Object>([
        widget.repository.list(),
        widget.repository.categories(),
      ]);

      final movimientos = resultados[0] as List<Movement>;
      final categorias = resultados[1] as List<Category>;

      if (!mounted) return;

      setState(() {
        _movimientos = movimientos;
        _categorias = categorias;
        _cargando = false;
        _error = null;
      });
    } on ApiFailure catch (failure) {
      _fallo(failure.message);
    } catch (error) {
      _fallo('$error');
    }
  }

  void _fallo(String mensaje) {
    if (!mounted) return;

    setState(() {
      _cargando = false;
      _trabajando = false;
      _error = mensaje;
    });
  }

  Future<void> _clasificar(
    Movement movimiento,
    Category categoria, {
    required bool recordar,
  }) async {
    setState(() {
      _trabajando = true;
      _error = null;
    });

    try {
      await widget.repository.setCategory(
        id: movimiento.id,
        categoryId: categoria.id,
        createRule: recordar,
      );

      await _cargar();

      if (!mounted) return;

      setState(() {
        _trabajando = false;
        _abierto = null;
      });

      unawaited(HapticFeedback.mediumImpact());
      widget.onChanged();
    } on ApiFailure catch (failure) {
      unawaited(HapticFeedback.heavyImpact());
      _fallo(failure.message);
    } catch (error) {
      unawaited(HapticFeedback.heavyImpact());
      _fallo('$error');
    }
  }

  @override
  Widget build(BuildContext context) {
    final todos = _movimientos;
    final relleno = MediaQuery.paddingOf(context);

    final pendientes = todos?.where((m) => m.needsCategory).length ?? 0;
    final visibles = todos == null
        ? const <Movement>[]
        : _soloPendientes
            ? [
                for (final m in todos)
                  if (m.needsCategory) m,
              ]
            : todos;

    return ColoredBox(
      color: Tone.ink,
      child: ListView(
        padding: EdgeInsets.only(
          left: Space.gutter,
          right: Space.gutter,
          top: relleno.top + Space.xxl,
          bottom: Space.xxxl,
        ),
        children: [
          Text('MOVIMIENTOS', style: Type.eyebrow()),
          const SizedBox(height: Space.lg),
          Text(
            _soloPendientes ? 'Sin clasificar' : 'Todo lo que pasó',
            style: Type.display(30),
          ),
          const SizedBox(height: Space.lg),
          if (pendientes > 0)
            // El filtro solo existe cuando hay algo que filtrar. Un control que
            // no cambia nada es ruido.
            _Filtro(
              pendientes: pendientes,
              activo: _soloPendientes,
              onCambiar: () =>
                  setState(() => _soloPendientes = !_soloPendientes),
            )
          else if (!_cargando)
            Text(
              'Todo está clasificado.',
              style: Type.body(13, color: Tone.faint),
            ),
          const SizedBox(height: Space.xl),
          if (_error != null) ...[
            ErrorNote(_error!),
            const SizedBox(height: Space.lg),
            ActionButton(
              label: 'Reintentar',
              quiet: true,
              onPressed: _cargar,
            ),
          ],
          if (_cargando)
            Text('Leyendo…', style: Type.body(14, color: Tone.faint))
          else if (visibles.isEmpty && _error == null)
            Text(
              'No hay movimientos que enseñar.',
              style: Type.body(14, color: Tone.faint),
            )
          else
            for (final m in visibles)
              _Fila(
                movimiento: m,
                categorias: _categorias,
                abierto: _abierto == m.id,
                trabajando: _trabajando,
                onAbrir: () => setState(
                  () => _abierto = _abierto == m.id ? null : m.id,
                ),
                onClasificar: (categoria, recordar) =>
                    _clasificar(m, categoria, recordar: recordar),
              ),
        ],
      ),
    );
  }
}

class _Filtro extends StatelessWidget {
  const _Filtro({
    required this.pendientes,
    required this.activo,
    required this.onCambiar,
  });

  final int pendientes;
  final bool activo;
  final VoidCallback onCambiar;

  @override
  Widget build(BuildContext context) {
    return Pressable(
      onTap: onCambiar,
      semanticLabel: activo
          ? 'Ver todos los movimientos'
          : 'Ver solo los $pendientes sin clasificar',
      child: Container(
        padding: const EdgeInsets.symmetric(
          horizontal: Space.lg,
          vertical: Space.md,
        ),
        decoration: BoxDecoration(
          color: activo ? Tone.surfaceRaised : Tone.ink,
          borderRadius: Radii.brSm,
          border: Border.all(color: activo ? Tone.bone : Tone.line),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 6,
              height: 6,
              decoration: const BoxDecoration(
                color: Signal.caution,
                shape: BoxShape.circle,
              ),
            ),
            const SizedBox(width: Space.sm),
            Text(
              activo
                  ? 'Viendo solo los $pendientes sin clasificar'
                  : '$pendientes sin clasificar',
              style: Type.body(
                13,
                weight: activo ? FontWeight.w600 : FontWeight.w400,
                color: activo ? Tone.bone : Tone.muted,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Un movimiento. Se despliega para clasificarlo.
class _Fila extends StatefulWidget {
  const _Fila({
    required this.movimiento,
    required this.categorias,
    required this.abierto,
    required this.trabajando,
    required this.onAbrir,
    required this.onClasificar,
  });

  final Movement movimiento;
  final List<Category> categorias;
  final bool abierto;
  final bool trabajando;
  final VoidCallback onAbrir;
  final void Function(Category categoria, bool recordar) onClasificar;

  @override
  State<_Fila> createState() => _FilaState();
}

class _FilaState extends State<_Fila> {
  /// Si al clasificar se crea una regla para el mismo comercio.
  ///
  /// Encendido por defecto: quien corrige una clasificación casi siempre quiere
  /// que no se le vuelva a preguntar. La cascada de la Fase 8 lo contempla, y
  /// era lo que no tenía forma de dispararse desde la app.
  bool _recordar = true;

  @override
  Widget build(BuildContext context) {
    final m = widget.movimiento;

    // El signo lo decide el servidor con `isIncome`. La app no lo deduce del
    // signo del monto: un reverso y un ingreso se guardan distinto.
    final color = m.isIncome ? Signal.credit : Tone.bone;
    final signo = m.isIncome ? '+' : '';

    return Padding(
      padding: const EdgeInsets.only(bottom: Space.sm),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Pressable(
            onTap: widget.trabajando ? null : widget.onAbrir,
            semanticLabel: '${m.merchant}, '
                '${MoneyFormat.display(m.amount)}, '
                '${DateLabel.long(m.occurredAt)}, '
                '${m.categoryName ?? "sin clasificar"}',
            child: Container(
              padding: const EdgeInsets.symmetric(vertical: Space.md),
              decoration: const BoxDecoration(
                border: Border(bottom: BorderSide(color: Tone.line)),
              ),
              child: Row(
                children: [
                  if (m.needsCategory) ...[
                    // Un punto, no solo un color de texto: es lo que se ve de
                    // reojo al recorrer la lista.
                    Container(
                      width: 6,
                      height: 6,
                      decoration: const BoxDecoration(
                        color: Signal.caution,
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: Space.sm),
                  ],
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          m.merchant,
                          style: Type.body(14),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '${DateLabel.long(m.occurredAt)} · '
                          '${m.categoryName ?? "sin clasificar"} · '
                          '····${m.accountLastFour}',
                          style: Type.data(10, color: Tone.muted),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: Space.sm),
                  Text(
                    '$signo${MoneyFormat.display(m.amount)}',
                    style: Type.data(14, color: color),
                  ),
                ],
              ),
            ),
          ),
          if (widget.abierto) ...[
            const SizedBox(height: Space.md),
            Text('CLASIFICAR COMO', style: Type.eyebrow()),
            const SizedBox(height: Space.md),
            Wrap(
              spacing: Space.sm,
              runSpacing: Space.sm,
              children: [
                for (final c in widget.categorias)
                  Pressable(
                    onTap: widget.trabajando
                        ? null
                        : () => widget.onClasificar(c, _recordar),
                    semanticLabel: 'Clasificar como ${c.name}',
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: Space.md,
                        vertical: Space.sm,
                      ),
                      decoration: BoxDecoration(
                        borderRadius: Radii.brSm,
                        border: Border.all(
                          color: c.id == m.categoryId ? Tone.bone : Tone.line,
                        ),
                        color: c.id == m.categoryId
                            ? Tone.surfaceRaised
                            : Tone.ink,
                      ),
                      child: Text(
                        c.name,
                        style: Type.body(
                          13,
                          color: c.id == m.categoryId ? Tone.bone : Tone.muted,
                        ),
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: Space.md),
            Pressable(
              onTap: () => setState(() => _recordar = !_recordar),
              semanticLabel: _recordar
                  ? 'Recordar para el próximo movimiento de ${m.merchant}: sí'
                  : 'Recordar para el próximo movimiento de ${m.merchant}: no',
              child: Row(
                children: [
                  Text(
                    _recordar ? '✓' : '○',
                    style: Type.body(
                      13,
                      color: _recordar ? Signal.credit : Tone.faint,
                    ),
                  ),
                  const SizedBox(width: Space.sm),
                  Expanded(
                    child: Text(
                      'Recordarlo para las próximas compras en este comercio',
                      style: Type.body(12, color: Tone.muted),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: Space.lg),
          ],
        ],
      ),
    );
  }
}
