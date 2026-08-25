#!/usr/bin/env bash
#
# Tests for resolve-preprod-version.sh. Plain shell on purpose: it needs a real
# git repo with real tags (the whole point is how git sorts and filters them),
# and that is cheaper to build than to mock, so there is nothing a test
# framework would add here beyond a dependency.
#
# Run: .github/scripts/resolve-preprod-version.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
resolve="${script_dir}/resolve-preprod-version.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

# A throwaway repo with one empty commit; tags are all we care about.
fresh_repo() {
  rm -rf "${scratch}/repo"
  mkdir -p "${scratch}/repo"
  git -C "${scratch}/repo" init -q
  git -C "${scratch}/repo" -c user.email=t@t -c user.name=t commit -q --allow-empty -m init
}

tag() {
  for t in "$@"; do git -C "${scratch}/repo" tag "$t"; done
}

expect() {
  local label="$1" want="$2" got
  got="$(cd "${scratch}/repo" && "${resolve}" 2>/dev/null)" || got="<failed>"
  if [ "${got}" = "${want}" ]; then
    echo "ok   ${label} → ${got}"
  else
    echo "FAIL ${label}: want '${want}', got '${got}'"
    failures=$((failures + 1))
  fi
}

expect_failure() {
  local label="$1"
  if (cd "${scratch}/repo" && "${resolve}" >/dev/null 2>&1); then
    echo "FAIL ${label}: expected a non-zero exit"
    failures=$((failures + 1))
  else
    echo "ok   ${label} → rejected"
  fi
}

export PREPROD_VERSION_BASE=""

fresh_repo
expect "no tags at all bootstraps at 0.1.0" "0.1.0-rc.1"

fresh_repo
tag 1.18.0 1.19.0
expect "base is the minor bump after the latest release" "1.20.0-rc.1"

tag 1.20.0-rc.1
expect "counter increments" "1.20.0-rc.2"

tag 1.20.0-rc.2 1.20.0-rc.3
expect "counter follows the highest, not the count" "1.20.0-rc.4"

git -C "${scratch}/repo" tag -d 1.20.0-rc.2 >/dev/null
expect "a deleted tag never makes the counter reuse a number" "1.20.0-rc.4"

# The trap this whole design has to survive: git ranks 1.20.0-rc.4 ABOVE 1.20.0,
# so an unfiltered "latest tag" read would compound the base to 1.21.0 and keep
# climbing on every merge.
tag 1.20.0
expect "a release resets the counter and moves the base exactly one minor" "1.21.0-rc.1"

# Two-digit counters must sort numerically, not lexically — `sort -n`, not `sort`.
fresh_repo
tag 1.19.0
tag 1.20.0-rc.9 1.20.0-rc.10
expect "counters sort numerically past 9" "1.20.0-rc.11"

# Prerelease tags on an unrelated base must not leak into this one's counter.
fresh_repo
tag 1.19.0 1.30.0-rc.7
expect "another base's counter is ignored" "1.20.0-rc.1"

fresh_repo
tag 1.19.0
PREPROD_VERSION_BASE=2.0.0 expect "an explicit base overrides the derived one" "2.0.0-rc.1"

fresh_repo
tag 1.19.0
PREPROD_VERSION_BASE=nope expect_failure "a malformed override is rejected rather than guessed"

if [ "${failures}" -ne 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
