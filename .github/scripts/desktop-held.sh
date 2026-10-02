#!/usr/bin/env bash
#
# Promote the newest held production build of the desktop app that the site
# release running in production is aligned with (#1799).
#
# Usage: desktop-held.sh <site release tag>
#
# The production app reads the production site: its API, and the pages it shares
# with the site (`web/layers/common`) as they were when it was built. A version
# bump merged to develop therefore builds the production flavour as a *draft*
# release (desktop-release.yml), and it goes out only once production runs
# everything it reads — when the site release:
#
#   - contains the bump commit (it was cut from develop after the merge), or
#   - does not differ from it on what the app reads: the site (`web/`) and the
#     API it fronts (`backend/Api`, `backend/Core`, `backend/Data`).
#
# Called by desktop-release.yml right after a bump's build, with the release
# production last deployed, and by deploy-prod.yml after each site release. A
# draft older than the stable release is never promoted over it. Needs a clone
# with full history and tags, GH_TOKEN (contents: write) and GH_REPO.
#
# RELEASES_JSON (the GitHub release list) and DESKTOP_PROMOTE (the promotion
# command) are overridable so desktop-held.test.sh runs the same logic offline.
set -euo pipefail

site_tag="$1"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
promote="${DESKTOP_PROMOTE:-${script_dir}/desktop-promote.sh}"
paths=(web backend/Api backend/Core backend/Data)

if ! git rev-parse --verify --quiet "${site_tag}^{commit}" > /dev/null; then
  echo "::error::No commit for the site release ${site_tag}: the clone needs full history and tags"
  exit 1
fi

releases="${RELEASES_JSON:-$(gh api --paginate "repos/${GH_REPO}/releases?per_page=100" | jq -s 'add // []')}"
production='select(.tag_name | test("^desktop-v[0-9]+\\.[0-9]+\\.[0-9]+$"))'
stable="$(jq -r ".[] | ${production} | select((.draft | not) and (.prerelease | not)) | .tag_name" <<< "${releases}" | head -1)"
held="$(jq -r ".[] | ${production} | select(.draft) | \"\(.tag_name) \(.target_commitish)\"" <<< "${releases}" | sort -V -r)"

if [ -z "${held}" ]; then
  echo "No production build of the app is waiting for production."
  exit 0
fi

newer() {
  [ -z "$2" ] || { [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | tail -1)" = "$1" ]; }
}

aligned() {
  git merge-base --is-ancestor "$1" "${site_tag}" 2> /dev/null \
    || git diff --quiet "${site_tag}" "$1" -- "${paths[@]}" 2> /dev/null
}

while read -r tag commit; do
  if ! newer "${tag}" "${stable}"; then
    echo "${tag} is not newer than the stable ${stable}: left as it is"
  elif aligned "${commit}"; then
    echo "${tag} reads what ${site_tag} serves: promoting it"
    "${promote}" "${tag#desktop-v}"
    exit 0
  else
    echo "::notice::${tag} waits for a site release: ${site_tag} neither contains ${commit:0:7} nor matches it on ${paths[*]}"
  fi
done <<< "${held}"
