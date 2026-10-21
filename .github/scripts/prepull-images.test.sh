#!/usr/bin/env bash
#
# Tests for prepull-images.sh. Plain shell, same reasoning as
# apply-migration.test.sh: the only collaborator is `ssh`, and a stub on the
# PATH that replays a list of exit codes (and, per call, what the forced
# command printed) is all a case needs.
#
# Every case runs with RETRY_DELAYS of zeros so the backoff costs nothing.
#
# Run: .github/scripts/prepull-images.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
prepull="${script_dir}/prepull-images.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

mkdir -p "${scratch}/bin"
cat > "${scratch}/bin/ssh" <<'STUB'
#!/usr/bin/env bash
# Records each call's remote command and stdin, prints the next output kind
# from $SSH_OUTS (pulled = the forced command's success line, - = nothing),
# then exits with the next code from $SSH_CODES.
n="$(cat "$STUB_DIR/calls" 2>/dev/null || echo 0)"
n=$((n + 1))
echo "$n" > "$STUB_DIR/calls"
printf '%s\n' "${!#}" > "$STUB_DIR/command.$n"
cat > "$STUB_DIR/stdin.$n"
read -r -a outs <<< "${SSH_OUTS:-}"
case "${outs[n - 1]:--}" in
  pulled) echo "ci-ssh-command: pulled ${IMAGE_TAG}" ;;
esac
read -r -a codes <<< "$SSH_CODES"
exit "${codes[n - 1]:-0}"
STUB
chmod +x "${scratch}/bin/ssh"

export PATH="${scratch}/bin:${PATH}"
export STUB_DIR="${scratch}"
export SSH_HOST=host.invalid SSH_KEY_FILE=/dev/null IMAGE_TAG=1.22.0
export RETRY_DELAYS="0 0 0"

# expect <label> <ssh exit codes> <want exit> <want calls> [output kinds]
expect() {
  local label="$1" codes="$2" want_exit="$3" want_calls="$4" outs="${5:-}" got_exit=0 got_calls
  rm -f "${scratch}"/calls "${scratch}"/command.* "${scratch}"/stdin.*
  SSH_CODES="$codes" SSH_OUTS="$outs" "$prepull" >/dev/null 2>&1 || got_exit=$?
  got_calls="$(cat "${scratch}/calls" 2>/dev/null || echo 0)"
  if [[ "$got_exit" = "$want_exit" ]] && [[ "$got_calls" = "$want_calls" ]]; then
    echo "ok   ${label} → exit ${got_exit}, ${got_calls} call(s)"
  else
    echo "FAIL ${label}: want exit ${want_exit} after ${want_calls} call(s), got exit ${got_exit} after ${got_calls}"
    failures=$((failures + 1))
  fi
  return 0
}

expect "the host pulls and confirms" "0" 0 1 "pulled"
expect "connection failures then a pull" "255 255 0" 0 3 "- - pulled"
expect "connection fails on every attempt" "255 255 255 255" 255 4
expect "a failed pull is not retried" "1" 1 1
expect "a refused request is not retried" "64" 64 1
expect "a failed pull after a retry stops there" "255 1" 1 2
expect "a success without the pulled line is the old forced command" "0" 1 1
RETRY_DELAYS="" expect "no delays means a single attempt" "255" 255 1

IMAGE_TAG=1.23.0 expect "a pulled line for another tag does not count" "0" 1 1 "-"

expect "every attempt asks for the same pull, with nothing on stdin" "255 0" 0 2 "- pulled"
for n in 1 2; do
  if [[ "$(cat "${scratch}/command.${n}")" != "pull 1.22.0" ]]; then
    echo "FAIL attempt ${n} sent '$(cat "${scratch}/command.${n}")' instead of 'pull 1.22.0'"
    failures=$((failures + 1))
  fi
  if [[ -s "${scratch}/stdin.${n}" ]]; then
    echo "FAIL attempt ${n} sent something on stdin"
    failures=$((failures + 1))
  fi
done

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all passed"
