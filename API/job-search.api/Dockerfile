FROM mcr.microsoft.com/dotnet/sdk:10.0.101@sha256:5504edd1267dd4deab3443f960cfab219249c8bd935fbcc358f1c24aeae23fe0 AS build
ARG TARGETARCH
WORKDIR /source
COPY global.json Directory.Build.props NuGet.Config ./
COPY .config/ .config/
COPY src/ src/
RUN dotnet restore src/JobSearch.Api/JobSearch.Api.csproj --locked-mode && dotnet tool restore && dotnet publish src/JobSearch.Api/JobSearch.Api.csproj -c Release --no-restore -o /out/api /p:UseAppHost=false
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) exit 1 ;; esac && dotnet ef migrations bundle --project src/JobSearch.Api --configuration Release --no-build --target-runtime "$rid" --output /out/migrations/efbundle
FROM mcr.microsoft.com/dotnet/aspnet:10.0.1@sha256:eaa79205c3ade4792a7f7bf310a3aac51fe0e1d91c44e40f70b7c6423d475fe0 AS runtime
USER root
RUN apt-get update && apt-get install --yes --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
FROM runtime AS migrations
COPY --from=build --chown=$APP_UID:$APP_UID /out/migrations/efbundle ./efbundle
COPY --from=build --chown=$APP_UID:$APP_UID /out/api/appsettings*.json ./
ENTRYPOINT ["/app/efbundle"]
FROM runtime AS api
COPY --from=build --chown=$APP_UID:$APP_UID /out/api/ ./
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=3 CMD curl --fail --silent --max-time 4 http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet","JobSearch.Api.dll"]

