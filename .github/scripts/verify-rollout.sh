#!/usr/bin/env bash
#
# Fail unless the VPS is actually running the images the rollout just deployed.
#
# `hostinger/deploy-on-vps` reports success as soon as the API accepts the
# request, which happens before Docker Manager has done anything on the host.
# This asks the same API what the project is really running. Rationale in
# docs/ci.md ("Verifying the rollout reached the VPS").
#
# Reads the expected image names from the compose file, then polls until every
# container built from one of them runs EXPECTED_TAG and is healthy.
#
# Environment:
#   HOSTINGER_API_KEY  token for the account owning the VM
#   VM_ID              virtual machine id
#   PROJECT_NAME       Docker Manager project to inspect
#   COMPOSE_FILE       compose file naming the images to check
#   EXPECTED_TAG       tag every one of those images must run
#   TIMEOUT_SECONDS    give up after this long (default 600)
#   POLL_SECONDS       delay between attempts (default 15)
#   PROJECTS_JSON      override the API response, for tests
set -euo pipefail

: "${COMPOSE_FILE:?COMPOSE_FILE is required}"
: "${EXPECTED_TAG:?EXPECTED_TAG is required}"
: "${PROJECT_NAME:?PROJECT_NAME is required}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-600}"
POLL_SECONDS="${POLL_SECONDS:-15}"

API_ROOT="${API_ROOT:-https://developers.hostinger.com/api/vps/v1}"

# Echoes the raw project list. A transport or HTTP failure is fatal rather than
# an empty listing: "the API did not answer" and "the project runs nothing" are
# different facts, and reading the first as the second is how a check ends up
# reporting a deploy that never happened as merely slow.
fetch_projects() {
  if [ -n "${PROJECTS_JSON+set}" ]; then
    printf '%s' "$PROJECTS_JSON"
    return
  fi

  local body status
  body="$(curl -sS --max-time 30 -w '\n%{http_code}' \
    -H "Authorization: Bearer ${HOSTINGER_API_KEY:?HOSTINGER_API_KEY is required}" \
    -H 'Accept: application/json' \
    "${API_ROOT}/virtual-machines/${VM_ID:?VM_ID is required}/docker")" || return 1

  status="$(printf '%s' "$body" | tail -n1)"
  body="$(printf '%s' "$body" | sed '$d')"

  if [ "$status" != "200" ]; then
    echo "::error::the Hostinger API answered ${status} when asked what ${PROJECT_NAME} is running" >&2
    printf '%s\n' "$body" >&2
    return 1
  fi
  printf '%s' "$body"
}

# "image<TAB>state<TAB>health" for each container of PROJECT_NAME.
containers_of_project() {
  jq -r --arg project "$PROJECT_NAME" '
    (map(select(.name == $project)) | first) as $p
    | if $p == null then empty
      else $p.containers[]? | [.image, .state, .health] | @tsv
      end
  ' <<< "$1"
}

images=()
while IFS= read -r image; do
  [ -n "$image" ] && images+=("$image")
done < <(grep -oE 'ghcr\.io/[^:[:space:]]+' "$COMPOSE_FILE" | sort -u)

if [ "${#images[@]}" -eq 0 ]; then
  echo "::error::no ghcr.io image found in ${COMPOSE_FILE}; there is nothing to verify, which would make this check vacuous"
  exit 1
fi

echo "Expecting these images at ${EXPECTED_TAG} in project ${PROJECT_NAME}:"
printf '  %s\n' "${images[@]}"

# One pass over the container list. Echoes a line per problem; silence means
# the environment is running what was deployed.
inspect() {
  local listing="$1" image lines ref state health
  for image in "${images[@]}"; do
    lines="$(grep -F "${image}:" <<< "$listing" || true)"
    if [ -z "$lines" ]; then
      echo "${image}: no container running"
      continue
    fi
    while IFS=$'\t' read -r ref state health; do
      [ -n "$ref" ] || continue
      if [ "$ref" != "${image}:${EXPECTED_TAG}" ]; then
        echo "${ref}: still running, expected ${image}:${EXPECTED_TAG}"
      elif [ "$state" != "running" ]; then
        echo "${ref}: ${state}"
      elif [ -n "$health" ] && [ "$health" != "healthy" ]; then
        echo "${ref}: ${health}"
      fi
    done <<< "$lines"
  done
  # Problems go to stdout; the status must stay 0 or `set -e` would turn the
  # assignment below into an exit.
  return 0
}

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
attempt=0

while :; do
  attempt=$(( attempt + 1 ))

  if ! projects="$(fetch_projects)"; then
    if [ "$(date +%s)" -ge "$deadline" ]; then
      echo "::error::could not read the state of ${PROJECT_NAME} from the Hostinger API"
      exit 1
    fi
    echo "Attempt ${attempt}: the API did not answer, retrying in ${POLL_SECONDS}s"
    sleep "$POLL_SECONDS"
    continue
  fi

  listing="$(containers_of_project "$projects")"
  if [ -z "$listing" ]; then
    problems="project ${PROJECT_NAME} reports no container at all"
  else
    problems="$(inspect "$listing")"
  fi

  if [ -z "$problems" ]; then
    echo "Attempt ${attempt}: every service runs ${EXPECTED_TAG} and is healthy."
    printf '%s\n' "$listing"
    exit 0
  fi

  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "::error::${PROJECT_NAME} is not running ${EXPECTED_TAG} after ${TIMEOUT_SECONDS}s; the rollout did not take effect"
    while IFS= read -r problem; do
      [ -n "$problem" ] && echo "::error::${problem}"
    done <<< "$problems"
    echo "Containers currently reported:"
    printf '%s\n' "$listing"
    exit 1
  fi

  echo "Attempt ${attempt}: not ready yet, retrying in ${POLL_SECONDS}s"
  printf '  %s\n' "$problems"
  sleep "$POLL_SECONDS"
done
