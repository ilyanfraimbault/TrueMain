#!/usr/bin/env bash
#
# Fail the deploy when the env file's INGESTOR_JOB_MODE contradicts the ingestor
# lanes the compose file deploys (#1493).
#
# Usage: ENV_FILE="<env file content>" check-job-mode.sh <compose file>
#
# A compose file that declares an `ingestor-aggregate` service runs the pipeline
# as two lanes: `ingestor` on `${INGESTOR_JOB_MODE:-FetchLane}`, the aggregate
# container on `AggregateLane`. The env file wins over that default, so a
# leftover `INGESTOR_JOB_MODE=Full` turns `ingestor` back into the whole
# pipeline and every aggregate step runs twice, once per container — the 1.20.5
# deploy went green that way. `Full` (any casing) is therefore rejected next to
# an aggregate lane; the single-process freeze modes docs/riot-key-switch.md
# relies on (`AccountRefreshOnly`, `MainAnalysisOnly`, ...) and an unset
# variable stay allowed. Without an aggregate lane, any value is accepted.
#
# The env file is the deploy secret: it is read from ENV_FILE, never from argv
# or disk, and no value other than the offending mode is ever printed. Every
# assignment of the variable is checked, so a duplicate cannot hide a `Full`.
set -euo pipefail

compose_file="$1"

if [ ! -f "${compose_file}" ]; then
  echo "::error::Compose file not found: ${compose_file}"
  exit 1
fi

if ! awk '
  /^[^[:space:]#]/ { in_services = ($0 ~ /^services:[[:space:]]*$/) }
  in_services && /^  ingestor-aggregate:[[:space:]]*$/ { found = 1 }
  END { exit !found }
' "${compose_file}"; then
  echo "${compose_file} runs no aggregate lane: INGESTOR_JOB_MODE is unconstrained."
  exit 0
fi

modes="$(printf '%s\n' "${ENV_FILE:-}" | awk -v q="'" '
  {
    sub(/\r$/, "")
    sub(/^[[:space:]]*(export[[:space:]]+)?/, "")
    if ($0 !~ /^INGESTOR_JOB_MODE[[:space:]]*=/) next
    value = $0
    sub(/^INGESTOR_JOB_MODE[[:space:]]*=[[:space:]]*/, "", value)
    first = substr(value, 1, 1)
    if (first == "\"" || first == q) {
      value = substr(value, 2)
      end = index(value, first)
      if (end > 0) value = substr(value, 1, end - 1)
    } else {
      sub(/[[:space:]]+#.*$/, "", value)
    }
    sub(/^[[:space:]]+/, "", value)
    sub(/[[:space:]]+$/, "", value)
    print value
  }
')"

while IFS= read -r mode; do
  if [ "$(printf '%s' "${mode}" | tr '[:upper:]' '[:lower:]')" = "full" ]; then
    echo "::error::The env file sets INGESTOR_JOB_MODE=${mode}, but ${compose_file} already runs the aggregate lane in ingestor-aggregate: the ingestor would run the whole pipeline and every aggregate step would run twice. Remove INGESTOR_JOB_MODE from the env file (or set a single-process freeze mode, docs/riot-key-switch.md) and re-run."
    exit 1
  fi
done <<< "${modes}"

echo "INGESTOR_JOB_MODE agrees with the lanes ${compose_file} deploys."
