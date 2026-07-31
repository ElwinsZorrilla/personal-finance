import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'core/env.dart';
import 'data/mock_repository.dart';
import 'design/theme.dart';
import 'design/tokens.dart';
import 'design/typography.dart';
import 'features/shell/app_shell.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  Type.bootstrap();
  await initializeDateFormatting('es_DO');

  SystemChrome.setSystemUIOverlayStyle(
    const SystemUiOverlayStyle(
      statusBarBrightness: Brightness.dark,
      statusBarIconBrightness: Brightness.light,
      systemNavigationBarColor: Tone.ink,
    ),
  );

  runApp(const MargenApp());
}

class MargenApp extends StatelessWidget {
  const MargenApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Margen',
      debugShowCheckedModeBanner: false,
      theme: buildTheme(),
      home: AppShell(
        snapshot: Env.useMocks
            ? MockRepository.strained()
            : MockRepository.healthy(),
      ),
    );
  }
}
