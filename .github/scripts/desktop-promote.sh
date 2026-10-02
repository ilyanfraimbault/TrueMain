#!/usr/bin/env bash
#
# Make `desktop-v<version>` the stable desktop release: the build truemain.lol
# offers on its download page and in the installed app's update feed (#1772).
#
# Usage: desktop-promote.sh <version>
#
# Called by desktop-held.sh, when production runs what a held production build
# reads (#1799), and by desktop-promote.yml, by hand — a rollback is the
# promotion of an older version. Needs GH_TOKEN (contents: write) and GH_REPO.
#
# The release must carry the production flavour (`truemain.dmg`, `truemain.exe`,
# `latest.json`). It may be a draft — a production build waiting for production
# to catch up — or a pre-release. It becomes a full release without GitHub's
# "Latest" badge, which stays the site's. Exactly one desktop release is stable:
# the previous one goes back to pre-release, and held drafts older than the
# promoted version are deleted, so none is promoted over it later.
set -euo pipefail

version="$1"
if ! [[ "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "::error::${version} is not a MAJOR.MINOR.PATCH version"
  exit 1
fi
tag="desktop-v${version}"

if ! release="$(gh release view "${tag}" --json assets)"; then
  echo "::error::No release ${tag}: merge a version bump to develop first"
  exit 1
fi
for asset in truemain.dmg truemain.exe latest.json; do
  if ! jq -e --arg asset "${asset}" 'any(.assets[].name; . == $asset)' <<< "${release}" > /dev/null; then
    echo "::error::${tag} has no ${asset} asset: only builds that carry the production flavour can be promoted"
    exit 1
  fi
done

gh release edit "${tag}" --draft=false --prerelease=false --latest=false

older() {
  [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | head -1)" = "$1" ]
}

gh api --paginate "repos/${GH_REPO}/releases?per_page=100" \
  --jq '.[] | select(.tag_name | test("^desktop-v[0-9]+\\.[0-9]+\\.[0-9]+$")) | [.tag_name, .draft, .prerelease] | @tsv' \
  | while IFS=$'\t' read -r other draft prerelease; do
      [ "${other}" = "${tag}" ] && continue
      if [ "${draft}" = "true" ]; then
        if older "${other}" "${tag}"; then
          gh release delete "${other}" --yes
          echo "Deleted the held ${other}: ${tag} supersedes it"
        fi
      elif [ "${prerelease}" = "false" ]; then
        gh release edit "${other}" --prerelease --latest=false
        echo "Moved ${other} back to pre-release"
      fi
    done

echo "truemain.lol now serves [${tag}](https://github.com/${GH_REPO}/releases/tag/${tag}) (download page and update feed, within five minutes)." >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
