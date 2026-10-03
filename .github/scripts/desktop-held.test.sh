#!/usr/bin/env bash
#
# Tests for desktop-held.sh. Plain shell, same reasoning as
# resolve-preprod-version.test.sh: alignment is a question about a real git
# history, cheaper to build than to mock. The release list goes in through
# RELEASES_JSON, the promotion through DESKTOP_PROMOTE, a stub that records the
# version it was asked to promote.
#
# Run: .github/scripts/desktop-held.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
held="${script_dir}/desktop-held.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

stub="${scratch}/promote"
# shellcheck disable=SC2016 # the stub expands $1 itself, when called
printf '#!/usr/bin/env bash\necho "$1" > "%s/promoted"\n' "${scratch}" > "${stub}"
chmod +x "${stub}"
export DESKTOP_PROMOTE="${stub}"

repo="${scratch}/repo"
git_() { git -C "${repo}" -c user.email=t@t -c user.name=t "$@"; }

# Commits `$1` touching file `$2`; prints its SHA.
commit() {
  mkdir -p "$(dirname "${repo}/$2")"
  echo "$1" >> "${repo}/$2"
  git_ add -A
  git_ commit -q -m "$1"
  git_ rev-parse HEAD
}

# History: the site release 1.0.0 on `master`, then develop moves on.
rm -rf "${repo}"
mkdir -p "${repo}"
git_ init -q -b develop
commit "first" web/app.vue > /dev/null
git_ tag 1.0.0
desktop_only="$(commit "app change" desktop/app/x.ts)"
site_change="$(commit "site change" web/layers/common/y.vue)"
git_ tag 1.1.0
after_release="$(commit "readme" desktop/README.md)"

# A release list entry: tag, draft, prerelease, target commit.
entry() {
  printf '{"tag_name":"%s","draft":%s,"prerelease":%s,"target_commitish":"%s"}' "$1" "$2" "$3" "${4:-develop}"
}

expect() {
  local label="$1" site="$2" releases="$3" want="$4" got
  rm -f "${scratch}/promoted"
  if ! (cd "${repo}" && RELEASES_JSON="${releases}" "${held}" "${site}" > /dev/null 2>&1); then
    got="<failed>"
  else
    got="$(cat "${scratch}/promoted" 2>/dev/null || echo none)"
  fi
  if [ "${got}" = "${want}" ]; then
    echo "ok   ${label} → ${got}"
  else
    echo "FAIL ${label}: want '${want}', got '${got}'"
    failures=$((failures + 1))
  fi
}

expect "nothing held, nothing promoted" 1.0.0 \
  "[$(entry desktop-v0.3.0 false false)]" none

expect "a bump that only changed the app reads what an older site release serves" 1.0.0 \
  "[$(entry desktop-v0.4.0 true false "${desktop_only}")]" 0.4.0

expect "a bump after a site change waits for a release that has it" 1.0.0 \
  "[$(entry desktop-v0.4.0 true false "${site_change}")]" none

expect "the site release that contains the bump promotes it" 1.1.0 \
  "[$(entry desktop-v0.4.0 true false "${site_change}")]" 0.4.0

expect "a bump merged after the release matches it outside the site" 1.1.0 \
  "[$(entry desktop-v0.4.0 true false "${after_release}")]" 0.4.0

expect "the newest aligned build wins over an older one" 1.1.0 \
  "[$(entry desktop-v0.4.0 true false "${desktop_only}"),$(entry desktop-v0.5.0 true false "${site_change}")]" 0.5.0

expect "an older build is promoted while a newer one still waits" 1.0.0 \
  "[$(entry desktop-v0.5.0 true false "${site_change}"),$(entry desktop-v0.4.0 true false "${desktop_only}")]" 0.4.0

expect "a held build older than the stable release stays held" 1.1.0 \
  "[$(entry desktop-v0.4.0 false false),$(entry desktop-v0.3.0 true false "${desktop_only}")]" none

expect "versions compare as numbers, not text" 1.1.0 \
  "[$(entry desktop-v0.9.0 false false),$(entry desktop-v0.10.0 true false "${desktop_only}")]" 0.10.0

expect "betas and withdrawn releases are never promoted" 1.1.0 \
  "[$(entry desktop-v0.5.0-beta.3 true true "${desktop_only}"),$(entry desktop-v0.4.0 false true "${desktop_only}")]" none

expect "an unknown commit is not aligned" 1.1.0 \
  "[$(entry desktop-v0.4.0 true false 0000000000000000000000000000000000000000)]" none

expect "an unknown site release fails" 9.9.9 \
  "[$(entry desktop-v0.4.0 true false "${desktop_only}")]" "<failed>"

if [ "${failures}" -gt 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
