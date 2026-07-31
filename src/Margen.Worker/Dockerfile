# Se construye desde la raíz del repositorio:
#   docker build -f src/Margen.Worker/Dockerfile -t margen-worker .

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Margen.Domain/Margen.Domain.csproj src/Margen.Domain/
COPY src/Margen.Infrastructure/Margen.Infrastructure.csproj src/Margen.Infrastructure/
COPY src/Margen.Worker/Margen.Worker.csproj src/Margen.Worker/
RUN dotnet restore src/Margen.Worker/Margen.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Margen.Worker/Margen.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
WORKDIR /app

RUN apk add --no-cache icu-libs tzdata

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=America/Santo_Domingo

COPY --from=build /app ./

USER 1001:1001

ENTRYPOINT ["dotnet", "Margen.Worker.dll"]
