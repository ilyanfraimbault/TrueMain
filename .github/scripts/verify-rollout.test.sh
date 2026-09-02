#!/usr/bin/env bash
#
# Tests for verify-rollout.sh. Plain shell on purpose, same reasoning as
# resolve-preprod-version.test.sh: the unit under test is a shell pipeline over
# `docker ps` output, and DOCKER_PS already exists to feed it a listing, so
# there is nothing to mock and nothing a framework would add.
#
# Every case runs with TIMEOUT_SECONDS=0 so a failing state is reported on the
# first pass instead of being polled for ten minutes; the polling itself gets
# its own case.
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

up() { printf '%s\tUp 2 minutes (healthy)\n' "$1"; }

# The listing a fully rolled stack produces: every image at $1, both ingestor
# lanes, plus an unrelated container that must never be judged.
rolled() {
  local tag="$1"
  up "ghcr.io/owner/truemain-api:${tag}"
  up "ghcr.io/owner/truemain-web:${tag}"
  up "ghcr.io/owner/truemain-ingestor:${tag}"
  up "ghcr.io/owner/truemain-ingestor:${tag}"
  up "postgres:17.2"
}

run_verify() {
  COMPOSE_FILE="${compose}" EXPECTED_TAG="${EXPECTED_TAG:-2.0.0}" \
    TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-0}" POLL_SECONDS="${POLL_SECONDS:-1}" \
    DOCKER_PS="$1" "${verify}" 2>&1
}

expect_pass() {
  local label="$1" listing="$2" out
  if out="$(run_verify "${listing}")"; then
    echo "ok   ${label}"
  else
    echo "FAIL ${label}: expected success, got:"
    printf '%s\n' "${out}" | sed 's/^/       /'
    failures=$((failures + 1))
  fi
}

# Fails, and says so by naming $3 in the error output — a check that fails for
# the wrong reason is not the check we want.
expect_fail() {
  local label="$1" listing="$2" needle="$3" out
  if out="$(run_verify "${listing}")"; then
    echo "FAIL ${label}: expected a non-zero exit"
    failures=$((failures + 1))
  elif printf '%s' "${out}" | grep -qF "${needle}"; then
    echo "ok   ${label}"
  else
    echo "FAIL ${label}: no error mentioning '${needle}', got:"
    printf '%s\n' "${out}" | sed 's/^/       /'
    failures=$((failures + 1))
  fi
}

expect_pass "a fully rolled stack passes" "$(rolled 2.0.0)"

# The incident this check exists for: the deploy reported success, the API had
# accepted it, and every container was still on the previous release.
expect_fail "a stack still on the old tag fails" \
  "$(rolled 1.19.4)" "still running, expected ghcr.io/owner/truemain-api:2.0.0"

expect_fail "one service left behind fails" \
  "$(rolled 2.0.0 | sed '2s/2\.0\.0/1.19.4/')" \
  "ghcr.io/owner/truemain-web:1.19.4"

# Both ingestor lanes share one image, so rolling only one of them is a
# partial deploy the image name alone cannot reveal.
expect_fail "one of two lanes on the same image left behind fails" \
  "$(rolled 2.0.0 | sed '4s/2\.0\.0/1.19.4/')" \
  "ghcr.io/owner/truemain-ingestor:1.19.4"

expect_fail "a missing container fails" \
  "$(rolled 2.0.0 | grep -v truemain-web)" \
  "ghcr.io/owner/truemain-web: no container running"

expect_fail "an unhealthy container fails" \
  "$(rolled 2.0.0 | sed '1s/(healthy)/(unhealthy)/')" "(unhealthy)"

expect_fail "a container still starting fails" \
  "$(rolled 2.0.0 | sed '1s/Up 2 minutes (healthy)/Up 3 seconds (health: starting)/')" \
  "(health: starting)"

expect_fail "an exited container fails" \
  "$(rolled 2.0.0 | sed '1s/Up 2 minutes (healthy)/Exited (1) 4 seconds ago/')" \
  "Exited (1)"

expect_fail "an empty listing fails" "" "no container running"

# A compose file naming no image of ours would make every assertion vacuous, so
# it is an error rather than a pass.
cat > "${scratch}/no-images.yaml" <<'YAML'
services:
  postgres:
    image: postgres:17.2
YAML
out="$(COMPOSE_FILE="${scratch}/no-images.yaml" EXPECTED_TAG=2.0.0 TIMEOUT_SECONDS=0 \
  DOCKER_PS="x" "${verify}" 2>&1)"
if [ $? -eq 0 ]; then
  echo "FAIL a compose file with no image of ours must not pass vacuously"
  failures=$((failures + 1))
elif printf '%s' "${out}" | grep -qF "no ghcr.io image found"; then
  echo "ok   a compose file with no image of ours fails rather than passing vacuously"
else
  echo "FAIL vacuous-compose case failed for the wrong reason:"
  printf '%s\n' "${out}" | sed 's/^/       /'
  failures=$((failures + 1))
fi

# The wait is what makes a slow but successful roll pass, so it has to retry
# rather than judge the first listing.
started="$(date +%s)"
attempts="$(TIMEOUT_SECONDS=3 POLL_SECONDS=1 run_verify "$(rolled 1.19.4)" | grep -c '^Attempt')"
elapsed=$(( $(date +%s) - started ))
if [ "${attempts}" -ge 2 ] && [ "${elapsed}" -ge 2 ]; then
  echo "ok   it polls until the timeout instead of judging one listing (${attempts} attempts in ${elapsed}s)"
else
  echo "FAIL expected repeated attempts over ~3s, got ${attempts} in ${elapsed}s"
  failures=$((failures + 1))
fi

if [ "${failures}" -ne 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
