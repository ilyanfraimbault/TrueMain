#!/usr/bin/env bash
#
# Ask the host to pull IMAGE_TAG's application images before Docker Manager
# recreates anything (#1089), with the same bounded retry as the migration.
#
# Docker Manager stops and removes every container before it pulls, so a pull
# that fails there leaves the whole stack down. Pulling first over the CI key
# fails the run while the old containers still serve. The key is pinned to
# .github/scripts/vps/ci-ssh-command.sh, which only accepts "pull <tag>" and
# the migration. Rationale in docs/ci.md ("Pre-pull before recreate").
#
# Only ssh's own failures (exit 255) are retried, on the migration's schedule.
# A pull that ran and failed, or a refused request, is reported at once. A
# success without the forced command's "pulled <tag>" line means the host
# still runs the previous forced command, which ignored the request.
#
# Environment:
#   SSH_HOST         host to connect to (user root)
#   SSH_KEY_FILE     private key file
#   IMAGE_TAG        tag to pull
#   RETRY_DELAYS     seconds to wait before each retry after a connection
#                    failure (default "15 30 60", i.e. 4 attempts)
#   CONNECT_TIMEOUT  ssh ConnectTimeout per attempt, seconds (default 30)
set -uo pipefail

: "${SSH_HOST:?SSH_HOST is required}"
: "${SSH_KEY_FILE:?SSH_KEY_FILE is required}"
: "${IMAGE_TAG:?IMAGE_TAG is required}"
RETRY_DELAYS="${RETRY_DELAYS-15 30 60}"
CONNECT_TIMEOUT="${CONNECT_TIMEOUT:-30}"

read -r -a delays <<< "$RETRY_DELAYS"
attempts=$(( ${#delays[@]} + 1 ))

stdout_log="$(mktemp)"
trap 'rm -f "$stdout_log"' EXIT

attempt=0
while true; do
  attempt=$(( attempt + 1 ))
  code=0
  ssh -i "$SSH_KEY_FILE" \
    -o StrictHostKeyChecking=yes \
    -o BatchMode=yes \
    -o ConnectTimeout="$CONNECT_TIMEOUT" \
    "root@${SSH_HOST}" "pull ${IMAGE_TAG}" < /dev/null > "$stdout_log" || code=$?
  cat "$stdout_log"

  if [ "$code" -eq 0 ]; then
    if grep -qxF "ci-ssh-command: pulled ${IMAGE_TAG}" "$stdout_log"; then
      exit 0
    fi
    echo "::error::The host did not confirm the pull: its CI key still runs the previous forced command. Install .github/scripts/vps/ci-ssh-command.sh there, or turn the pre-pull off (docs/ci.md, Pre-pull before recreate)."
    exit 1
  fi

  if [ "$code" -eq 255 ]; then
    if [ "$attempt" -ge "$attempts" ]; then
      echo "::error::SSH could not reach the host after ${attempts} attempts; nothing was redeployed. Re-run the failed jobs (docs/ci.md, Pre-pull before recreate)."
      exit 255
    fi
    delay="${delays[attempt - 1]}"
    echo "::warning::Attempt ${attempt}: SSH could not reach the host (${attempt}/${attempts}); retrying in ${delay}s."
    sleep "$delay"
    continue
  fi

  if [ "$code" -eq 64 ]; then
    echo "::error::The host refused 'pull ${IMAGE_TAG}': the tag is not a git sha or a semver; nothing was redeployed."
    exit "$code"
  fi

  echo "::error::Pulling ${IMAGE_TAG} failed on the host (exit ${code}); not retried, nothing was redeployed and the running stack is untouched."
  exit "$code"
done
