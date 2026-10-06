#!/usr/bin/env bash
#
# Pipe the idempotent migration script into the host over SSH, retrying only
# when SSH itself could not get through or when the script lost a lock.
#
# GitHub runners intermittently cannot reach the VPS: the connection times out
# upstream of the host, and the same job re-run minutes later passes (#1568).
# ssh exits 255 for its own failures (connect timeout, refused, handshake) and
# with the remote command's status otherwise, so 255 is retried on its own
# schedule.
#
# The script opens with `SET lock_timeout` (#1631): a DDL that cannot get its
# lock within a few seconds fails instead of queueing traffic behind it. The
# script runs in one transaction, so such a failure rolls everything back and
# a later attempt is safe; it is retried on a second, separate schedule. Any
# other failure of the script is reported at once, never re-run behind a retry.
# Rationale in docs/ci.md ("Migration over SSH").
#
# Environment:
#   SSH_HOST            host to connect to (user root)
#   SSH_KEY_FILE        private key file
#   SCRIPT              SQL file piped on stdin
#   RETRY_DELAYS        seconds to wait before each retry after a connection
#                       failure (default "15 30 60", i.e. 4 attempts)
#   LOCK_RETRY_DELAYS   seconds to wait before each retry after a lock
#                       timeout (default "30 30", i.e. 3 attempts)
#   CONNECT_TIMEOUT     ssh ConnectTimeout per attempt, seconds (default 30)
set -uo pipefail

: "${SSH_HOST:?SSH_HOST is required}"
: "${SSH_KEY_FILE:?SSH_KEY_FILE is required}"
: "${SCRIPT:?SCRIPT is required}"
RETRY_DELAYS="${RETRY_DELAYS-15 30 60}"
LOCK_RETRY_DELAYS="${LOCK_RETRY_DELAYS-30 30}"
CONNECT_TIMEOUT="${CONNECT_TIMEOUT:-30}"

read -r -a delays <<< "$RETRY_DELAYS"
read -r -a lock_delays <<< "$LOCK_RETRY_DELAYS"
attempts=$(( ${#delays[@]} + 1 ))
lock_attempts=$(( ${#lock_delays[@]} + 1 ))

stderr_log="$(mktemp)"
trap 'rm -f "$stderr_log"' EXIT

connection_failures=0
lock_failures=0
attempt=0

while true; do
  attempt=$(( attempt + 1 ))
  code=0
  ssh -i "$SSH_KEY_FILE" \
    -o StrictHostKeyChecking=yes \
    -o BatchMode=yes \
    -o ConnectTimeout="$CONNECT_TIMEOUT" \
    "root@${SSH_HOST}" < "$SCRIPT" 2> "$stderr_log" || code=$?
  cat "$stderr_log" >&2

  if [ "$code" -eq 0 ]; then
    exit 0
  fi

  if [ "$code" -eq 255 ]; then
    connection_failures=$(( connection_failures + 1 ))
    if [ "$connection_failures" -ge "$attempts" ]; then
      echo "::error::SSH could not reach the host after ${attempts} attempts; nothing was applied. Re-run the failed jobs (docs/ci.md, Migration over SSH)."
      exit 255
    fi
    delay="${delays[connection_failures - 1]}"
    echo "::warning::Attempt ${attempt}: SSH could not reach the host (${connection_failures}/${attempts}); retrying in ${delay}s."
    sleep "$delay"
    continue
  fi

  if grep -qE 'lock timeout|55P03' "$stderr_log"; then
    lock_failures=$(( lock_failures + 1 ))
    if [ "$lock_failures" -ge "$lock_attempts" ]; then
      echo "::error::The migration script lost its lock ${lock_attempts} times and was rolled back each time; nothing was applied. Find the blocker, then re-run the failed jobs (docs/production-migrations.md)."
      exit "$code"
    fi
    delay="${lock_delays[lock_failures - 1]}"
    echo "::warning::Attempt ${attempt}: a migration statement hit its lock timeout and the script was rolled back (${lock_failures}/${lock_attempts}); retrying in ${delay}s."
    sleep "$delay"
    continue
  fi

  echo "::error::The migration script failed on the host (exit ${code}); not retried."
  exit "$code"
done
