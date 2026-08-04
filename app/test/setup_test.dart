import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:margen/core/money.dart';
import 'package:margen/data/api_client.dart';
import 'package:margen/data/setup_repository.dart';
import 'package:margen/design/theme.dart';
import 'package:margen/features/setup/setup_screen.dart';

/// Un servidor de mentira que apunta lo que le mandan.
class _Servidor {
  _Servidor({required this.estados});

  /// Un estado por cada consulta a `/setup/status`, en orden.
  final List<Map<String, Object?>> estados;

  final List<ApiRequest> peticiones = [];
  int _consultas = 0;

  Future<ApiResponse> call(ApiRequest request) async {
    peticiones.add(request);

    if (request.path.endsWith('/setup/status')) {
      final i = _consultas < estados.length ? _consultas : estados.length - 1;
      _consultas++;
      return ApiResponse(200, jsonEncode(estados[i]));
    }

    return const ApiResponse(200, '{}');
  }

  /// El cuerpo de la última petición a una ruta.
  Map<String, Object?> cuerpoDe(String ruta) => Map<String, Object?>.from(
        peticiones.lastWhere((p) => p.path.endsWith(ruta)).body!
            as Map<dynamic, dynamic>,
      );
}

Map<String, Object?> estado({
  bool listo = false,
  int cuentas = 0,
  int categorias = 0,
  bool periodo = false,
  int correos = 0,
}) =>
    {
      'isReady': listo,
      'accounts': cuentas,
      'categories': categorias,
      'hasOpenPeriod': periodo,
      'emailsWaiting': correos,
      'missing': <String>[],
    };

void main() {
  ({SetupRepository repo, _Servidor servidor}) montar(
    List<Map<String, Object?>> estados,
  ) {
    final servidor = _Servidor(estados: estados);
    return (
      repo: SetupRepository(
        ApiClient(baseUrl: 'https://api.ejemplo', send: servidor.call),
      ),
      servidor: servidor,
    );
  }

  Future<void> pintar(WidgetTester tester, Widget pantalla) async {
    await tester.pumpWidget(MaterialApp(theme: buildTheme(), home: pantalla));
    await tester.pumpAndSettle();
  }

  group('SetupRepository', () {
    test('el saldo viaja en centavos enteros', () async {
      // Es la única cifra del sistema que escribe una persona, y entra directa
      // en la fórmula del gasto seguro.
      final m = montar([estado()]);

      await m.repo.createAccount(
        name: 'Popular',
        lastFour: '1234',
        kind: AccountKind.checking,
        balance: const Money(1250075),
      );

      final cuerpo = m.servidor.cuerpoDe('/setup/accounts');

      expect(cuerpo['balanceCents'], 1250075);
      expect(cuerpo['kind'], 'Checking');
      expect(cuerpo['creditLimitCents'], isNull);
    });

    test('el tipo viaja con el nombre que entiende el servidor', () async {
      // La etiqueta que se lee en pantalla está en español y el `enum` del
      // dominio en inglés. Mandar la etiqueta daría un 400 con un mensaje
      // sobre un tipo desconocido.
      final m = montar([estado()]);

      await m.repo.createAccount(
        name: 'Visa',
        lastFour: '9876',
        kind: AccountKind.credit,
        balance: const Money(-450000),
        creditLimit: const Money(10000000),
      );

      final cuerpo = m.servidor.cuerpoDe('/setup/accounts');

      expect(cuerpo['kind'], 'Credit');
      expect(cuerpo['creditLimitCents'], 10000000);
    });
  });

  group('SetupScreen', () {
    testWidgets('los montos con coma de miles llegan en centavos', (
      tester,
    ) async {
      // `12,500.50` son 1250050 centavos. Si la coma se colara en el número o
      // la cifra pasara por un `double`, esto cambiaría sin que nada fallara.
      final m = montar([estado(categorias: 12, cuentas: 1)]);

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12)),
          onReady: () {},
        ),
      );

      await tester.enterText(
        find.widgetWithText(TextField, 'Nombre'),
        'Popular corriente',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Últimos cuatro dígitos'),
        '4821',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Saldo actual'),
        '12,500.50',
      );
      await tester.tap(find.text('Dar de alta la cuenta'));
      await tester.pumpAndSettle();

      expect(m.servidor.cuerpoDe('/setup/accounts')['balanceCents'], 1250050);
    });

    testWidgets('un saldo que no se entiende no se manda como cero', (
      tester,
    ) async {
      // Un cero silencioso sería una cuenta vacía dada de alta como si el
      // usuario lo hubiera dicho, y con ella el panel diría que no hay nada
      // que gastar.
      final m = montar([estado(categorias: 12)]);

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12)),
          onReady: () {},
        ),
      );

      await tester.enterText(
        find.widgetWithText(TextField, 'Nombre'),
        'Popular',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Últimos cuatro dígitos'),
        '4821',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Saldo actual'),
        'como mil pesos',
      );
      await tester.tap(find.text('Dar de alta la cuenta'));
      await tester.pumpAndSettle();

      expect(
        m.servidor.peticiones.any((p) => p.path.endsWith('/setup/accounts')),
        isFalse,
        reason: 'no se manda nada si el monto no se entiende',
      );
      expect(find.textContaining('no se entiende'), findsOneWidget);
    });

    testWidgets('sin los cuatro dígitos no se manda nada', (tester) async {
      // Son lo único que trae el correo del banco para saber de qué cuenta
      // habla: una cuenta sin ellos no recibiría ningún movimiento.
      final m = montar([estado(categorias: 12)]);

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12)),
          onReady: () {},
        ),
      );

      await tester.enterText(
        find.widgetWithText(TextField, 'Nombre'),
        'Popular',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Saldo actual'),
        '1,000.00',
      );
      await tester.tap(find.text('Dar de alta la cuenta'));
      await tester.pumpAndSettle();

      expect(
        m.servidor.peticiones.any((p) => p.path.endsWith('/setup/accounts')),
        isFalse,
      );
    });

    testWidgets('un ingreso de cero no abre el período', (tester) async {
      // El servidor lo rechaza, pero gastar la ida y vuelta para enterarse
      // deja al usuario esperando por algo que ya se sabía aquí.
      final m = montar([estado(categorias: 12, cuentas: 1)]);

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12, cuentas: 1)),
          onReady: () {},
        ),
      );

      await tester.enterText(
        find.widgetWithText(TextField, 'Día de cobro'),
        '30',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Ingreso esperado del período'),
        '0.00',
      );
      await tester.tap(find.text('Abrir el período'));
      await tester.pumpAndSettle();

      expect(
        m.servidor.peticiones.any((p) => p.path.endsWith('/setup/periods')),
        isFalse,
      );
      expect(find.textContaining('mayor que'), findsOneWidget);
    });

    testWidgets('el paso hecho se marca con símbolo y no solo con color', (
      tester,
    ) async {
      // Quien no distingue el verde tiene que poder saber qué falta.
      final m = montar([estado(categorias: 12)]);

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12)),
          onReady: () {},
        ),
      );

      expect(find.text('✓'), findsOneWidget);
      expect(find.text('Crear las categorías'), findsNothing);
    });

    testWidgets('al quedar listo se avisa una sola vez', (tester) async {
      // Un solo estado: la pantalla recibe el de partida ya hecho y solo
      // consulta **después** de cada paso.
      final m = montar([
        estado(listo: true, categorias: 12, cuentas: 1, periodo: true),
      ]);

      var listo = 0;

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12, cuentas: 1)),
          onReady: () => listo++,
        ),
      );

      await tester.enterText(
        find.widgetWithText(TextField, 'Día de cobro'),
        '30',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Ingreso esperado del período'),
        '85,000.00',
      );
      await tester.tap(find.text('Abrir el período'));
      await tester.pumpAndSettle();

      expect(
        m.servidor.cuerpoDe('/setup/periods')['expectedIncomeCents'],
        8500000,
      );
      expect(listo, 1);
    });

    testWidgets('los correos en espera se dicen', (tester) async {
      // No entran solos: sin avisar, se esperarían movimientos que no van a
      // aparecer.
      final m = montar([estado(categorias: 12, correos: 7)]);

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12, correos: 7)),
          onReady: () {},
        ),
      );

      expect(find.textContaining('7 correos'), findsOneWidget);
    });
  });
}
