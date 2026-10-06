#!/usr/bin/env bash
#
# Pipe the idempotent migration script into the host over SSH, retrying only
# when SSH itself could not get through.
#
# GitHub runners intermittently cannot reach the VPS: the connection times out
# upstream of the host, and the same job re-run minutes later passes (#1568).
# ssh exits 255 for its own failures (connect timeout, refused, handshake) and
# with the remote command's status otherwise, so only 255 is retried: a script
# that ran and failed is reported at once, never re-run behind a retry.
# Rationale in docs/ci.md ("Migration over SSH").
#
# Environment:
#   SSH_HOST          host to connect to (user root)
#   SSH_KEY_FILE      private key file
#   SCRIPT            SQL file piped on stdin
#   RETRY_DELAYS      seconds to wait before each retry (default "15 30 60",
#                     i.e. 4 attempts)
#   CONNECT_TIMEOUT   ssh ConnectTimeout per attempt, seconds (default 30)
set -uo pipefail

: "${SSH_HOST:?SSH_HOST is required}"
: "${SSH_KEY_FILE:?SSH_KEY_FILE is required}"
: "${SCRIPT:?SCRIPT is required}"
RETRY_DELAYS="${RETRY_DELAYS-15 30 60}"
CONNECT_TIMEOUT="${CONNECT_TIMEOUT:-30}"

read -r -a delays <<< "$RETRY_DELAYS"
attempts=$(( ${#delays[@]} + 1 ))

for (( attempt = 1; attempt <= attempts; attempt++ )); do
  code=0
  ssh -i "$SSH_KEY_FILE" \
    -o StrictHostKeyChecking=yes \
    -o BatchMode=yes \
    -o ConnectTimeout="$CONNECT_TIMEOUT" \
    "root@${SSH_HOST}" < "$SCRIPT" || code=$?

  if [ "$code" -eq 0 ]; then
    exit 0
  fi
  if [ "$code" -ne 255 ]; then
    echo "::error::The migration script failed on the host (exit ${code}); not retried."
    exit "$code"
  fi
  if [ "$attempt" -lt "$attempts" ]; then
    delay="${delays[attempt - 1]}"
    echo "::warning::Attempt ${attempt}/${attempts}: SSH could not reach the host; retrying in ${delay}s."
    sleep "$delay"
  fi
done

echo "::error::SSH could not reach the host after ${attempts} attempts; nothing was applied. Re-run the failed jobs (docs/ci.md, Migration over SSH)."
exit 255
