import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'core/env.dart';
import 'data/api_client.dart';
import 'data/dashboard_repository.dart';
import 'data/device_key.dart';
import 'data/enrollment.dart';
import 'data/api_sender.dart';
import 'data/local_store.dart';
import 'data/mock_repository.dart';
import 'data/setup_repository.dart';
import 'design/theme.dart';
import 'design/tokens.dart';
import 'design/typography.dart';
import 'features/shell/dashboard_loader.dart';
import 'features/shell/session_gate.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  Type.bootstrap();

  SystemChrome.setSystemUIOverlayStyle(
    const SystemUiOverlayStyle(
      statusBarBrightness: Brightness.dark,
      statusBarIconBrightness: Brightness.light,
      systemNavigationBarColor: Tone.ink,
    ),
  );

  final (repository, review, enrollment, setup) = buildRepositories();
  runApp(
    MargenApp(
      repository: repository,
      review: review,
      enrollment: enrollment,
      setup: setup,
    ),
  );
}

/// Elige de dónde salen los datos.
///
/// `Env.useMocks` es una constante de compilación, así que esta condición se
/// resuelve al compilar y la rama muerta se elimina del binario. Es lo que hace
/// que `MockRepository` —y los seis movimientos de ejemplo que lleva dentro— no
/// viaje en release.
(DashboardRepository, ReviewRepository, Enrollment?, SetupRepository?)
    buildRepositories() {
  if (Env.useMocks) {
    // Sin alta: los datos de ejemplo no necesitan servidor, y pedir un código
    // para verlos convertiría el modo de desarrollo en algo más lento que el
    // real.
    return (
      const MockDashboardRepository(MockRepository.strained),
      const MockReviewRepository(),
      null,
      null,
    );
  }

  // El transporte lo elige la plataforma, igual que el almacén.
  final api = ApiClient(baseUrl: Env.apiBaseUrl, send: defaultSender());

  // El almacén lo elige la plataforma. Ver `local_store.dart`: construir aquí
  // un `FileStore` hacía que la app compilara para web y se cayera al arrancar,
  // porque `dart:io` no existe en el navegador.
  final store = defaultStore();

  return (
    RemoteDashboardRepository(api: api, store: store),
    RemoteReviewRepository(api),
    Enrollment(api: api, key: defaultDeviceKey(), store: store),
    SetupRepository(api),
  );
}

/// Con qué nombre se registra este dispositivo.
///
/// Solo sirve para reconocerlo en la lista de tokens al revocarlo, así que basta
/// con que distinga uno de otro. Sale de la plataforma de Flutter y no de la
/// cadena del navegador: esa cadena miente a propósito desde hace años y
/// analizarla sería mantener una tabla de mentiras.
String _deviceName() => switch (defaultTargetPlatform) {
      TargetPlatform.iOS => 'iPhone',
      TargetPlatform.android => 'Android',
      TargetPlatform.macOS => 'Mac',
      TargetPlatform.windows => 'Windows',
      _ => 'Navegador',
    };

class MargenApp extends StatelessWidget {
  const MargenApp({
    super.key,
    required this.repository,
    required this.review,
    this.enrollment,
    this.setup,
  });

  final DashboardRepository repository;
  final ReviewRepository review;

  /// Nulo con datos de ejemplo: ahí no hay servidor al que darse de alta.
  final Enrollment? enrollment;

  /// Nulo por el mismo motivo: sin servidor no hay nada que configurar.
  final SetupRepository? setup;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Margen',
      debugShowCheckedModeBanner: false,
      theme: buildTheme(),
      home: enrollment == null
          ? DashboardLoader(
              repository: repository,
              review: review,
              setup: setup,
            )
          : SessionGate(
              enrollment: enrollment!,
              deviceName: _deviceName(),
              repository: repository,
              review: review,
              setup: setup,
            ),
    );
  }
}
