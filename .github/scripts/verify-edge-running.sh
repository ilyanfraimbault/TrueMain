#!/usr/bin/env bash
#
# Fail unless every container of the edge Docker Manager project is running.
#
# The edge project builds no image of ours, so verify-rollout.sh (which checks
# that our images run the released tag) has nothing to assert on it. What can
# go wrong there is Caddy not starting at all: a port still held by another
# container, a Caddyfile it refuses. `hostinger/deploy-on-vps` reports success
# before Docker Manager has done anything, so this asks the API what the
# project really runs.
#
# Environment:
#   HOSTINGER_API_KEY  token for the account owning the VM
#   VM_ID              virtual machine id
#   PROJECT_NAME       the edge project
#   TIMEOUT_SECONDS    give up after this long (default 300)
#   POLL_SECONDS       delay between attempts (default 10)
#   PROJECTS_JSON      override the API response, for tests
set -euo pipefail

: "${PROJECT_NAME:?PROJECT_NAME is required}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-300}"
POLL_SECONDS="${POLL_SECONDS:-10}"

API_ROOT="${API_ROOT:-https://developers.hostinger.com/api/vps/v1}"

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
    return 1
  fi
  printf '%s' "$body"
}

# One line per problem; silence means every container runs.
problems_of() {
  jq -r --arg name "$PROJECT_NAME" '
    (map(select(.name == $name)) | first) as $p
    | if $p == null then "project \($name) does not exist"
      elif ($p.containers // [] | length) == 0 then "project \($name) reports no container at all"
      else $p.containers[]
        | select(.state != "running" or ((.health // "") != "" and .health != "healthy"))
        | "\(.image): \(.state) \(.health // "")"
      end
  ' <<< "$1"
}

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
attempt=0

while :; do
  attempt=$(( attempt + 1 ))

  if projects="$(fetch_projects)"; then
    problems="$(problems_of "$projects")"
    if [ -z "$problems" ]; then
      echo "Attempt ${attempt}: every container of ${PROJECT_NAME} is running."
      exit 0
    fi
  else
    problems="the API did not answer"
  fi

  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "::error::${PROJECT_NAME} is not running after ${TIMEOUT_SECONDS}s"
    while IFS= read -r problem; do
      [ -n "$problem" ] && echo "::error::${problem}"
    done <<< "$problems"
    exit 1
  fi

  echo "Attempt ${attempt}: not running yet, retrying in ${POLL_SECONDS}s"
  printf '  %s\n' "$problems"
  sleep "$POLL_SECONDS"
done
