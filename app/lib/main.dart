import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'core/env.dart';
import 'data/api_client.dart';
import 'data/dashboard_repository.dart';
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

  runApp(MargenApp(repository: buildRepository()));
}

/// Elige de dónde salen los datos.
///
/// `Env.useMocks` es una constante de compilación, así que esta condición se
/// resuelve al compilar y la rama muerta se elimina del binario. Es lo que hace
/// que `MockRepository` —y los seis movimientos de ejemplo que lleva dentro— no
/// viaje en release.
DashboardRepository buildRepository() {
  if (Env.useMocks) {
    return const MockDashboardRepository(MockRepository.strained);
  }

  final sender = IoHttpSender();

  return RemoteDashboardRepository(
    api: ApiClient(baseUrl: Env.apiBaseUrl, send: sender.call),
    store: FileStore(
      Directory('${Directory.systemTemp.path}${Platform.pathSeparator}margen'),
    ),
  );
}

class MargenApp extends StatelessWidget {
  const MargenApp({super.key, required this.repository});

  final DashboardRepository repository;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Margen',
      debugShowCheckedModeBanner: false,
      theme: buildTheme(),
      home: DashboardLoader(repository: repository),
    );
  }
}
