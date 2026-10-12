#!/usr/bin/env bash
#
# Fail unless the public edge serves what the tracked Caddy configuration says
# it should: compressed HTML and JSON when the client offers zstd or gzip, and
# the security headers on the public site.
#
# The edge configuration used to be a file on the host that no deploy wrote, so
# changes to it shipped as green releases and never reached prod (#1598). This
# asks the live site rather than the compose file, so any future drift between
# the two fails the run instead of going unnoticed. Rationale in docs/ci.md
# ("Verifying the edge").
#
# Environment:
#   SITE_URL         origin to check, e.g. https://example.org (no trailing slash)
#   JSON_PATH        a JSON route large enough to be compressed (default /api/static/versions)
#   TIMEOUT_SECONDS  give up after this long (default 300)
#   POLL_SECONDS     delay between attempts (default 15)
#   HTML_HEADERS     override the response headers of SITE_URL/, for tests
#   JSON_HEADERS     override the response headers of SITE_URL$JSON_PATH, for tests
set -euo pipefail

: "${SITE_URL:?SITE_URL is required}"
JSON_PATH="${JSON_PATH:-/api/static/versions}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-300}"
POLL_SECONDS="${POLL_SECONDS:-15}"

# Echoes the response headers of a GET that offers zstd and gzip. The body is
# discarded undecoded: only what the edge declares matters here.
fetch_headers() {
  local url="$1" override="$2"
  if [ -n "${!override+set}" ]; then
    printf '%s\n' "${!override}"
    return
  fi
  curl -sS --max-time 30 -o /dev/null -D - \
    -H 'Accept-Encoding: zstd, gzip' \
    "$url"
}

# Value of header $2 in header block $1, lower-cased name match, CR stripped.
header_value() {
  tr -d '\r' <<< "$1" \
    | awk -v name="$(tr '[:upper:]' '[:lower:]' <<< "$2")" '
        { line = $0; split(line, kv, ":"); if (tolower(kv[1]) == name) {
            sub(/^[^:]*:[[:space:]]*/, "", line); print line; exit } }'
}

status_of() {
  tr -d '\r' <<< "$1" | awk 'toupper($1) ~ /^HTTP\// { code = $2 } END { print code }'
}

# One pass. Echoes a line per problem; silence means the edge is as configured.
inspect() {
  local html json encoding
  if ! html="$(fetch_headers "${SITE_URL}/" HTML_HEADERS)"; then
    echo "${SITE_URL}/: no response"
  else
    [ "$(status_of "$html")" = "200" ] || echo "${SITE_URL}/: answered $(status_of "$html"), expected 200"
    encoding="$(header_value "$html" Content-Encoding)"
    case "$encoding" in
      zstd|gzip) ;;
      *) echo "${SITE_URL}/: Content-Encoding is '${encoding}', expected zstd or gzip" ;;
    esac
    [ -n "$(header_value "$html" Strict-Transport-Security)" ] \
      || echo "${SITE_URL}/: no Strict-Transport-Security header"
    [ -n "$(header_value "$html" Content-Security-Policy)" ] \
      || echo "${SITE_URL}/: no Content-Security-Policy header"
  fi

  if ! json="$(fetch_headers "${SITE_URL}${JSON_PATH}" JSON_HEADERS)"; then
    echo "${SITE_URL}${JSON_PATH}: no response"
  else
    [ "$(status_of "$json")" = "200" ] || echo "${SITE_URL}${JSON_PATH}: answered $(status_of "$json"), expected 200"
    encoding="$(header_value "$json" Content-Encoding)"
    case "$encoding" in
      zstd|gzip) ;;
      *) echo "${SITE_URL}${JSON_PATH}: Content-Encoding is '${encoding}', expected zstd or gzip" ;;
    esac
  fi
  return 0
}

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
attempt=0

while :; do
  attempt=$(( attempt + 1 ))
  problems="$(inspect)"

  if [ -z "$problems" ]; then
    echo "Attempt ${attempt}: ${SITE_URL} compresses HTML and JSON and sends the security headers."
    exit 0
  fi

  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "::error::${SITE_URL} does not serve the tracked edge configuration after ${TIMEOUT_SECONDS}s"
    while IFS= read -r problem; do
      [ -n "$problem" ] && echo "::error::${problem}"
    done <<< "$problems"
    exit 1
  fi

  echo "Attempt ${attempt}: not as configured yet, retrying in ${POLL_SECONDS}s"
  printf '  %s\n' "$problems"
  sleep "$POLL_SECONDS"
done
