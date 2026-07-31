# Se construye desde la raíz del repositorio:
#   docker build -f src/Margen.Api/Dockerfile -t margen-api .
#
# Ninguna variable de configuración se hornea en la imagen. La cadena de
# conexión y el código de alta entran por entorno en tiempo de ejecución.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Primero solo lo que define las dependencias, para que restaurar quede en
# caché mientras no cambie ningún csproj.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Margen.Domain/Margen.Domain.csproj src/Margen.Domain/
COPY src/Margen.Infrastructure/Margen.Infrastructure.csproj src/Margen.Infrastructure/
COPY src/Margen.Api/Margen.Api.csproj src/Margen.Api/
RUN dotnet restore src/Margen.Api/Margen.Api.csproj

COPY src/ src/
RUN dotnet publish src/Margen.Api/Margen.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# curl lo usa el healthcheck del compose. El contenedor corre en solo lectura,
# así que se instala aquí y no en arranque.
RUN apk add --no-cache curl icu-libs tzdata

# La globalización se queda activa: el formato de fechas y montos depende de
# es-DO, y con globalización invariante `es-DO` cae en silencio a la cultura
# invariante.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    TZ=America/Santo_Domingo

COPY --from=build /app ./

# El mismo UID que espera el servicio `init` del compose, que ajusta el dueño
# del volumen de claves antes de que este contenedor arranque.
USER 1001:1001

EXPOSE 8080
ENTRYPOINT ["dotnet", "Margen.Api.dll"]
