#!/usr/bin/env bash
#
# Tests for edge-drift.sh. Plain shell, like verify-rollout.test.sh: the unit
# under test is a few jq filters over two API responses, PROJECTS_JSON and
# CONTENT_JSON already exist to feed them, and there is nothing else to mock.
#
# Run: .github/scripts/edge-drift.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
drift="${script_dir}/edge-drift.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

compose="${scratch}/compose.edge.yaml"
cat > "${compose}" <<'YAML'
services:
  caddy:
    image: caddy:2-alpine
YAML

container() {
  printf '{"image":"%s","state":"%s","health":"%s"}' "$1" "$2" "$3"
}

projects() {
  local state="${1:-running}"
  cat <<JSON
[
  {"name":"truemain","containers":[$(container "ghcr.io/owner/truemain-web:2.0.0" exited "")]},
  {"name":"truemain-edge","containers":[$(container "caddy:2-alpine" "${state}" "")]}
]
JSON
}

content_of() {
  jq -n --arg content "$1" '{content: $content, environment: null}'
}

# Runs the script with $1 as the project list and $2 as the content response,
# and checks the decision it wrote to GITHUB_OUTPUT, and that its reason names $4.
expect() {
  local label="$1" projects_json="$2" content_json="$3" want="$4" reason="$5" out decision
  : > "${scratch}/output"
  if ! out="$(COMPOSE_FILE="${compose}" PROJECT_NAME=truemain-edge \
      PROJECTS_JSON="${projects_json}" CONTENT_JSON="${content_json}" \
      GITHUB_OUTPUT="${scratch}/output" GITHUB_STEP_SUMMARY=/dev/null "${drift}" 2>&1)"; then
    echo "FAIL ${label}: exited non-zero:"
    printf '%s\n' "${out}" | sed 's/^/       /'
    failures=$((failures + 1))
    return
  fi
  decision="$(sed -n 's/^deploy=//p' "${scratch}/output")"
  if [ "${decision}" != "${want}" ]; then
    echo "FAIL ${label}: expected deploy=${want}, got deploy=${decision}: ${out}"
    failures=$((failures + 1))
  elif ! grep -qF "${reason}" <<< "${out}"; then
    echo "FAIL ${label}: no reason mentioning '${reason}', got: ${out}"
    failures=$((failures + 1))
  else
    echo "ok   ${label}"
  fi
}

same="$(content_of "$(cat "${compose}")")"

expect "an edge running this compose file is left alone" \
  "$(projects)" "${same}" false "leaving the edge untouched"

# Docker Manager may hand the file back without its final newline; that is not
# a change worth recreating Caddy for.
expect "a trailing newline is not a change" \
  "$(projects)" "$(content_of "$(cat "${compose}")"$'\n\n')" false "leaving the edge untouched"

expect "a changed compose file is redeployed" \
  "$(projects)" "$(content_of "services: {}")" true "holds a different"

expect "a missing project is deployed" \
  '[{"name":"truemain","containers":[]}]' "${same}" true "has no truemain-edge project"

expect "a project with no container is redeployed" \
  '[{"name":"truemain-edge","containers":[]}]' "${same}" true "runs no container"

# The cutover case: the edge project exists but Caddy could not start (the old
# in-stack Caddy still held the ports). Same compose file, yet it needs a deploy.
expect "a stopped edge is redeployed even when its file matches" \
  "$(projects exited)" "${same}" true "is not fully running"

# The app project's own exited container must not leak into the edge decision.
expect "another project's state is ignored" \
  "$(projects)" "${same}" false "leaving the edge untouched"

# An API failure must fail the step, never read as "missing".
out="$(COMPOSE_FILE="${compose}" PROJECT_NAME=truemain-edge HOSTINGER_API_KEY=x VM_ID=1 \
  API_ROOT="http://127.0.0.1:9" "${drift}" 2>&1)"
if [ $? -eq 0 ]; then
  echo "FAIL an unreachable API fails: exited zero: ${out}"
  failures=$((failures + 1))
else
  echo "ok   an unreachable API fails"
fi

if [ "${failures}" -gt 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all edge-drift cases pass"
