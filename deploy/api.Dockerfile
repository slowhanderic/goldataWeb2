# GoldataWeb2 API (REST + MCP). Build from the repo root:
#   docker buildx build -f deploy/api.Dockerfile --platform linux/amd64 -t $REGISTRY/goldata/goldata-api:$TAG .
# Alpine (musl) runtime on purpose: DSM 7.1's Docker 20.10 seccomp profile breaks newer glibc images (002-spec §9.0).

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY db/migrations/ db/migrations/
RUN dotnet restore src/Goldata.Api/Goldata.Api.csproj -a $TARGETARCH
RUN dotnet publish src/Goldata.Api/Goldata.Api.csproj -c Release -a $TARGETARCH --no-restore -o /app \
    -p:UseAppHost=true -p:DebugType=none

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# ICU for zh-CN formatting/sorting, tzdata for Asia/Shanghai.
RUN apk add --no-cache icu-libs icu-data-full tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=Asia/Shanghai
WORKDIR /app
COPY --from=build /app ./
# Non-root by default (the image's 'app' user); compose overrides with the PHP files' UID:GID (§9.6).
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["/app/Goldata.Api", "--healthcheck"]
ENTRYPOINT ["/app/Goldata.Api"]
