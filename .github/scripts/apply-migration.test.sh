#!/usr/bin/env bash
#
# Tests for apply-migration.sh. Plain shell on purpose, same reasoning as
# resolve-preprod-version.test.sh: the only collaborator is `ssh`, and a stub
# on the PATH that replays a list of exit codes (and, per call, what psql
# would have printed on stderr) is all a case needs.
#
# Every case runs with RETRY_DELAYS and LOCK_RETRY_DELAYS of zeros so the
# backoff costs nothing.
#
# Run: .github/scripts/apply-migration.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
apply="${script_dir}/apply-migration.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

mkdir -p "${scratch}/bin"
cat > "${scratch}/bin/ssh" <<'STUB'
#!/usr/bin/env bash
# Records each call's stdin, prints the next stderr kind from $SSH_ERRS
# (lock = psql's lock-timeout error, sql = any other SQL error, - = nothing),
# then exits with the next code from $SSH_CODES.
n="$(cat "$STUB_DIR/calls" 2>/dev/null || echo 0)"
n=$((n + 1))
echo "$n" > "$STUB_DIR/calls"
cat > "$STUB_DIR/stdin.$n"
read -r -a errs <<< "${SSH_ERRS:-}"
case "${errs[n - 1]:--}" in
  lock) echo 'psql:<stdin>:42: ERROR:  canceling statement due to lock timeout' >&2 ;;
  sql) echo 'psql:<stdin>:42: ERROR:  relation "matches" does not exist' >&2 ;;
esac
read -r -a codes <<< "$SSH_CODES"
exit "${codes[n - 1]:-0}"
STUB
chmod +x "${scratch}/bin/ssh"

printf 'SELECT 1;\n' > "${scratch}/migration.sql"

export PATH="${scratch}/bin:${PATH}"
export STUB_DIR="${scratch}"
export SSH_HOST=host.invalid SSH_KEY_FILE=/dev/null SCRIPT="${scratch}/migration.sql"
export RETRY_DELAYS="0 0 0" LOCK_RETRY_DELAYS="0 0"

# expect <label> <ssh exit codes> <want exit> <want calls> [stderr kinds]
expect() {
  local label="$1" codes="$2" want_exit="$3" want_calls="$4" errs="${5:-}" got_exit=0 got_calls
  rm -f "${scratch}"/calls "${scratch}"/stdin.*
  SSH_CODES="$codes" SSH_ERRS="$errs" "$apply" >/dev/null 2>&1 || got_exit=$?
  got_calls="$(cat "${scratch}/calls" 2>/dev/null || echo 0)"
  if [ "$got_exit" = "$want_exit" ] && [ "$got_calls" = "$want_calls" ]; then
    echo "ok   ${label} → exit ${got_exit}, ${got_calls} call(s)"
  else
    echo "FAIL ${label}: want exit ${want_exit} after ${want_calls} call(s), got exit ${got_exit} after ${got_calls}"
    failures=$((failures + 1))
  fi
}

expect "first attempt succeeds" "0" 0 1
expect "connection failures then success" "255 255 0" 0 3
expect "connection fails on every attempt" "255 255 255 255" 255 4
expect "a failing script is not retried" "3" 3 1
expect "a failing script after a retry stops there" "255 1" 1 2

expect "every attempt is fed the whole script" "255 255 0" 0 3
for n in 1 2 3; do
  if ! cmp -s "${scratch}/stdin.${n}" "${scratch}/migration.sql"; then
    echo "FAIL attempt ${n} did not receive the script on stdin"
    failures=$((failures + 1))
  fi
done

RETRY_DELAYS="" expect "no delays means a single attempt" "255" 255 1

expect "a lock timeout is retried, then succeeds" "3 0" 0 2 "lock -"
expect "lock timeouts on every attempt stop after three" "3 3 3" 3 3 "lock lock lock"
expect "another SQL error is not retried" "3" 3 1 "sql"
expect "a lock timeout then another SQL error stops there" "3 3" 3 2 "lock sql"
expect "the two schedules are counted apart" "255 3 255 3 0" 0 5 "- lock - lock -"
expect "connection failures keep their budget after lock retries" "3 3 255 255 255 255" 255 6 "lock lock - - - -"
LOCK_RETRY_DELAYS="" expect "no lock delays means no lock retry" "3" 3 1 "lock"

if [ "$failures" -gt 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
