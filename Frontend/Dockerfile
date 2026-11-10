FROM node:22-alpine@sha256:0e1754758c2df4cfa3e68d59e2e1b7374bb7fd9ea6328eaaacf15cc934af8202 AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:stable-alpine@sha256:583b7f2fce1cdb8da5ad5c39ebd3c33b98b87d493190e548bdee1fa8c619c311 AS final
ENV FRONTEND_HTTP_PORT=8080 \
    HEALTHCHECK_HOST=127.0.0.1 \
    NGINX_ENVSUBST_OUTPUT_DIR=/tmp/nginx-conf \
    NGINX_ENVSUBST_FILTER=^FRONTEND_HTTP_PORT$
COPY docker/nginx.conf /etc/nginx/nginx.conf
COPY docker/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/dist/ /usr/share/nginx/html/
COPY --chmod=755 docker/40-runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh
COPY --chmod=755 docker/healthcheck.sh /usr/local/bin/frontend-healthcheck
RUN mkdir -p /tmp/nginx-conf \
    && chown nginx:nginx /tmp/nginx-conf /usr/share/nginx/html/config.json
USER nginx
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=3s --retries=3 \
    CMD ["/usr/local/bin/frontend-healthcheck"]
CMD ["nginx", "-g", "daemon off;"]
