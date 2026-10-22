#!/usr/bin/env bash
#
# Tests for ci-ssh-command.sh, the CI key's forced command on the VPS. Plain
# shell, same reasoning as apply-migration.test.sh: its collaborators are
# `docker` and the migration command, and a stub of each that records how it
# was called is all a case needs.
#
# Run: .github/scripts/vps/ci-ssh-command.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
command="${script_dir}/ci-ssh-command.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

mkdir -p "${scratch}/bin" "${scratch}/project"
cat > "${scratch}/bin/docker" <<'STUB'
#!/usr/bin/env bash
# Records its arguments, working directory and IMAGE_TAG, then exits with
# $DOCKER_CODE (read from a file: the forced command clears the environment).
printf '%s\n' "$*" > "${0%/bin/docker}/docker.args"
pwd > "${0%/bin/docker}/docker.cwd"
printf '%s\n' "${IMAGE_TAG-<unset>}" > "${0%/bin/docker}/docker.tag"
exit "$(cat "${0%/bin/docker}/docker.code")"
STUB
cat > "${scratch}/bin/migrate" <<'STUB'
#!/usr/bin/env bash
cat > "${0%/bin/migrate}/migrate.stdin"
exit 0
STUB
chmod +x "${scratch}/bin/docker" "${scratch}/bin/migrate"

export PATH="${scratch}/bin:${PATH}"
sha=0123456789abcdef0123456789abcdef01234567

fail() {
  local message="$1"
  echo "FAIL ${message}"
  failures=$((failures + 1))
  return 0
}

# run <request> [docker exit code]: runs the forced command for one request,
# sets $got_exit and $got_out
run() {
  local request="$1" docker_code="${2:-0}"
  rm -f "${scratch}"/docker.args "${scratch}"/docker.cwd "${scratch}"/docker.tag "${scratch}"/migrate.stdin
  echo "$docker_code" > "${scratch}/docker.code"
  got_exit=0
  got_out="$(printf 'SELECT 1;\n' | SSH_ORIGINAL_COMMAND="$request" IMAGE_TAG=from-client \
    "$command" "${scratch}/project" "${scratch}/bin/migrate" 2>/dev/null)" || got_exit=$?
  return 0
}

expect_migrate() {
  local label="$1" request="$2"
  run "$request"
  if [[ "$got_exit" = 0 ]] && [[ "$(cat "${scratch}/migrate.stdin" 2>/dev/null)" = "SELECT 1;" ]] \
    && [[ ! -e "${scratch}/docker.args" ]]; then
    echo "ok   ${label} → migration fed stdin"
  else
    fail "${label}: want the migration run with stdin, got exit ${got_exit}"
  fi
  return 0
}

expect_pull() {
  local label="$1" tag="$2"
  run "pull ${tag}"
  if [[ "$got_exit" = 0 ]] \
    && [[ "$(cat "${scratch}/docker.args")" = "compose pull api web admin ingestor" ]] \
    && [[ "$(cat "${scratch}/docker.cwd")" = "${scratch}/project" ]] \
    && [[ "$(cat "${scratch}/docker.tag")" = "$tag" ]] \
    && [[ "$got_out" = "ci-ssh-command: pulled ${tag}" ]] \
    && [[ ! -e "${scratch}/migrate.stdin" ]]; then
    echo "ok   ${label} → pulled ${tag}"
  else
    fail "${label}: want a pull of ${tag}, got exit ${got_exit}, docker '$(cat "${scratch}/docker.args" 2>/dev/null)' tag '$(cat "${scratch}/docker.tag" 2>/dev/null)', output '${got_out}'"
  fi
  return 0
}

expect_refused() {
  local label="$1" request="$2"
  run "$request"
  if [[ "$got_exit" = 64 ]] && [[ ! -e "${scratch}/docker.args" ]] && [[ ! -e "${scratch}/migrate.stdin" ]]; then
    echo "ok   ${label} → refused"
  else
    fail "${label}: want exit 64 with nothing run, got exit ${got_exit}"
  fi
  return 0
}

expect_migrate "no remote command runs the migration" ""
expect_migrate "migrate runs the migration" "migrate"

expect_pull "a full git sha" "$sha"
expect_pull "a short git sha" "0123abc"
expect_pull "a release" "1.22.0"
expect_pull "a pre-release" "1.23.0-rc.4"

expect_refused "an unknown command" "ls"
expect_refused "pull without a tag" "pull"
expect_refused "pull with an empty tag" "pull "
expect_refused "a moving tag" "pull latest"
expect_refused "an uppercase sha" "pull 0123ABC"
expect_refused "a sha too short" "pull 0123ab"
expect_refused "a sha too long" "pull ${sha}0"
expect_refused "an incomplete version" "pull 1.22"
expect_refused "a v-prefixed version" "pull v1.22.0"
expect_refused "a second argument" "pull 1.22.0 extra"
expect_refused "two spaces" "pull  1.22.0"
expect_refused "a leading space" " pull 1.22.0"
expect_refused "a chained command" "pull 1.22.0;id"
# shellcheck disable=SC2016 # the request must reach the command unexpanded
expect_refused "a command substitution" 'pull $(id)'
expect_refused "a newline" "pull 1.22.0
id"
expect_refused "a path" "pull ../1.22.0"
expect_refused "an uppercase verb" "PULL 1.22.0"
expect_refused "migrate with an argument" "migrate now"

run "pull 1.22.0" 1
if [[ "$got_exit" = 1 ]] && [[ -z "$got_out" ]]; then
  echo "ok   a failed pull → exit 1, no success line"
else
  fail "a failed pull: want exit 1 and no success line, got exit ${got_exit}, output '${got_out}'"
fi

got_exit=0
SSH_ORIGINAL_COMMAND="pull 1.22.0" "$command" >/dev/null 2>&1 || got_exit=$?
if [[ "$got_exit" != 0 ]]; then
  echo "ok   no project directory → exit ${got_exit}"
else
  fail "no project directory: want a failure, got exit 0"
fi

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
