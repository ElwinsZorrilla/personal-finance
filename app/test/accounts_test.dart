import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:margen/data/api_client.dart';
import 'package:margen/data/setup_repository.dart';
import 'package:margen/design/components/field_line.dart';
import 'package:margen/design/theme.dart';
import 'package:margen/features/settings/accounts_screen.dart';

/// Un servidor de mentira que guarda las cuentas en memoria.
class _Servidor {
  _Servidor(this.cuentas);

  List<Map<String, Object?>> cuentas;
  final List<ApiRequest> peticiones = [];

  Future<ApiResponse> call(ApiRequest request) async {
    peticiones.add(request);

    if (request.path.endsWith('/setup/accounts') && request.method == 'GET') {
      return ApiResponse(200, jsonEncode(cuentas));
    }

    // El servidor devuelve la cuenta resultante, no un objeto vacío. Un doble
    // que responde menos de lo que responde el original hace pasar pruebas
    // sobre un contrato que no existe.
    if (request.method == 'PUT' || request.method == 'POST') {
      return ApiResponse(200, jsonEncode(cuentas.first));
    }

    return const ApiResponse(200, '{}');
  }

  Map<String, Object?> cuerpoDe(String metodo) => Map<String, Object?>.from(
        peticiones.lastWhere((p) => p.method == metodo).body!
            as Map<dynamic, dynamic>,
      );
}

Map<String, Object?> cuenta({
  String id = 'a1',
  String nombre = 'Popular corriente',
  String ultimos = '4821',
  String tipo = 'Checking',
  int saldo = 1250050,
  int? limite,
}) =>
    {
      'id': id,
      'name': nombre,
      'lastFour': ultimos,
      'kind': tipo,
      'balanceCents': saldo,
      'creditLimitCents': limite,
    };

void main() {
  ({SetupRepository repo, _Servidor servidor}) montar(
    List<Map<String, Object?>> cuentas,
  ) {
    final servidor = _Servidor(cuentas);
    return (
      repo: SetupRepository(
        ApiClient(baseUrl: 'https://api.ejemplo', send: servidor.call),
      ),
      servidor: servidor,
    );
  }

  Future<void> pintar(WidgetTester tester, SetupRepository repo) async {
    // Lienzo alto: la pantalla vive en una columna larga y el widget de prueba
    // por defecto es 800x600.
    tester.view.physicalSize = const Size(420, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      MaterialApp(
        theme: buildTheme(),
        home: Scaffold(
          body: SingleChildScrollView(
            child: AccountsScreen(repository: repo, onChanged: () {}),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> escribir(
    WidgetTester tester,
    String etiqueta,
    String texto,
  ) async {
    final campo = find.ancestor(
      of: find.text(etiqueta.toUpperCase()),
      matching: find.byType(FieldLine),
    );

    await tester.enterText(
      find.descendant(of: campo, matching: find.byType(EditableText)),
      texto,
    );
  }

  group('AccountsScreen', () {
    testWidgets('las cuentas se listan con su saldo', (tester) async {
      final m = montar([
        cuenta(),
        cuenta(
          id: 'a2',
          nombre: 'Visa',
          ultimos: '9876',
          tipo: 'Credit',
          saldo: -450000,
          limite: 10000000,
        ),
      ]);

      await pintar(tester, m.repo);

      expect(find.text('Popular corriente'), findsOneWidget);
      expect(find.text('Visa'), findsOneWidget);
      expect(find.text(r'RD$ 12,500.50'), findsOneWidget);
      expect(find.text(r'-RD$ 4,500'), findsOneWidget);
    });

    testWidgets('corregir el saldo lo manda en centavos enteros', (
      tester,
    ) async {
      // El saldo es la única cifra que escribe una persona y entra directa en
      // el gasto seguro. Si la coma se colara en el número, o la cifra pasara
      // por un `double`, esto cambiaría sin que nada fallara.
      final m = montar([cuenta()]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('Popular corriente'));
      await tester.pumpAndSettle();

      await escribir(tester, 'Saldo actual', '17,777.25');
      await tester.tap(find.text('Guardar'));
      await tester.pumpAndSettle();

      expect(m.servidor.cuerpoDe('PUT')['balanceCents'], 1777725);
    });

    testWidgets('un saldo que no se entiende no se manda', (tester) async {
      // Un cero silencioso sería un saldo dado por bueno como si el usuario lo
      // hubiera dicho, y el panel lo presentaría como un hecho.
      final m = montar([cuenta()]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('Popular corriente'));
      await tester.pumpAndSettle();

      await escribir(tester, 'Saldo actual', 'como doce mil');
      await tester.tap(find.text('Guardar'));
      await tester.pumpAndSettle();

      expect(
        m.servidor.peticiones.any((p) => p.method == 'PUT'),
        isFalse,
      );
      expect(find.textContaining('no se entiende'), findsOneWidget);
    });

    testWidgets('el formulario llega relleno con lo que hay', (tester) async {
      // Reescribir una cifra de dinero para cambiar solo el nombre es una
      // ocasión de equivocarse que no hacía falta crear.
      final m = montar([cuenta()]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('Popular corriente'));
      await tester.pumpAndSettle();

      expect(find.text('12,500.50'), findsOneWidget);
    });

    testWidgets('dar de baja pregunta antes', (tester) async {
      // No borra nada, pero saca la cuenta del dinero líquido, y esa cifra es
      // la que decide cuánto se puede gastar hoy.
      final m = montar([cuenta(), cuenta(id: 'a2', nombre: 'Visa')]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('Popular corriente'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Dar de baja'));
      await tester.pumpAndSettle();

      expect(
        m.servidor.peticiones.any((p) => p.method == 'DELETE'),
        isFalse,
        reason: 'el primer toque solo pregunta',
      );
      expect(find.text('Sí, dar de baja'), findsOneWidget);

      await tester.tap(find.text('Sí, dar de baja'));
      await tester.pumpAndSettle();

      expect(m.servidor.peticiones.any((p) => p.method == 'DELETE'), isTrue);
    });

    testWidgets('con una sola cuenta no se ofrece darla de baja', (
      tester,
    ) async {
      // El servidor lo rechaza; ofrecerlo sería ofrecer algo que va a fallar.
      final m = montar([cuenta()]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('Popular corriente'));
      await tester.pumpAndSettle();

      expect(find.text('Dar de baja'), findsNothing);
    });

    testWidgets('una cuenta nueva manda los cuatro dígitos', (tester) async {
      final m = montar([cuenta()]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('Añadir una cuenta'));
      await tester.pumpAndSettle();

      await escribir(tester, 'Nombre', 'Banreservas nómina');
      await escribir(tester, 'Últimos cuatro dígitos', '3344');
      await escribir(tester, 'Saldo actual', '8,000.00');
      await tester.tap(find.text('Dar de alta'));
      await tester.pumpAndSettle();

      final cuerpo = m.servidor.cuerpoDe('POST');

      expect(cuerpo['lastFour'], '3344');
      expect(cuerpo['balanceCents'], 800000);
      expect(cuerpo['kind'], 'Checking');
    });

    testWidgets('sin los cuatro dígitos no se da de alta nada', (tester) async {
      // Son lo único que trae el correo del banco para saber de qué cuenta
      // habla: sin ellos, la cuenta no recibiría un solo movimiento.
      final m = montar([cuenta()]);

      await pintar(tester, m.repo);
      await tester.tap(find.text('Añadir una cuenta'));
      await tester.pumpAndSettle();

      await escribir(tester, 'Nombre', 'Banreservas');
      await escribir(tester, 'Saldo actual', '8,000.00');
      await tester.tap(find.text('Dar de alta'));
      await tester.pumpAndSettle();

      expect(m.servidor.peticiones.any((p) => p.method == 'POST'), isFalse);
    });
  });
}
