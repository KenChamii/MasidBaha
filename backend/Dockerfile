# API image. Build context is the backend/ folder:
#   docker build -t masidbaha-api backend

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on the project files alone, so this layer is cached until a dependency changes
COPY MasidBaha.slnx ./
COPY MasidBaha.Application/MasidBaha.Application.csproj MasidBaha.Application/
COPY MasidBaha.WebAPI/MasidBaha.WebAPI.csproj MasidBaha.WebAPI/
RUN dotnet restore MasidBaha.WebAPI/MasidBaha.WebAPI.csproj

COPY MasidBaha.Application MasidBaha.Application
COPY MasidBaha.WebAPI MasidBaha.WebAPI
RUN dotnet publish MasidBaha.WebAPI/MasidBaha.WebAPI.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Local photo storage writes here; mount a volume on it to keep photos across restarts
RUN mkdir -p /app/wwwroot/uploads && chown -R $APP_UID /app/wwwroot

COPY --from=build --chown=$APP_UID /app .

# Never run as root: $APP_UID is the unprivileged "app" user built into the image
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "MasidBaha.WebAPI.dll"]
