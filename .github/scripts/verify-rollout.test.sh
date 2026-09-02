#!/usr/bin/env bash
#
# Tests for verify-rollout.sh. Plain shell on purpose, same reasoning as
# resolve-preprod-version.test.sh: the unit under test is a jq filter over an
# API response, PROJECTS_JSON already exists to feed it one, and there is
# nothing else to mock.
#
# The fixture is the shape the Hostinger API really returns, trimmed to the
# fields the check reads.
#
# Every case runs with TIMEOUT_SECONDS=0 so a failing state is reported on the
# first pass instead of being polled for ten minutes; the polling gets its own
# case.
#
# Run: .github/scripts/verify-rollout.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
verify="${script_dir}/verify-rollout.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

compose="${scratch}/compose.yaml"
cat > "${compose}" <<'YAML'
services:
  api:
    image: ghcr.io/owner/truemain-api:${IMAGE_TAG:-latest}
  web:
    image: ghcr.io/owner/truemain-web:${IMAGE_TAG:-latest}
  ingestor:
    image: ghcr.io/owner/truemain-ingestor:${IMAGE_TAG:-latest}
  ingestor-aggregate:
    image: ghcr.io/owner/truemain-ingestor:${IMAGE_TAG:-latest}
  postgres:
    image: postgres:17.2
YAML

container() {
  printf '{"image":"%s","state":"%s","health":"%s"}' "$1" "$2" "$3"
}

# The response a fully rolled project produces: every image at $1, both ingestor
# lanes, an unrelated project, and containers with no healthcheck of their own.
projects() {
  local tag="${1:-2.0.0}"
  local api="${2:-running}" api_health="${3:-healthy}"
  cat <<JSON
[
  {"name":"other-project","containers":[$(container "something:1.0" running "")]},
  {"name":"truemain","containers":[
    $(container "ghcr.io/owner/truemain-api:${tag}" "${api}" "${api_health}"),
    $(container "ghcr.io/owner/truemain-web:${tag}" running healthy),
    $(container "ghcr.io/owner/truemain-ingestor:${tag}" running healthy),
    $(container "ghcr.io/owner/truemain-ingestor:${tag}" running healthy),
    $(container "postgres:17.2" running healthy),
    $(container "dpage/pgadmin4:9.17" running "")
  ]}
]
JSON
}

run_verify() {
  COMPOSE_FILE="${compose}" PROJECT_NAME="${PROJECT_NAME:-truemain}" \
    EXPECTED_TAG="${EXPECTED_TAG:-2.0.0}" \
    TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-0}" POLL_SECONDS="${POLL_SECONDS:-1}" \
    PROJECTS_JSON="$1" "${verify}" 2>&1
}

expect_pass() {
  local label="$1" out
  if out="$(run_verify "$2")"; then
    echo "ok   ${label}"
  else
    echo "FAIL ${label}: expected success, got:"
    printf '%s\n' "${out}" | sed 's/^/       /'
    failures=$((failures + 1))
  fi
}

# Fails, and names $3 while doing so — a check that fails for the wrong reason
# is not the check we want.
expect_fail() {
  local label="$1" out
  if out="$(run_verify "$2")"; then
    echo "FAIL ${label}: expected a non-zero exit"
    failures=$((failures + 1))
  elif grep -qF "$3" <<< "${out}"; then
    echo "ok   ${label}"
  else
    echo "FAIL ${label}: no error mentioning '$3', got:"
    printf '%s\n' "${out}" | sed 's/^/       /'
    failures=$((failures + 1))
  fi
}

expect_pass "a fully rolled project passes" "$(projects 2.0.0)"

# The incident this check exists for: the deploy reported success, the API had
# accepted it, and every container was still on the previous release.
expect_fail "a project still on the old tag fails" "$(projects 1.19.4)" \
  "still running, expected ghcr.io/owner/truemain-api:2.0.0"

expect_fail "one service left behind fails" \
  "$(projects 2.0.0 | sed 's#truemain-web:2\.0\.0#truemain-web:1.19.4#')" \
  "ghcr.io/owner/truemain-web:1.19.4"

# Both ingestor lanes share one image, so rolling only one of them is a partial
# deploy that a per-service check would bless.
expect_fail "one of two lanes on the same image left behind fails" \
  "$(projects 2.0.0 | sed '0,/truemain-ingestor:2\.0\.0/! s#truemain-ingestor:2\.0\.0#truemain-ingestor:1.19.4#')" \
  "ghcr.io/owner/truemain-ingestor:1.19.4"

expect_fail "a missing container fails" \
  "$(projects 2.0.0 | grep -v truemain-web)" \
  "ghcr.io/owner/truemain-web: no container running"

expect_fail "an unhealthy container fails" "$(projects 2.0.0 running unhealthy)" "unhealthy"
expect_fail "a container still starting fails" "$(projects 2.0.0 running starting)" "starting"
expect_fail "an exited container fails" "$(projects 2.0.0 exited "")" "exited"

# A container with no healthcheck reports an empty health and must not be read
# as unhealthy; pgadmin and the umami sidecars are in that state permanently.
expect_pass "a container with no healthcheck of its own passes" "$(projects 2.0.0 running "")"

PROJECT_NAME=truemain-absent expect_fail "a project the API does not know fails" \
  "$(projects 2.0.0)" "reports no container at all"

expect_fail "an empty project list fails" "[]" "reports no container at all"

# A compose file naming no image of ours would make every assertion vacuous.
cat > "${scratch}/no-images.yaml" <<'YAML'
services:
  postgres:
    image: postgres:17.2
YAML
out="$(COMPOSE_FILE="${scratch}/no-images.yaml" PROJECT_NAME=truemain EXPECTED_TAG=2.0.0 \
  TIMEOUT_SECONDS=0 PROJECTS_JSON="$(projects 2.0.0)" "${verify}" 2>&1)"
if [ $? -eq 0 ]; then
  echo "FAIL a compose file with no image of ours must not pass vacuously"
  failures=$((failures + 1))
elif grep -qF "no ghcr.io image found" <<< "${out}"; then
  echo "ok   a compose file with no image of ours fails rather than passing vacuously"
else
  echo "FAIL vacuous-compose case failed for the wrong reason:"
  printf '%s\n' "${out}" | sed 's/^/       /'
  failures=$((failures + 1))
fi

# The wait is what lets a slow but successful roll pass, so it has to retry
# rather than judge the first response.
started="$(date +%s)"
attempts="$(TIMEOUT_SECONDS=3 POLL_SECONDS=1 run_verify "$(projects 1.19.4)" | grep -c '^Attempt')"
elapsed=$(( $(date +%s) - started ))
if [ "${attempts}" -ge 2 ] && [ "${elapsed}" -ge 2 ]; then
  echo "ok   it polls until the timeout instead of judging one response (${attempts} attempts in ${elapsed}s)"
else
  echo "FAIL expected repeated attempts over ~3s, got ${attempts} in ${elapsed}s"
  failures=$((failures + 1))
fi

if [ "${failures}" -ne 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
