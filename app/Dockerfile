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

EXPOSE 8080
