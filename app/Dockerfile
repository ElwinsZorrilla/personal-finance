# La PWA: nginx sirviendo el bundle de Flutter Web.
#
# **El bundle se compila fuera y esta imagen solo lo empaqueta.** No es lo
# habitual y tiene un motivo: compilar Flutter dentro exige bajar un SDK de
# varios gigas en cada máquina que construya, y el SDK que se usa aquí es el
# mismo con el que corre la compuerta 1 —3.44.5—, así que lo que se empaqueta es
# exactamente lo que pasó las pruebas.
#
# Se construye desde la raíz del repositorio, después de:
#   cd app && flutter build web --release \
#     --dart-define=USE_MOCKS=false \
#     --dart-define=API_BASE_URL=https://api.tudominio
#
# El `API_BASE_URL` va ahí y no aquí: en una PWA no hay proceso donde leer una
# variable en tiempo de ejecución, así que la dirección del API se compila
# dentro del JavaScript.

FROM nginx:1.27-alpine AS runtime

# Si el bundle no está, esto falla al copiar en vez de producir una imagen que
# sirve un directorio vacío y da 404 sin decir por qué.
COPY app/build/web /usr/share/nginx/html

COPY infra/nginx-pwa.conf /etc/nginx/conf.d/default.conf
COPY infra/nginx-seguridad.inc /etc/nginx/margen-seguridad.inc

# El origen del API entra en la CSP al construir, por el mismo motivo que
# `API_BASE_URL`: el navegador bloquea las peticiones a un origen que la CSP no
# nombra, y ese error sale en la consola del navegador y en ningún registro del
# servidor.
ARG API_BASE_URL=""
# Sin el argumento, `sed` sustituye por vacío y la CSP queda sintácticamente
# válida y funcionalmente rota: la app cargaría y ninguna petición saldría. Se
# comprueba que hay valor, no solo que el marcador desapareció.
RUN test -n "${API_BASE_URL}" \
    || { echo "FALLO: falta --build-arg API_BASE_URL."; exit 1; } \
    && sed -i "s|__API_ORIGIN__|${API_BASE_URL}|g" /etc/nginx/margen-seguridad.inc \
    && if grep -q "__API_ORIGIN__" /etc/nginx/margen-seguridad.inc; then \
         echo "FALLO: la CSP quedó sin el origen del API."; \
         exit 1; \
       fi \
    && nginx -t

# ---- La versión entra en las URL ----
#
# Flutter Web **no pone hash en los nombres**: su salida se llama siempre
# `main.dart.js` y `flutter_bootstrap.js`. Sin nada más, la URL de un arreglo es
# idéntica a la del error que arregla, y cualquier caché por el camino puede
# seguir sirviendo el error indefinidamente sin que nada lo delate.
#
# Pasó dos veces. La primera, un `immutable` de un año en el propio nginx: seis
# despliegues seguidos sin que cambiara nada en la pantalla. La segunda,
# Cloudflare reescribiendo `no-cache` a `max-age=14400` — porque **las cabeceras
# del origen son una petición, no una orden**: hay una CDN en medio con su
# propia política y gana ella.
#
# Una URL distinta no se puede negociar. `index.html` es el único archivo cuya
# dirección no cambia, y es el que lleva dentro las de todos los demás.
#
# Cada sustitución se comprueba después. Un `sed` que no encuentra su patrón
# sale con 0: sin los `grep`, un cambio de plantilla de Flutter dejaría la
# imagen construida y el versionado apagado —justo el fallo que esto viene a
# evitar, y otra vez invisible—.
ARG BUILD_STAMP=""
RUN test -n "${BUILD_STAMP}" \
    || { echo "FALLO: falta --build-arg BUILD_STAMP."; exit 1; } \
    && cd /usr/share/nginx/html \
    && sed -i \
        -e "s|src=\"arranque.js\"|src=\"arranque.js?v=${BUILD_STAMP}\"|" \
        -e "s|src=\"flutter_bootstrap.js\"|src=\"flutter_bootstrap.js?v=${BUILD_STAMP}\"|" \
        index.html \
    && sed -i \
        "s|\"mainJsPath\":\"main.dart.js\"|\"mainJsPath\":\"main.dart.js?v=${BUILD_STAMP}\"|" \
        flutter_bootstrap.js \
    && sed -i "s|__BUILD_STAMP__|${BUILD_STAMP}|g" arranque.js \
    && grep -q "flutter_bootstrap.js?v=${BUILD_STAMP}" index.html \
    && grep -q "arranque.js?v=${BUILD_STAMP}" index.html \
    && grep -q "main.dart.js?v=${BUILD_STAMP}" flutter_bootstrap.js \
    && ! grep -q "__BUILD_STAMP__" arranque.js \
    || { echo "FALLO: el versionado de URL no se aplicó entero."; exit 1; }

EXPOSE 8080
