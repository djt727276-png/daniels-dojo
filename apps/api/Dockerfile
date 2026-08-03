# syntax=docker/dockerfile:1
#
# Multi-stage build for the Daniel's Dojo API.
# Build from the REPOSITORY ROOT so central build/package configuration is in context:
#   docker build -f apps/api/Dockerfile -t daniels-dojo-api .
#
# Uses official Microsoft .NET 10 images. No secrets are baked into any layer.

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy repo-wide MSBuild + SDK pinning first (rarely changes -> better layer caching).
# .editorconfig carries the analyzer severity configuration, so the container build must
# apply exactly the same rules as a local build.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./

# Copy only project files next so 'restore' is cached until a project file changes.
COPY apps/api/src/DanielsDojo.Domain/DanielsDojo.Domain.csproj apps/api/src/DanielsDojo.Domain/
COPY apps/api/src/DanielsDojo.Application/DanielsDojo.Application.csproj apps/api/src/DanielsDojo.Application/
COPY apps/api/src/DanielsDojo.Infrastructure/DanielsDojo.Infrastructure.csproj apps/api/src/DanielsDojo.Infrastructure/
COPY apps/api/src/DanielsDojo.Api/DanielsDojo.Api.csproj apps/api/src/DanielsDojo.Api/

RUN dotnet restore apps/api/src/DanielsDojo.Api/DanielsDojo.Api.csproj

# Copy the remaining source and publish in Release without re-restoring.
COPY apps/api/src/ apps/api/src/
RUN dotnet publish apps/api/src/DanielsDojo.Api/DanielsDojo.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# ---------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# The official images provide a non-root user exposed via $APP_UID. Run as it.
USER $APP_UID

# Listen on plain HTTP 8080 (no in-container HTTPS; TLS is terminated upstream).
# Port 8080 keeps liveness/readiness probes (/health/live, /health/ready) simple.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DanielsDojo.Api.dll"]
