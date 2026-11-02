# GoldataWeb2 web (React build + nginx). Build from the repo root:
#   docker buildx build -f deploy/web.Dockerfile --platform linux/amd64 -t $REGISTRY/goldata/goldata-web:$TAG .

FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# Free & open source only (NFR-LIC-2): fails the build on any other licence.
RUN npm run licenses && npm run build && npm run notices

FROM nginxinc/nginx-unprivileged:stable-alpine
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
ENV TZ=Asia/Shanghai
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/index.html || exit 1
