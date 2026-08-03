import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'core/env.dart';
import 'data/api_client.dart';
import 'data/dashboard_repository.dart';
import 'data/api_sender.dart';
import 'data/local_store.dart';
import 'data/mock_repository.dart';
import 'design/theme.dart';
import 'design/tokens.dart';
import 'design/typography.dart';
import 'features/shell/dashboard_loader.dart';

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

  final (repository, review) = buildRepositories();
  runApp(MargenApp(repository: repository, review: review));
}

/// Elige de dónde salen los datos.
///
/// `Env.useMocks` es una constante de compilación, así que esta condición se
/// resuelve al compilar y la rama muerta se elimina del binario. Es lo que hace
/// que `MockRepository` —y los seis movimientos de ejemplo que lleva dentro— no
/// viaje en release.
(DashboardRepository, ReviewRepository) buildRepositories() {
  if (Env.useMocks) {
    return (
      const MockDashboardRepository(MockRepository.strained),
      const MockReviewRepository(),
    );
  }

  // El transporte lo elige la plataforma, igual que el almacén.
  final api = ApiClient(baseUrl: Env.apiBaseUrl, send: defaultSender());

  return (
    RemoteDashboardRepository(
      api: api,
      // El almacén lo elige la plataforma. Ver `local_store.dart`: construir
      // aquí un `FileStore` hacía que la app compilara para web y se cayera al
      // arrancar, porque `dart:io` no existe en el navegador.
      store: defaultStore(),
    ),
    RemoteReviewRepository(api),
  );
}

class MargenApp extends StatelessWidget {
  const MargenApp({
    super.key,
    required this.repository,
    required this.review,
  });

  final DashboardRepository repository;
  final ReviewRepository review;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Margen',
      debugShowCheckedModeBanner: false,
      theme: buildTheme(),
      home: DashboardLoader(repository: repository, review: review),
    );
  }
}
