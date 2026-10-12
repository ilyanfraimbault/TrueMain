#!/usr/bin/env bash
#
# Tests for check-job-mode.sh, against the real compose files (so the check
# breaks here, not on a deploy, if a lane is renamed) and a fixture without an
# aggregate lane. The env file goes in through ENV_FILE, as on the deploy.
#
# Run: .github/scripts/check-job-mode.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"
check="${script_dir}/check-job-mode.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

single_lane="${scratch}/compose.single.yaml"
cat > "${single_lane}" <<'YAML'
services:
  ingestor:
    environment:
      Job__Mode: ${INGESTOR_JOB_MODE:-Full}
volumes:
  ingestor-aggregate:
YAML

expect() {
  local label="$1" compose="$2" env_file="$3" want="$4" got
  if ENV_FILE="${env_file}" "${check}" "${compose}" > "${scratch}/out" 2>&1; then
    got=pass
  else
    got=fail
  fi
  if [ "${got}" = "${want}" ]; then
    echo "ok   ${label} → ${got}"
  else
    echo "FAIL ${label}: want ${want}, got ${got}"
    sed 's/^/     /' "${scratch}/out"
    failures=$((failures + 1))
  fi
}

for compose in compose.prod.yaml compose.preprod.yaml; do
  file="${repo_root}/${compose}"
  expect "${compose}: variable unset" "${file}" $'POSTGRES_DB=truemain\nIMAGE_TAG=x' pass
  expect "${compose}: empty env file" "${file}" "" pass
  expect "${compose}: the fetch lane" "${file}" "INGESTOR_JOB_MODE=FetchLane" pass
  expect "${compose}: a freeze mode" "${file}" "INGESTOR_JOB_MODE=AccountRefreshOnly" pass
  expect "${compose}: another freeze mode" "${file}" "INGESTOR_JOB_MODE=MainAnalysisOnly" pass
  expect "${compose}: Full" "${file}" "INGESTOR_JOB_MODE=Full" fail
  expect "${compose}: full, the 1.20.5 casing" "${file}" $'A=1\nINGESTOR_JOB_MODE=full\nB=2' fail
  expect "${compose}: FULL" "${file}" "INGESTOR_JOB_MODE=FULL" fail
  expect "${compose}: double-quoted" "${file}" 'INGESTOR_JOB_MODE="Full"' fail
  expect "${compose}: single-quoted" "${file}" "INGESTOR_JOB_MODE='full'" fail
  expect "${compose}: exported, spaced, CRLF" "${file}" $'  export INGESTOR_JOB_MODE = Full \r' fail
  expect "${compose}: trailing comment" "${file}" "INGESTOR_JOB_MODE=Full # freeze" fail
  expect "${compose}: a duplicate cannot hide Full" "${file}" $'INGESTOR_JOB_MODE=Full\nINGESTOR_JOB_MODE=FetchLane' fail
  expect "${compose}: commented out" "${file}" "# INGESTOR_JOB_MODE=Full" pass
  expect "${compose}: another variable named alike" "${file}" "INGESTOR_JOB_MODE_OLD=Full" pass
  expect "${compose}: the aggregate container's own variable" "${file}" "INGESTOR_AGGREGATE_JOB_MODE=Full" pass
  expect "${compose}: a mode that merely contains full" "${file}" "INGESTOR_JOB_MODE=FullPipeline" pass
done

expect "no aggregate lane: Full is the default" "${single_lane}" "INGESTOR_JOB_MODE=Full" pass
expect "compose.yaml (local stack) is not constrained" "${repo_root}/compose.yaml" "INGESTOR_JOB_MODE=Full" pass
expect "a missing compose file fails" "${scratch}/missing.yaml" "" fail

if ! ENV_FILE=$'SECRET=hunter2\nINGESTOR_JOB_MODE=Full' "${check}" "${repo_root}/compose.prod.yaml" 2>&1 | grep -q hunter2; then
  echo "ok   the env file's other values are never printed"
else
  echo "FAIL the env file's other values leaked into the output"
  failures=$((failures + 1))
fi

if [ "${failures}" -gt 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
