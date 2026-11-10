#!/bin/sh
set -eu

case "${FRONTEND_HTTP_PORT:-}" in ''|*[!0-9]*) echo 'FRONTEND_HTTP_PORT must be a valid port.' >&2; exit 1 ;; esac
if [ "${#FRONTEND_HTTP_PORT}" -gt 5 ] || [ "$FRONTEND_HTTP_PORT" -lt 1 ] || [ "$FRONTEND_HTTP_PORT" -gt 65535 ]; then
    echo 'FRONTEND_HTTP_PORT must be a valid port.' >&2
    exit 1
fi

# Without an override, retain the shared default already copied from public/config.json.
if [ "${API_BASE_URL+x}" != x ]; then exit 0; fi

invalid_url() {
    echo 'API_BASE_URL must be an absolute HTTP(S) URL without credentials, whitespace, query, or fragment.' >&2
    exit 1
}

case "${API_BASE_URL:-}" in http://?*|https://?*) ;; *) invalid_url ;; esac
case "$API_BASE_URL" in *\?*|*\#*|*[[:space:][:cntrl:]]*) invalid_url ;; esac
authority=${API_BASE_URL#*://}
authority=${authority%%/*}
case "$authority" in ''|*@*) invalid_url ;; esac

# Read the environment directly and escape JSON; never evaluate or substitute URL text as code.
awk 'BEGIN {
    value = ENVIRON["API_BASE_URL"]
    printf "{\"API_BASE_URL\":\""
    for (i = 1; i <= length(value); i++) {
        char = substr(value, i, 1)
        if (char == "\\" || char == "\"") printf "\\%s", char
        else printf "%s", char
    }
    printf "\"}\n"
}' > /usr/share/nginx/html/config.json
