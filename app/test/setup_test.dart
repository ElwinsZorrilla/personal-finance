import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:margen/core/money.dart';
import 'package:margen/data/api_client.dart';
import 'package:margen/data/setup_repository.dart';
import 'package:margen/design/components/field_line.dart';
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
    // Una pantalla alta. El lienzo de prueba por defecto es 800x600 y la
    // pantalla vive en un `ListView`, que **solo construye lo que se ve**: con
    // el lienzo corto, el botón y el aviso de error quedaban sin construir y
    // las pruebas fallaban por no encontrarlos, no por lo que comprueban.
    tester.view.physicalSize = const Size(420, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(MaterialApp(theme: buildTheme(), home: pantalla));
    await tester.pumpAndSettle();
  }

  /// Escribe en el campo cuya etiqueta es [etiqueta].
  ///
  /// Se busca por la etiqueta y no por el tipo del widget: los campos ya no son
  /// `TextField` de Material —eso era lo que hacía que la app se leyera como un
  /// formulario web— y una prueba atada al tipo se rompe con cada cambio del
  /// sistema visual. La etiqueta es lo que ve quien la usa.
  Future<void> escribir(
    WidgetTester tester,
    String etiqueta,
    String texto,
  ) async {
    final campo = find.ancestor(
      of: find.text(etiqueta.toUpperCase()),
      matching: find.byType(FieldLine),
    );

    expect(campo, findsOneWidget, reason: 'no existe el campo: $etiqueta');

    await tester.enterText(
      find.descendant(of: campo, matching: find.byType(EditableText)),
      texto,
    );
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

      await escribir(tester, 'Nombre', 'Popular corriente');
      await escribir(tester, 'Últimos cuatro dígitos', '4821');
      await escribir(tester, 'Saldo actual', '12,500.50');
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

      await escribir(tester, 'Nombre', 'Popular');
      await escribir(tester, 'Últimos cuatro dígitos', '4821');
      await escribir(tester, 'Saldo actual', 'como mil pesos');
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

      await escribir(tester, 'Nombre', 'Popular');
      await escribir(tester, 'Saldo actual', '1,000.00');
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

      await escribir(tester, 'Primer cobro', '15');
      await escribir(tester, 'Lo que cobras cada quincena', '0.00');
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

      await escribir(tester, 'Primer cobro', '15');
      await escribir(tester, 'Lo que cobras cada quincena', '42,500.00');
      await tester.tap(find.text('Abrir el período'));
      await tester.pumpAndSettle();

      expect(
        m.servidor.cuerpoDe('/setup/periods')['expectedIncomeCents'],
        4250000,
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
    testWidgets('con quincena y fin de mes viajan los dos días', (
      tester,
    ) async {
      // Es el caso real de quien cobra dos veces al mes. Con un solo día, el
      // período duraba un mes entero y el reparto diario dividía el dinero de
      // una quincena entre treinta días: la mitad de lo que se puede gastar,
      // todos los días, sin que nada fallara.
      final m = montar([estado(listo: true, categorias: 12, cuentas: 1)]);

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12, cuentas: 1)),
          onReady: () {},
        ),
      );

      await escribir(tester, 'Primer cobro', '15');
      await escribir(tester, 'Segundo cobro', '31');
      await escribir(tester, 'Lo que cobras cada quincena', '42,500.00');
      await tester.tap(find.text('Abrir el período'));
      await tester.pumpAndSettle();

      expect(m.servidor.cuerpoDe('/setup/periods')['payDays'], [15, 31]);
    });

    testWidgets('dos cobros el mismo día no se mandan', (tester) async {
      // Dos cobros el mismo día son un cobro. Dejarlo pasar daría un ciclo de
      // cero días y reventaría todo lo que reparte entre días.
      final m = montar([estado(categorias: 12, cuentas: 1)]);

      await pintar(
        tester,
        SetupScreen(
          repository: m.repo,
          status: SetupStatus.fromJson(estado(categorias: 12, cuentas: 1)),
          onReady: () {},
        ),
      );

      await escribir(tester, 'Primer cobro', '15');
      await escribir(tester, 'Segundo cobro', '15');
      await escribir(tester, 'Lo que cobras cada quincena', '42,500.00');
      await tester.tap(find.text('Abrir el período'));
      await tester.pumpAndSettle();

      expect(
        m.servidor.peticiones.any((p) => p.path.endsWith('/setup/periods')),
        isFalse,
      );
      expect(find.textContaining('no pueden ser el mismo'), findsOneWidget);
    });
  });
}
