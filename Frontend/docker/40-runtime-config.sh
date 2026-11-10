#!/bin/sh
set -eu

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
