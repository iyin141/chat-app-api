# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY ChatApp.slnx ./
COPY ChatApp.Api/ChatApp.Api.csproj ./ChatApp.Api/
COPY ChatApp.Infrastructure/ChatApp.Infrastructure.csproj ./ChatApp.Infrastructure/
COPY ChatApp.Tests/ChatApp.Tests.csproj ./ChatApp.Tests/

RUN dotnet restore ChatApp.slnx

# Copy everything and publish
COPY . .
RUN dotnet publish ChatApp.Api/ChatApp.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:PublishSingleFile=false \
    -p:UseAppHost=false

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Use official .NET non-root app user
USER $APP_UID

COPY --from=build /app/publish .

# Health-check using the /healthz endpoint
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:8080/healthz || exit 1

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "ChatApp.Api.dll"]