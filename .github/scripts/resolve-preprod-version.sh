#!/usr/bin/env bash
#
# Resolve the version for a preprod build: `<base>-rc.<N>`, printed on stdout.
#
# Lives in its own file rather than inline in deploy-preprod.yml so the logic
# that ships is the logic under test — a test that re-implemented these rules
# could agree with itself while drifting from the workflow.
#
# Reads PREPROD_VERSION_BASE from the environment (empty = derive the base) and
# the tag list from the git repo in the working directory, so it needs a clone
# with full tags (`fetch-depth: 0`).
set -euo pipefail

base_override="${PREPROD_VERSION_BASE:-}"

# Releases are bare `MAJOR.MINOR.PATCH` tags cut on master. Filter to exactly
# that shape: git's version sort ranks `1.20.0-rc.4` ABOVE `1.20.0`, so an
# unfiltered `head -1` would read a prerelease as the latest release and
# compound the base on every merge.
latest_release="$(git tag --list --sort=-v:refname \
  | grep -E '^[0-9]+\.[0-9]+\.[0-9]+$' | head -1 || true)"
latest_release="${latest_release:-0.0.0}"

# The base is the version this preprod line is heading for. Default to the minor
# bump, which is what a plain "release" cuts. When the next release is known to
# be a major, set PREPROD_VERSION_BASE and clear it once that release is cut —
# the actual bump is still decided at release time, so this is only a label.
if [ -n "${base_override}" ]; then
  if ! printf '%s' "${base_override}" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "PREPROD_VERSION_BASE must be MAJOR.MINOR.PATCH, got '${base_override}'." >&2
    exit 1
  fi
  base="${base_override}"
else
  major="${latest_release%%.*}"
  rest="${latest_release#*.}"
  minor="${rest%%.*}"
  base="${major}.$((minor + 1)).0"
fi

# Highest existing counter on this base, +1 — so the sequence resets on its own
# once a release moves the base, and a deleted tag can never make us reuse a
# number.
n="$(git tag --list "${base}-rc.*" \
  | sed "s/^${base}-rc\.//" | grep -E '^[0-9]+$' | sort -n | tail -1 || true)"

printf '%s-rc.%s\n' "${base}" "$(( ${n:-0} + 1 ))"
