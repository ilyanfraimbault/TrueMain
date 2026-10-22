#!/usr/bin/env bash
#
# Decide whether the edge Docker Manager project needs a redeploy.
#
# The edge (Caddy and the maintenance page) is its own project so that an app
# rollout, which recreates every container of its project, never takes it down.
# Redeploying the edge recreates Caddy too, so it only happens when it changes
# something: the project is missing, the compose file Docker Manager holds is
# not the repository's, or a container of it is not running. Rationale in
# docs/ci.md ("The edge is its own project").
#
# Environment:
#   HOSTINGER_API_KEY  token for the account owning the VM
#   VM_ID              virtual machine id
#   PROJECT_NAME       the edge project
#   COMPOSE_FILE       the compose file the project should hold
#   PROJECTS_JSON      override the project list response, for tests
#   CONTENT_JSON       override the project content response, for tests
#
# Prints the reason, and writes deploy=true|false to $GITHUB_OUTPUT when set.
set -euo pipefail

: "${COMPOSE_FILE:?COMPOSE_FILE is required}"
: "${PROJECT_NAME:?PROJECT_NAME is required}"

API_ROOT="${API_ROOT:-https://developers.hostinger.com/api/vps/v1}"

# GET a path of the Docker Manager API. A transport or HTTP failure is fatal:
# "the API did not answer" must never read as "the edge is missing", which
# would recreate Caddy for nothing.
api_get() {
  local body status
  body="$(curl -sS --max-time 30 -w '\n%{http_code}' \
    -H "Authorization: Bearer ${HOSTINGER_API_KEY:?HOSTINGER_API_KEY is required}" \
    -H 'Accept: application/json' \
    "${API_ROOT}/virtual-machines/${VM_ID:?VM_ID is required}/docker$1")" || return 1

  status="$(printf '%s' "$body" | tail -n1)"
  body="$(printf '%s' "$body" | sed '$d')"
  if [ "$status" != "200" ]; then
    echo "::error::the Hostinger API answered ${status} for docker$1" >&2
    printf '%s\n' "$body" >&2
    return 1
  fi
  printf '%s' "$body"
}

decide() {
  echo "$2"
  if [ -n "${GITHUB_OUTPUT:-}" ]; then
    echo "deploy=$1" >> "$GITHUB_OUTPUT"
    echo "Edge ${PROJECT_NAME}: $2" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
  fi
  exit 0
}

if [ -n "${PROJECTS_JSON+set}" ]; then
  projects="$PROJECTS_JSON"
else
  projects="$(api_get "")"
fi

project="$(jq -c --arg name "$PROJECT_NAME" 'map(select(.name == $name)) | first // empty' <<< "$projects")"
if [ -z "$project" ]; then
  decide true "Docker Manager has no ${PROJECT_NAME} project yet: deploying it."
fi

stopped="$(jq -r '
  [.containers[]? | select(.state != "running" or ((.health // "") != "" and .health != "healthy"))
   | "\(.image) \(.state) \(.health // "")"] | join(", ")
' <<< "$project")"
if [ "$(jq '.containers // [] | length' <<< "$project")" = "0" ]; then
  decide true "${PROJECT_NAME} runs no container: redeploying it."
fi
if [ -n "$stopped" ]; then
  decide true "${PROJECT_NAME} is not fully running (${stopped}): redeploying it."
fi

if [ -n "${CONTENT_JSON+set}" ]; then
  content="$CONTENT_JSON"
else
  content="$(api_get "/${PROJECT_NAME}")"
fi

# Trailing newlines are the only difference a round trip through the API may
# add; anything else is a real change.
deployed="$(jq -r '.content // ""' <<< "$content")"
wanted="$(cat "$COMPOSE_FILE")"
if [ "$deployed" != "$wanted" ]; then
  changed="$(diff <(printf '%s\n' "$deployed") <(printf '%s\n' "$wanted") | grep -c '^[<>]' || true)"
  decide true "${PROJECT_NAME} holds a different ${COMPOSE_FILE} (${changed} lines differ): redeploying it."
fi

decide false "${PROJECT_NAME} already runs this ${COMPOSE_FILE}: leaving the edge untouched."
