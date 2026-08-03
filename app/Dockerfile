# La PWA: se compila con Flutter y se sirve como archivos estáticos.
#
# Dos etapas. La primera compila; la segunda solo lleva nginx y el resultado, y
# pesa unos megabytes en vez de los dos gigas del SDK de Flutter.

FROM ghcr.io/cirruslabs/flutter:3.44.5 AS build

WORKDIR /app

# El manifiesto primero y las dependencias después: mientras `pubspec` no
# cambie, Docker reutiliza esta capa y no vuelve a descargar nada.
COPY app/pubspec.yaml app/pubspec.lock ./
RUN flutter pub get

COPY app/ ./

# La URL del API entra al compilar y no en tiempo de ejecución: en una PWA no
# hay proceso de servidor donde leer una variable de entorno. Es la misma
# variable que usa `Env.apiBaseUrl`.
ARG API_BASE_URL=""
RUN flutter build web --release \
    --dart-define=USE_MOCKS=false \
    --dart-define=API_BASE_URL="${API_BASE_URL}"

FROM nginx:1.27-alpine AS runtime

COPY --from=build /app/build/web /usr/share/nginx/html
COPY infra/nginx-pwa.conf /etc/nginx/conf.d/default.conf

# nginx escribe temporales y su pid. Con el sistema de archivos de solo lectura
# —que es como corre este contenedor— hacen falta estos directorios en tmpfs;
# el compose los monta.
EXPOSE 8080
