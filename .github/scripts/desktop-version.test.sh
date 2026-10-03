#!/usr/bin/env bash
#
# Tests for desktop-version.sh. Plain shell, same reasoning as
# resolve-preprod-version.test.sh: the unit is a few string rules, nothing to mock.
#
# Run: .github/scripts/desktop-version.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
resolve="${script_dir}/desktop-version.sh"

failures=0

expect() {
  local label="$1" want="$2" got
  shift 2
  got="$("${resolve}" "$@" 2>/dev/null | tr '\n' ' ' | sed 's/ $//')" || got="<failed>"
  if [ "${got}" = "${want}" ]; then
    echo "ok   ${label} → ${got}"
  else
    echo "FAIL ${label}: want '${want}', got '${got}'"
    failures=$((failures + 1))
  fi
}

expect_failure() {
  local label="$1"
  shift
  if "${resolve}" "$@" >/dev/null 2>&1; then
    echo "FAIL ${label}: expected a non-zero exit"
    failures=$((failures + 1))
  else
    echo "ok   ${label} → rejected"
  fi
}

expect "a bump builds production and a beta of that version" \
  "version=0.4.0 stable=true beta=0.4.0-beta.58" 0.4.0 58 false
expect "after the production build, betas head for the next patch" \
  "version=0.4.0 stable=false beta=0.4.1-beta.59" 0.4.0 59 true
expect "the patch carries over two digits" \
  "version=1.2.9 stable=false beta=1.2.10-beta.7" 1.2.9 7 true

expect_failure "a version without patch" 0.4 58 false
expect_failure "a version with a prerelease suffix" 0.4.0-rc.1 58 false
expect_failure "a version with a v prefix" v0.4.0 58 false
expect_failure "a run number that is not a number" 0.4.0 abc false
expect_failure "an unknown existence flag" 0.4.0 58 yes

if [ "${failures}" -gt 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
