# ── Stage 1: build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first so the slow restore layer is cached until a package changes.
# If your repo root has Directory.Build.props, Directory.Packages.props, global.json or nuget.config,
# add a COPY line for each of them here too.
COPY LedgerLite.Domain/LedgerLite.Domain.csproj           ./LedgerLite.Domain/
COPY LedgerLite.Data/LedgerLite.Data.csproj               ./LedgerLite.Data/
COPY LedgerLite.Application/LedgerLite.Application.csproj ./LedgerLite.Application/
COPY LedgerLite.Contracts/LedgerLite.Contracts.csproj     ./LedgerLite.Contracts/
COPY LedgerLite.Web/LedgerLite.Web.csproj                 ./LedgerLite.Web/
COPY LedgerLite.Api/LedgerLite.Api.csproj                 ./LedgerLite.Api/

RUN dotnet restore LedgerLite.Api/LedgerLite.Api.csproj

COPY . .

# The Api references the Blazor project, so one publish produces the API and the web app (wwwroot).
RUN dotnet publish LedgerLite.Api/LedgerLite.Api.csproj \
    --configuration Release --no-restore --output /app/publish

# ── Stage 2: runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Folder for the Data Protection keys (mounted as a volume), owned by the non-root user
RUN mkdir -p /keys && chown $APP_UID /keys

# Run as the non-root user that ships in the image
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeysPath=/keys

EXPOSE 8080
ENTRYPOINT ["dotnet", "LedgerLite.Api.dll"]