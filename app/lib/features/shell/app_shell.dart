import 'package:flutter/material.dart';

import '../../design/tokens.dart';
import '../../design/typography.dart';
import '../../domain/models.dart';
import '../../data/setup_repository.dart';
import '../../data/transactions_repository.dart';
import '../budget/budget_screen.dart';
import '../dashboard/dashboard_screen.dart';
import '../review/review_screen.dart';
import '../settings/period_settings_screen.dart';
import '../transactions/movements_screen.dart';
import '../transactions/transactions_screen.dart';

/// Cinco destinos, ni uno más. Cada pestaña responde a una pregunta distinta:
/// cuánto tengo, qué pasó, cómo voy, qué falta resolver, y cómo está
/// configurado.
///
/// «Ajustes» solo aparece con servidor detrás: con datos de prueba no hay ciclo
/// que corregir, y una pestaña que no hace nada es peor que una que falta.
class AppShell extends StatefulWidget {
  const AppShell({
    super.key,
    required this.snapshot,
    this.onResolve,
    this.setup,
    this.movements,
    this.onChanged,
  });

  final DashboardSnapshot snapshot;

  /// Marcar un aviso como resuelto y recargar. Nulo con datos de prueba.
  final void Function(AttentionItem item)? onResolve;

  /// Nulo con datos de prueba: ahí no hay servidor cuyo ciclo corregir, y sin
  /// esto la pestaña de ajustes no aparece.
  final SetupRepository? setup;

  /// Nulo con datos de prueba. Sin él, la pestaña de movimientos enseña el
  /// recorte que trae el panel, que es lo que hacía antes.
  final TransactionsRepository? movements;

  /// Recargar el panel tras un cambio de ajustes. El ciclo decide el reparto
  /// diario, así que la cifra de la pantalla anterior deja de valer.
  final VoidCallback? onChanged;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final s = widget.snapshot;
    final pending = s.attention.length;
    final setup = widget.setup;

    return Scaffold(
      body: IndexedStack(
        index: _index,
        children: [
          DashboardScreen(snapshot: s),
          if (widget.movements case final repo?)
            MovementsScreen(
              repository: repo,
              onChanged: () => widget.onChanged?.call(),
            )
          else
            TransactionsScreen(transactions: s.recent),
          BudgetScreen(snapshot: s),
          ReviewScreen(items: s.attention, onResolve: widget.onResolve),
          if (setup != null)
            PeriodSettingsScreen(
              repository: setup,
              onDone: () {
                setState(() => _index = 0);
                widget.onChanged?.call();
              },
            ),
        ],
      ),
      bottomNavigationBar: DecoratedBox(
        decoration: const BoxDecoration(
          border: Border(top: BorderSide(color: Tone.line)),
        ),
        child: NavigationBar(
          selectedIndex: _index,
          onDestinationSelected: (i) => setState(() => _index = i),
          backgroundColor: Tone.ink,
          surfaceTintColor: Colors.transparent,
          indicatorColor: Tone.surfaceRaised,
          height: 62,
          labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
          destinations: [
            const NavigationDestination(
              icon: Icon(Icons.pending_actions_outlined),
              selectedIcon: Icon(Icons.pending_actions),
              label: 'Panel',
            ),
            const NavigationDestination(
              icon: Icon(Icons.receipt_long_outlined),
              selectedIcon: Icon(Icons.receipt_long),
              label: 'Movimientos',
            ),
            const NavigationDestination(
              icon: Icon(Icons.equalizer_outlined),
              selectedIcon: Icon(Icons.equalizer),
              label: 'Presupuesto',
            ),
            NavigationDestination(
              icon: Badge(
                isLabelVisible: pending > 0,
                backgroundColor: Signal.risk,
                label: Text('$pending', style: Type.data(9)),
                child: const Icon(Icons.rule_folder_outlined),
              ),
              selectedIcon: const Icon(Icons.rule_folder),
              label: 'Revisión',
            ),
            if (setup != null)
              const NavigationDestination(
                icon: Icon(Icons.tune_outlined),
                selectedIcon: Icon(Icons.tune),
                label: 'Ajustes',
              ),
          ],
        ),
      ),
    );
  }
}
