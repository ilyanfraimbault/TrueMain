#!/usr/bin/env bash
#
# Tests for check-file-size.sh. Plain shell, same reasoning as the other script tests here: the unit
# under test walks a directory tree and diffs it against a baseline file, so the only fixture it needs
# is a scratch tree — FILE_SIZE_ROOT and FILE_SIZE_BASELINE exist to point it at one.
#
# Run: .github/scripts/check-file-size.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
check="${script_dir}/check-file-size.sh"

failures=0
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

# A file of exactly $2 lines at $1, inside the scratch tree.
make_file() {
  mkdir -p "${scratch}/$(dirname "$1")"
  awk -v n="$2" 'BEGIN { for (i = 1; i <= n; i++) print "line" i }' > "${scratch}/$1"
}

baseline() {
  mkdir -p "${scratch}/.github"
  {
    echo "# path<TAB>lines<TAB>limit"
    [ "$#" -gt 0 ] && printf '%s\n' "$@"
  } > "${scratch}/.github/file-size-baseline.txt"
}

run_check() {
  FILE_SIZE_ROOT="${scratch}" "${check}" "$@" 2>&1
}

expect() {
  local label="$1" expected_status="$2" expected_text="$3" output status
  output="$(run_check)"
  status=$?
  if [ "${status}" -ne "${expected_status}" ]; then
    echo "FAIL ${label}: expected exit ${expected_status}, got ${status}"
    echo "${output}" | sed 's/^/     /'
    failures=$((failures + 1))
    return
  fi
  if ! grep -qF "${expected_text}" <<< "${output}"; then
    echo "FAIL ${label}: output does not mention '${expected_text}'"
    echo "${output}" | sed 's/^/     /'
    failures=$((failures + 1))
    return
  fi
  echo "ok   ${label}"
}

# A tree whose files all sit under their limit: 500 for .cs, 400 for .vue, 300 for .ts.
make_file "backend/Api/Small.cs" 100
make_file "web/app/pages/small.vue" 100
make_file "web/app/utils/small.ts" 100
baseline
expect "a tree under the limits passes" 0 "File sizes OK"

# The limits themselves: at the limit is fine, one line over is not.
make_file "backend/Api/Edge.cs" 500
expect "a file exactly at its limit is not an offender" 0 "File sizes OK"

make_file "backend/Api/Edge.cs" 501
expect "a file one line over its limit fails" 1 "New file over the size limit"

# Recording it in the baseline is what makes it tolerated.
baseline "backend/Api/Edge.cs	501	500"
expect "a baselined offender passes" 0 "File sizes OK"

make_file "backend/Api/Edge.cs" 600
expect "a baselined offender that grows fails" 1 "Already-oversized file that grew"

# Re-baseline at the size it grew to, so the next step is a genuine shrink.
baseline "backend/Api/Edge.cs	600	500"
make_file "backend/Api/Edge.cs" 520
expect "a baselined offender that shrinks passes and reports progress" 0 "Progress"

make_file "backend/Api/Edge.cs" 400
expect "a baselined offender back under its limit reports progress" 0 "no longer over the limit"

rm "${scratch}/backend/Api/Edge.cs"
expect "a deleted baselined offender reports progress" 0 "no longer over the limit"

# Per-extension limits: 350 is over the .ts limit but under the .vue one.
baseline
make_file "web/app/utils/big.ts" 350
expect "the .ts limit is 300" 1 "web/app/utils/big.ts"

rm "${scratch}/web/app/utils/big.ts"
make_file "web/app/pages/big.vue" 350
expect "the .vue limit is 400, so 350 lines pass" 0 "File sizes OK"

# Exclusions.
make_file "backend/tests/TrueMain.IntegrationTests/Huge.cs" 2000
expect "backend tests are not covered" 0 "File sizes OK"

make_file "backend/Data/Migrations/20250101_Huge.cs" 2000
expect "migrations are not covered" 0 "File sizes OK"

make_file "backend/Data/CompiledModels/Huge.cs" 2000
expect "the compiled model is not covered" 0 "File sizes OK"

make_file "backend/Api/obj/Debug/Huge.cs" 2000
expect "build output is not covered" 0 "File sizes OK"

make_file "web/server/utils/dev-api-mock.ts" 2000
expect "the dev API mock is not covered" 0 "File sizes OK"

# --update writes the current offenders, so the next check passes.
make_file "web/app/utils/fresh.ts" 400
run_check --update > /dev/null
expect "--update records the current offenders" 0 "File sizes OK"
if ! grep -qF "web/app/utils/fresh.ts	400	300" "${scratch}/.github/file-size-baseline.txt"; then
  echo "FAIL --update writes the offender with its line count and limit"
  failures=$((failures + 1))
else
  echo "ok   --update writes the offender with its line count and limit"
fi

# A missing baseline is a setup error, not a silent pass.
rm "${scratch}/.github/file-size-baseline.txt"
expect "a missing baseline fails loudly" 1 "Missing"

if [ "${failures}" -ne 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "All check-file-size tests passed."
