#!/usr/bin/env bash
#
# Tests for verify-edge-running.sh, in the same plain shell as
# verify-rollout.test.sh. Every case runs with TIMEOUT_SECONDS=0 so a failing
# state is reported on the first pass.
#
# Run: .github/scripts/verify-edge-running.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
verify="${script_dir}/verify-edge-running.sh"

failures=0

projects() {
  printf '[{"name":"truemain","containers":[{"image":"x:1","state":"exited","health":""}]},'
  printf '{"name":"truemain-edge","containers":[{"image":"caddy:2-alpine","state":"%s","health":"%s"}]}]' \
    "${1:-running}" "${2:-}"
}

run_verify() {
  PROJECT_NAME="${PROJECT_NAME:-truemain-edge}" TIMEOUT_SECONDS=0 POLL_SECONDS=1 \
    PROJECTS_JSON="$1" "${verify}" 2>&1
}

expect_pass() {
  local out
  if out="$(run_verify "$2")"; then
    echo "ok   $1"
  else
    echo "FAIL $1: expected success, got:"
    printf '%s\n' "${out}" | sed 's/^/       /'
    failures=$((failures + 1))
  fi
}

expect_fail() {
  local out
  if out="$(run_verify "$2")"; then
    echo "FAIL $1: expected a non-zero exit"
    failures=$((failures + 1))
  elif grep -qF "$3" <<< "${out}"; then
    echo "ok   $1"
  else
    echo "FAIL $1: no error mentioning '$3', got:"
    printf '%s\n' "${out}" | sed 's/^/       /'
    failures=$((failures + 1))
  fi
}

# Caddy has no healthcheck: an empty health is not a problem.
expect_pass "a running Caddy passes" "$(projects running)"
expect_pass "another project's exited container is ignored" "$(projects running)"
expect_fail "a Caddy that could not start fails" "$(projects created)" "caddy:2-alpine: created"
expect_fail "an unhealthy container fails" "$(projects running unhealthy)" "unhealthy"
PROJECT_NAME=truemain-absent expect_fail "a missing project fails" "$(projects)" "does not exist"
expect_fail "an empty project fails" '[{"name":"truemain-edge","containers":[]}]' "no container at all"

if [ "${failures}" -gt 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all verify-edge-running cases pass"
