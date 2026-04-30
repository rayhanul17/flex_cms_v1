# ── Build stage ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FlexCms.slnx ./
COPY src/FlexCms.Core/FlexCms.Core.csproj           src/FlexCms.Core/
COPY src/FlexCms.Framework/FlexCms.Framework.csproj  src/FlexCms.Framework/
COPY src/FlexCms.Host/FlexCms.Host.csproj            src/FlexCms.Host/

RUN dotnet restore FlexCms.slnx

COPY src/ src/

RUN dotnet publish src/FlexCms.Host/FlexCms.Host.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish

# ── Runtime stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system --gid 1001 flexcms \
 && adduser  --system --uid 1001 --ingroup flexcms flexcms

COPY --from=build --chown=flexcms:flexcms /app/publish .

# App_Data is mounted as a volume at runtime; create the directory so the
# container starts cleanly even without a mount (first-run wizard will write here)
RUN mkdir -p App_Data && chown flexcms:flexcms App_Data

USER flexcms

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "FlexCms.Host.dll"]
