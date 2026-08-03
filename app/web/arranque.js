// Quita la pantalla de carga cuando Flutter pinta su primer fotograma, y dice
// qué pasó si no llega a pintarlo.
//
// Va en un archivo aparte y no en línea a propósito: la CSP de esta app no
// permite scripts en línea, y bajar esa defensa para tres líneas de arranque
// sería pagar mucho por muy poco.
//
// **La pantalla de carga se quitaba sola: no.** La primera versión la dejaba en
// el HTML dando por hecho que Flutter pintaría encima. `position: fixed` con
// `inset: 0` cubre el viewport entero y nadie la quitaba, así que la app podía
// estar funcionando perfectamente debajo y lo único que se veía era «Cargando…»
// para siempre.
//
// El diagnóstico también está aquí por un motivo concreto: en un iPhone sin un
// Mac delante **no hay forma de ver la consola del navegador**. Si el arranque
// falla, lo único que puede contarlo es la propia pantalla.
(function () {
  'use strict';

  var TOPE_MS = 20000;

  function overlay() {
    return document.getElementById('cargando');
  }

  function quitar() {
    var e = overlay();
    if (e && e.parentNode) e.parentNode.removeChild(e);
  }

  // Flutter dispara este evento al pintar el primer fotograma. Es la señal de
  // que el motor arrancó de verdad, no solo de que el script se descargó.
  window.addEventListener('flutter-first-frame', quitar);

  // Si el primer fotograma no llega, la pantalla lo cuenta en vez de quedarse
  // en «Cargando…» sin decir nada.
  window.setTimeout(function () {
    var e = overlay();
    if (!e) return;

    var detalle = window.__margenError || 'sin error registrado';

    e.style.display = 'block';
    e.style.padding = '24px';
    e.style.overflow = 'auto';
    e.style.textAlign = 'left';
    e.style.fontSize = '13px';
    e.style.lineHeight = '1.5';
    e.textContent =
      'La app no llegó a arrancar en ' + TOPE_MS / 1000 + ' segundos.\n\n' +
      'Motivo: ' + detalle + '\n\n' +
      'Navegador: ' + navigator.userAgent;
    e.style.whiteSpace = 'pre-wrap';
  }, TOPE_MS);

  // Cualquier error que reviente el arranque se guarda para que el mensaje de
  // arriba lo pueda enseñar. Sin esto, el aviso diría que no arrancó y no por
  // qué, que es la mitad de lo que hace falta.
  window.addEventListener('error', function (evento) {
    if (!window.__margenError) {
      window.__margenError =
        (evento.message || 'error') +
        (evento.filename ? ' en ' + evento.filename + ':' + evento.lineno : '');
    }
  });

  window.addEventListener('unhandledrejection', function (evento) {
    if (!window.__margenError) {
      window.__margenError = 'promesa rechazada: ' + (evento.reason || 'sin detalle');
    }
  });

  // Se envuelve `fetch` para saber **qué URL** falló.
  //
  // Sin esto, Safari dice «TypeError: Load failed» y nada más: ni la
  // dirección, ni el código, ni si llegó a haber respuesta. Con veinte
  // archivos en juego —el motor gráfico pesa dos megas repartidos en varios—
  // eso no alcanza para saber dónde mirar, y en un iPhone sin Mac no hay
  // consola donde comprobarlo.
  var fetchOriginal = window.fetch;

  window.fetch = function () {
    var url = arguments[0];
    if (url && url.url) url = url.url;

    return fetchOriginal.apply(this, arguments).then(
      function (respuesta) {
        if (!respuesta.ok && !window.__margenError) {
          window.__margenError = 'fetch ' + respuesta.status + ' en ' + url;
        }
        return respuesta;
      },
      function (error) {
        if (!window.__margenError) {
          window.__margenError = 'fetch FALLÓ en ' + url + ' — ' + error;
        }
        throw error;
      });
  };
})();
