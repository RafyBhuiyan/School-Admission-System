FROM node:22-alpine@sha256:0e1754758c2df4cfa3e68d59e2e1b7374bb7fd9ea6328eaaacf15cc934af8202 AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:stable-alpine@sha256:583b7f2fce1cdb8da5ad5c39ebd3c33b98b87d493190e548bdee1fa8c619c311 AS final
ENV API_BASE_URL=http://localhost:8080
COPY docker/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /src/dist/ /usr/share/nginx/html/
COPY --chmod=755 docker/40-runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh
RUN chown nginx:nginx /usr/share/nginx/html/config.json
USER nginx
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=3s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
CMD ["nginx", "-g", "daemon off;"]
