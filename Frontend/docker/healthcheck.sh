#!/bin/sh
set -eu

# Use the same port supplied to the Nginx template, independently of the browser's origin.
target=$(printf 'http://%s:%s/healthz' "$HEALTHCHECK_HOST" "$FRONTEND_HTTP_PORT")
exec wget -Y off -q -O /dev/null "$target"
