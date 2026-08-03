import 'api_client.dart';

/// El transporte de las plataformas con `dart:io`.
///
/// Este archivo **no se compila en web**: lo excluye la importación condicional
/// de `api_sender.dart`.
Future<ApiResponse> Function(ApiRequest) createSender() => IoHttpSender().call;
