# The runtime image for Compass.Api. Built from `api/` — `docker build -t compass-api api` — and
# pushed to ECR; see README.md for running it against the local Compose Postgres.
#
# Two things about its shape are load-bearing for the deployed stack:
#
#   * It runs on the port the base image already defaults to (8080, via ASPNETCORE_HTTP_PORTS), and
#     nothing here changes it. The load balancer's target group depends on that number silently:
#     get it wrong and every health check fails with no clue as to why.
#   * The app is a CMD, not an ENTRYPOINT, so an ECS `command` override replaces it outright.
#     That is how migrations run: the same image with `["./efbundle"]` instead. An ENTRYPOINT would
#     prepend `dotnet Compass.Api.dll` to the override and the migration task would never run.

# The SDK version tracks api/global.json; the build fails outright if the two drift apart, which
# is the point of pinning both. The runtime tag floats on the 10.0 patch line so the image picks up
# runtime security fixes without a code change.
ARG DOTNET_SDK_VERSION=10.0.300
ARG DOTNET_RUNTIME_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, from the manifests alone, so editing a .cs file does not re-download the world.
COPY global.json ./
COPY .config/dotnet-tools.json .config/
COPY Compass.Api/Compass.Api.csproj Compass.Api/
RUN dotnet restore Compass.Api/Compass.Api.csproj -a "$TARGETARCH"

COPY Compass.Api/ Compass.Api/
RUN dotnet publish Compass.Api/Compass.Api.csproj -a "$TARGETARCH" -c Release --no-restore -o /app

# The migration bundle: the committed migrations compiled into one executable that needs neither
# the SDK nor the sources, because the runtime image has neither. `dotnet ef database update` — the
# local command — is not available there. Framework-dependent, so it runs on the runtime image's
# shared framework. `dotnet ef` is the version pinned in .config/dotnet-tools.json, not a global.
#
# Generating the bundle builds the app and resolves AppDbContext, so DatabaseConnectionString.Resolve
# runs here and throws unless a connection string is configured — even though nothing connects to a
# database while bundling. `dotnet ef` runs the app as Development, so appsettings.Development.json,
# copied in above, is what satisfies it. That makes the file load-bearing at build time: keep it out
# of .dockerignore, however much dev configuration in a production image looks like something to
# prune. Excluded, this step fails with a message about finding 'DbContext' types that never
# mentions the file that went missing.
RUN dotnet tool restore \
 && dotnet ef migrations bundle \
      --project Compass.Api/Compass.Api.csproj \
      --configuration Release \
      --target-runtime "linux-$(test "$TARGETARCH" = amd64 && echo x64 || echo "$TARGETARCH")" \
      --output /app/efbundle \
      --force

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final
WORKDIR /app
COPY --from=build /app ./

EXPOSE 8080
USER $APP_UID
CMD ["dotnet", "Compass.Api.dll"]
