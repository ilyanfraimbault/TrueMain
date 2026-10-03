#!/usr/bin/env bash
#
# Resolve what a Desktop release run builds (#1799), as `key=value` lines for
# $GITHUB_OUTPUT.
#
# Usage: desktop-version.sh <app version> <run number> <production build exists: true|false>
#
#   version=X.Y.Z       the app version, `version` in desktop/src-tauri/tauri.conf.json
#   stable=true|false   whether this run builds the production flavour — only when
#                       no `desktop-vX.Y.Z` release exists yet, i.e. after a bump
#   beta=X.Y.Z-beta.N   the version of this run's preprod build
#
# Every run builds the preprod flavour, so its version must be new and ordered:
# the Tauri updater installs a build only when its version is strictly greater
# than the installed one, which a commit SHA is not. The run number orders it. It
# is a prerelease of the version being worked towards — X.Y.Z while that
# version's production build is not out, the next patch once it is — because
# semver ranks `0.4.0-beta.N` below `0.4.0`, and a beta built after 0.4.0 shipped
# is newer than it.
#
# Lives in its own file, not inline in desktop-release.yml, so the logic that
# ships is the logic desktop-version.test.sh runs.
set -euo pipefail

version="$1" run="$2" stable_exists="$3"

if ! [[ "${version}" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  echo "::error::${version} is not a MAJOR.MINOR.PATCH version (desktop/src-tauri/tauri.conf.json)" >&2
  exit 1
fi
major="${BASH_REMATCH[1]}" minor="${BASH_REMATCH[2]}" patch="${BASH_REMATCH[3]}"

if ! [[ "${run}" =~ ^[0-9]+$ ]]; then
  echo "::error::The run number must be a positive integer, got '${run}'" >&2
  exit 1
fi

case "${stable_exists}" in
  true) stable=false base="${major}.${minor}.$((patch + 1))" ;;
  false) stable=true base="${version}" ;;
  *)
    echo "::error::Whether the production build exists must be true or false, got '${stable_exists}'" >&2
    exit 1
    ;;
esac

printf 'version=%s\nstable=%s\nbeta=%s-beta.%s\n' "${version}" "${stable}" "${base}" "${run}"
