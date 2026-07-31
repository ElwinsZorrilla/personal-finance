import 'package:flutter/material.dart';

import '../../design/tokens.dart';
import '../../design/typography.dart';
import '../../domain/models.dart';
import '../budget/budget_screen.dart';
import '../dashboard/dashboard_screen.dart';
import '../review/review_screen.dart';
import '../transactions/transactions_screen.dart';

/// Cuatro destinos, ni uno más. Cada pestaña responde a una pregunta distinta:
/// cuánto tengo, qué pasó, cómo voy, qué falta resolver.
class AppShell extends StatefulWidget {
  const AppShell({super.key, required this.snapshot});

  final DashboardSnapshot snapshot;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final s = widget.snapshot;
    final pending = s.attention.length;

    return Scaffold(
      body: IndexedStack(
        index: _index,
        children: [
          DashboardScreen(snapshot: s),
          TransactionsScreen(transactions: s.recent),
          BudgetScreen(snapshot: s),
          ReviewScreen(items: s.attention),
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
                label: Text('$pending', style: Type.data(9, color: Tone.bone)),
                child: const Icon(Icons.rule_folder_outlined),
              ),
              selectedIcon: const Icon(Icons.rule_folder),
              label: 'Revisión',
            ),
          ],
        ),
      ),
    );
  }
}
