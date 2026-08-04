import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:margen/data/api_client.dart';
import 'package:margen/data/transactions_repository.dart';
import 'package:margen/design/theme.dart';
import 'package:margen/features/transactions/movements_screen.dart';

class _Servidor {
  _Servidor({required this.movimientos, required this.categorias});

  final List<Map<String, Object?>> movimientos;
  final List<Map<String, Object?>> categorias;
  final List<ApiRequest> peticiones = [];

  Future<ApiResponse> call(ApiRequest request) async {
    peticiones.add(request);

    if (request.path.contains('/transactions?')) {
      return ApiResponse(200, jsonEncode({'items': movimientos}));
    }

    if (request.path.endsWith('/setup/categories')) {
      return ApiResponse(200, jsonEncode(categorias));
    }

    // El servidor devuelve el movimiento resultante. Un doble que responde
    // menos que el original hace pasar pruebas sobre un contrato que no existe.
    return ApiResponse(200, jsonEncode(movimientos.first));
  }

  Map<String, Object?> cuerpoDe(String metodo) => Map<String, Object?>.from(
        peticiones.lastWhere((p) => p.method == metodo).body!
            as Map<dynamic, dynamic>,
      );
}

Map<String, Object?> movimiento({
  String id = 't1',
  String comercio = 'SUPERMERCADO NACIONAL',
  int monto = 285075,
  bool ingreso = false,
  String? categoriaId,
  String? categoria,
}) =>
    {
      'id': id,
      'merchant': comercio,
      'amountCents': monto,
      'occurredAt': '2026-08-02T15:30:00Z',
      'isIncome': ingreso,
      'status': 'Posted',
      'accountLastFour': '4821',
      'categoryId': categoriaId,
      'categoryName': categoria,
    };

Map<String, Object?> categoria({
  String id = 'c1',
  String nombre = 'Comida',
  String prioridad = 'Flexible',
}) =>
    {
      'id': id,
      'name': nombre,
      'priority': prioridad,
      'icon': null,
      'isSystem': false,
    };

void main() {
  ({TransactionsRepository repo, _Servidor servidor}) montar({
    required List<Map<String, Object?>> movimientos,
    List<Map<String, Object?>>? categorias,
  }) {
    final servidor = _Servidor(
      movimientos: movimientos,
      categorias: categorias ??
          [categoria(), categoria(id: 'c2', nombre: 'Transporte')],
    );

    return (
      repo: TransactionsRepository(
        ApiClient(baseUrl: 'https://api.ejemplo', send: servidor.call),
      ),
      servidor: servidor,
    );
  }

  Future<void> pintar(WidgetTester tester, TransactionsRepository repo) async {
    tester.view.physicalSize = const Size(420, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      MaterialApp(
        theme: buildTheme(),
        home: MovementsScreen(repository: repo, onChanged: () {}),
      ),
    );
    await tester.pumpAndSettle();
  }

  group('MovementsScreen', () {
    testWidgets('los movimientos se listan con su comercio y monto', (
      tester,
    ) async {
      final m = montar(movimientos: [movimiento()]);

      await pintar(tester, m.repo);

      expect(find.text('SUPERMERCADO NACIONAL'), findsOneWidget);
      expect(find.text(r'RD$ 2,850.75'), findsOneWidget);
    });

    testWidgets('un ingreso lleva signo más', (tester) async {
      // El signo lo decide el servidor con `isIncome`, no el signo del monto:
      // un reverso y un ingreso se guardan distinto.
      final m = montar(
        movimientos: [movimiento(comercio: 'NOMINA', ingreso: true)],
      );

      await pintar(tester, m.repo);

      expect(find.text(r'+RD$ 2,850.75'), findsOneWidget);
    });

    testWidgets('lo que falta clasificar se cuenta y se puede filtrar', (
      tester,
    ) async {
      // Es lo único que pide una decisión. Un movimiento ya clasificado no
      // necesita atención.
      final m = montar(
        movimientos: [
          movimiento(),
          movimiento(
            id: 't2',
            comercio: 'UBER',
            categoriaId: 'c2',
            categoria: 'Transporte',
          ),
        ],
      );

      await pintar(tester, m.repo);

      expect(find.text('1 sin clasificar'), findsOneWidget);
      expect(find.text('UBER'), findsOneWidget);

      await tester.tap(find.text('1 sin clasificar'));
      await tester.pumpAndSettle();

      expect(find.text('SUPERMERCADO NACIONAL'), findsOneWidget);
      expect(find.text('UBER'), findsNothing);
    });

    testWidgets('sin nada pendiente no aparece el filtro', (tester) async {
      // Un control que no cambia nada es ruido.
      final m = montar(
        movimientos: [
          movimiento(categoriaId: 'c1', categoria: 'Comida'),
        ],
      );

      await pintar(tester, m.repo);

      expect(find.textContaining('sin clasificar'), findsNothing);
      expect(find.text('Todo está clasificado.'), findsOneWidget);
    });

    testWidgets('clasificar manda la categoría y crea la regla', (
      tester,
    ) async {
      // Es la cascada de la Fase 8: una corrección del usuario genera una regla
      // y evita volver a preguntar por el mismo comercio. Estaba construida en
      // el servidor y no tenía forma de dispararse desde la app.
      final m = montar(movimientos: [movimiento()]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('SUPERMERCADO NACIONAL'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Comida'));
      await tester.pumpAndSettle();

      final cuerpo = m.servidor.cuerpoDe('PUT');

      expect(cuerpo['categoryId'], 'c1');
      expect(cuerpo['createRule'], isTrue);
    });

    testWidgets('se puede clasificar sin crear la regla', (tester) async {
      // Un gasto excepcional en un comercio que normalmente es otra cosa: la
      // regla lo clasificaría mal para siempre.
      final m = montar(movimientos: [movimiento()]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('SUPERMERCADO NACIONAL'));
      await tester.pumpAndSettle();

      await tester.tap(find.textContaining('Recordarlo para las próximas'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Comida'));
      await tester.pumpAndSettle();

      expect(m.servidor.cuerpoDe('PUT')['createRule'], isFalse);
    });

    testWidgets('un fallo del servidor se enseña y se puede reintentar', (
      tester,
    ) async {
      final servidor = _Servidor(movimientos: [], categorias: []);
      final repo = TransactionsRepository(
        ApiClient(
          baseUrl: 'https://api.ejemplo',
          maxAttempts: 1,
          send: (_) async => const ApiResponse(500, '{"detail":"se cayó"}'),
        ),
      );

      await pintar(tester, repo);

      expect(find.text('se cayó'), findsOneWidget);
      expect(find.text('Reintentar'), findsOneWidget);
      expect(servidor.peticiones, isEmpty);
    });
  });
}
