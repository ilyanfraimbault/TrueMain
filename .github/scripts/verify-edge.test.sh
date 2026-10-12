#!/usr/bin/env bash
#
# Tests for verify-edge.sh. Plain shell, same reasoning as
# verify-rollout.test.sh: the unit under test reads response headers,
# HTML_HEADERS / JSON_HEADERS already exist to feed it some, and there is
# nothing else to mock.
#
# Every case runs with TIMEOUT_SECONDS=0 so a failing state is reported on the
# first pass instead of being polled.
#
# Run: .github/scripts/verify-edge.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
verify="${script_dir}/verify-edge.sh"

failures=0

html_ok=$'HTTP/2 200\r\ncontent-type: text/html;charset=utf-8\r\ncontent-encoding: zstd\r\nstrict-transport-security: max-age=31536000; includeSubDomains\r\ncontent-security-policy: default-src \'self\'\r\n'
json_ok=$'HTTP/2 200\r\ncontent-type: application/json\r\ncontent-encoding: gzip\r\n'

# Runs the check against fixed headers; echoes the exit status then the output.
run() {
  local html="$1" json="$2" out status
  out="$(SITE_URL=https://edge.test TIMEOUT_SECONDS=0 POLL_SECONDS=0 \
    HTML_HEADERS="$html" JSON_HEADERS="$json" "$verify" 2>&1)"
  status=$?
  printf '%s\n%s\n' "$status" "$out"
}

expect() {
  local name="$1" want_status="$2" want_text="$3" result status
  result="$(run "$4" "$5")"
  status="$(head -n1 <<< "$result")"
  if [ "$status" != "$want_status" ]; then
    echo "FAIL ${name}: exit ${status}, expected ${want_status}"
    tail -n +2 <<< "$result" | sed 's/^/    /'
    failures=$(( failures + 1 ))
  elif ! grep -qF -- "$want_text" <<< "$result"; then
    echo "FAIL ${name}: output lacks '${want_text}'"
    tail -n +2 <<< "$result" | sed 's/^/    /'
    failures=$(( failures + 1 ))
  else
    echo "ok   ${name}"
  fi
}

expect "configured edge passes" 0 "compresses HTML and JSON" "$html_ok" "$json_ok"

expect "uncompressed HTML fails" 1 "/: Content-Encoding is '', expected zstd or gzip" \
  "${html_ok/content-encoding: zstd$'\r\n'/}" "$json_ok"

expect "unexpected encoding fails" 1 "Content-Encoding is 'br'" \
  "${html_ok/content-encoding: zstd/content-encoding: br}" "$json_ok"

expect "uncompressed JSON fails" 1 "/api/static/versions: Content-Encoding is ''" \
  "$html_ok" $'HTTP/2 200\r\ncontent-type: application/json\r\n'

expect "missing HSTS fails" 1 "no Strict-Transport-Security header" \
  "${html_ok/strict-transport-security: max-age=31536000; includeSubDomains$'\r\n'/}" "$json_ok"

expect "missing CSP fails" 1 "no Content-Security-Policy header" \
  "${html_ok/content-security-policy: default-src \'self\'$'\r\n'/}" "$json_ok"

expect "header names are case-insensitive" 0 "compresses HTML and JSON" \
  $'HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nStrict-Transport-Security: max-age=1\r\nContent-Security-Policy: default-src \'self\'\r\n' \
  "$json_ok"

expect "non-200 fails" 1 "answered 502, expected 200" \
  "${html_ok/HTTP\/2 200/HTTP/2 502}" "$json_ok"

if [ "$failures" -gt 0 ]; then
  echo "${failures} case(s) failed"
  exit 1
fi
echo "all cases passed"
