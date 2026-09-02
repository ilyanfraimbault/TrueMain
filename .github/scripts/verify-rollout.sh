#!/usr/bin/env bash
#
# Fail unless the VPS is actually running the images the rollout just deployed.
#
# Reads the expected image names from the compose file, then polls the host over
# SSH until every container built from one of them runs EXPECTED_TAG and reports
# healthy. Rationale in docs/ci.md ("Verifying the rollout reached the VPS").
#
# Environment:
#   SSH_HOST          host to reach as root
#   SSH_IDENTITY      private key file (default ~/.ssh/deploy_ed25519)
#   COMPOSE_FILE      compose file naming the images to check
#   EXPECTED_TAG      tag every one of those images must run
#   TIMEOUT_SECONDS   give up after this long (default 600)
#   POLL_SECONDS      delay between attempts (default 15)
#   DOCKER_PS         override the container listing, for tests
set -euo pipefail

: "${COMPOSE_FILE:?COMPOSE_FILE is required}"
: "${EXPECTED_TAG:?EXPECTED_TAG is required}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-600}"
POLL_SECONDS="${POLL_SECONDS:-15}"
SSH_IDENTITY="${SSH_IDENTITY:-$HOME/.ssh/deploy_ed25519}"

list_containers() {
  # Set-but-empty is a listing of no containers, which is a state to report,
  # not a missing override to fall through on.
  if [ -n "${DOCKER_PS+set}" ]; then
    printf '%s\n' "$DOCKER_PS"
    return
  fi
  ssh -i "$SSH_IDENTITY" -o StrictHostKeyChecking=yes -o ConnectTimeout=20 \
    "root@${SSH_HOST:?SSH_HOST is required}" \
    "docker ps --format '{{.Image}}\t{{.Status}}'"
}

images=()
while IFS= read -r image; do
  [ -n "$image" ] && images+=("$image")
done < <(grep -oE 'ghcr\.io/[^:[:space:]]+' "$COMPOSE_FILE" | sort -u)

if [ "${#images[@]}" -eq 0 ]; then
  echo "::error::no ghcr.io image found in ${COMPOSE_FILE}; there is nothing to verify, which would make this check vacuous"
  exit 1
fi

echo "Expecting these images at ${EXPECTED_TAG}:"
printf '  %s\n' "${images[@]}"

# One pass over the listing. Echoes a line per problem; silence means deployed.
inspect() {
  local listing="$1" image lines ref status
  for image in "${images[@]}"; do
    lines="$(printf '%s\n' "$listing" | grep -F "${image}:" || true)"
    if [ -z "$lines" ]; then
      echo "${image}: no container running"
      continue
    fi
    while IFS=$'\t' read -r ref status; do
      [ -n "$ref" ] || continue
      if [ "$ref" != "${image}:${EXPECTED_TAG}" ]; then
        echo "${ref}: still running, expected ${image}:${EXPECTED_TAG}"
      elif [[ "$status" != Up* ]]; then
        echo "${ref}: ${status}"
      elif [[ "$status" == *"(unhealthy)"* || "$status" == *"(health: starting)"* ]]; then
        echo "${ref}: ${status}"
      fi
    done <<< "$lines"
  done
  # Problems are reported on stdout; the status must stay 0 or `set -e` would
  # turn the assignment below into an exit.
  return 0
}

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
attempt=0

while :; do
  attempt=$(( attempt + 1 ))
  listing="$(list_containers || true)"
  problems="$(inspect "$listing")"

  if [ -z "$problems" ]; then
    echo "Attempt ${attempt}: every service runs ${EXPECTED_TAG} and is healthy."
    printf '%s\n' "$listing"
    exit 0
  fi

  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "::error::the VPS is not running ${EXPECTED_TAG} after ${TIMEOUT_SECONDS}s; the rollout did not take effect"
    while IFS= read -r problem; do
      [ -n "$problem" ] && echo "::error::${problem}"
    done <<< "$problems"
    echo "Containers currently running:"
    printf '%s\n' "$listing"
    exit 1
  fi

  echo "Attempt ${attempt}: not ready yet, retrying in ${POLL_SECONDS}s"
  printf '  %s\n' "$problems"
  sleep "$POLL_SECONDS"
done
