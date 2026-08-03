import 'dart:async';
import 'dart:convert';
import 'dart:js_interop';
import 'dart:js_interop_unsafe';
import 'dart:typed_data';

import 'package:web/web.dart' as web;

import 'device_key.dart';

/// La identidad del dispositivo en el navegador. **No se compila fuera de web.**
DeviceKey createDeviceKey() => const WebDeviceKey();

/// Clave ECDSA P-256 con WebCrypto, guardada en IndexedDB.
///
/// Es el equivalente del Enclave Seguro que preveía la app nativa. No es tan
/// bueno —el navegador no tiene hardware dedicado— pero conserva lo que
/// importa: **la privada se genera con `extractable: false`**, así que ni este
/// código puede leerla. Lo que se guarda es un objeto opaco, no el material.
///
/// En IndexedDB y no en `localStorage` porque este último solo admite texto, y
/// guardar la clave como texto exige hacerla extraíble, que es justo lo que se
/// quiere evitar.
///
/// P-256 y no otra curva porque es la que el servidor valida desde la Fase 2, y
/// aquella elección venía de que es la única que genera el Enclave Seguro de
/// iOS. Cambiarla aquí obligaría a cambiarla allí.
class WebDeviceKey implements DeviceKey {
  const WebDeviceKey();

  static const _baseDatos = 'margen';
  static const _almacen = 'claves';
  static const _clave = 'dispositivo';

  @override
  Future<bool> exists() async => await _leerPar() != null;

  @override
  Future<String> ensureKeyPair() async {
    final JSObject par = await _leerPar() ?? await _generarPar();

    final JSAny? spki = await web.window.crypto.subtle
        .exportKey('spki', _parte(par, 'publicKey'))
        .toDart;

    return base64.encode(_bytes(spki!));
  }

  @override
  Future<String> sign(String nonceBase64) async {
    final JSObject? par = await _leerPar();

    if (par == null) {
      throw StateError(
        'No hay clave en este dispositivo. Hay que darse de alta.',
      );
    }

    // Se firman **los bytes** del reto, no su texto en base64. El servidor
    // verifica contra los bytes que generó; firmar la representación produce
    // una firma que no valida, y el error que devuelve —«firma inválida»— no
    // dice nada de que el problema esté en la codificación.
    final Uint8List datos = base64.decode(nonceBase64);

    final JSAny? firma = await web.window.crypto.subtle
        .sign(
          _algoritmo({'name': 'ECDSA', 'hash': 'SHA-256'}),
          _parte(par, 'privateKey'),
          datos.toJS,
        )
        .toDart;

    return base64.encode(_bytes(firma!));
  }

  /// Convierte lo que devuelve WebCrypto —un `ArrayBuffer`— en bytes.
  static Uint8List _bytes(JSAny buffer) =>
      (buffer as JSArrayBuffer).toDart.asUint8List();

  /// Un objeto de algoritmo para WebCrypto.
  ///
  /// Se construye a mano porque `package:web` no tipa `EcKeyGenParams` ni
  /// `EcdsaParams`: su `AlgorithmIdentifier` es un objeto cualquiera y la forma
  /// la define la especificación, no el paquete.
  static JSObject _algoritmo(Map<String, String> campos) {
    final objeto = JSObject();

    campos.forEach((nombre, valor) {
      objeto.setProperty(nombre.toJS, valor.toJS);
    });

    return objeto;
  }

  /// Una de las dos claves del par.
  ///
  /// El par que devuelve `generateKey` es un objeto con `publicKey` y
  /// `privateKey`, y `package:web` tampoco lo tipa.
  static web.CryptoKey _parte(JSObject par, String cual) =>
      par.getProperty(cual.toJS) as web.CryptoKey;

  Future<JSObject> _generarPar() async {
    // `false` es `extractable`. La diferencia con `true` es la que hay entre
    // «no exporto la clave» y «no se puede exportar»: con `true`, cualquier
    // script que llegara a ejecutarse en esta página podría llevársela.
    final JSAny? generado = await web.window.crypto.subtle
        .generateKey(
          _algoritmo({'name': 'ECDSA', 'namedCurve': 'P-256'}),
          false,
          <JSString>['sign'.toJS, 'verify'.toJS].toJS,
        )
        .toDart;

    final par = generado! as JSObject;
    await _guardar(par);

    return par;
  }

  Future<JSObject?> _leerPar() async {
    final web.IDBDatabase db = await _abrir();

    try {
      final transaccion = db.transaction(_almacen.toJS, 'readonly');
      final peticion = transaccion.objectStore(_almacen).get(_clave.toJS);
      final Object? valor = await _esperar(peticion);

      return valor as JSObject?;
    } finally {
      db.close();
    }
  }

  Future<void> _guardar(JSObject par) async {
    final web.IDBDatabase db = await _abrir();

    try {
      final transaccion = db.transaction(_almacen.toJS, 'readwrite');

      await _esperar(transaccion.objectStore(_almacen).put(par, _clave.toJS));
    } finally {
      db.close();
    }
  }

  Future<web.IDBDatabase> _abrir() async {
    final apertura = web.window.indexedDB.open(_baseDatos, 1);

    apertura.onupgradeneeded = (web.Event _) {
      final db = apertura.result! as web.IDBDatabase;

      if (!db.objectStoreNames.contains(_almacen)) {
        db.createObjectStore(_almacen);
      }
    }.toJS;

    return (await _esperar(apertura))! as web.IDBDatabase;
  }

  /// Convierte una petición de IndexedDB, que va por devoluciones de llamada,
  /// en algo que se puede esperar.
  Future<Object?> _esperar(web.IDBRequest peticion) {
    final completador = Completer<Object?>();

    peticion.onsuccess = (web.Event _) {
      if (!completador.isCompleted) completador.complete(peticion.result);
    }.toJS;

    peticion.onerror = (web.Event _) {
      if (!completador.isCompleted) {
        completador.completeError(
          StateError('IndexedDB falló: ${peticion.error?.message ?? "?"}'),
        );
      }
    }.toJS;

    return completador.future;
  }
}
