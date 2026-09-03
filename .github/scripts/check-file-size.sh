#!/usr/bin/env bash
# Guardrail against oversized source files.
#
# The repository has no linter on the frontends and no file-length analyzer on the backend, so file
# size drifted until a handful of controllers, query services and pages carried most of the reading
# cost. This script is a ratchet, not a style rule: files already over the limit are recorded in a
# baseline and may only shrink. A new file over the limit, or an existing one that grows, fails CI.
#
#   check-file-size.sh            check the working tree against the baseline
#   check-file-size.sh --update   rewrite the baseline from the working tree
#
# Refresh the baseline (and commit it) whenever a split lands, so the list only ever gets shorter.

set -euo pipefail

# Overridable so the test suite can run the same logic against a scratch tree.
cd "${FILE_SIZE_ROOT:-$(dirname "$0")/../..}"
BASELINE="${FILE_SIZE_BASELINE:-.github/file-size-baseline.txt}"

# Limits are deliberately generous — they mark "this file has stopped being one thing", not a style
# preference. A .vue file carries a template and a script block, hence the higher bar.
LIMIT_CS=500
LIMIT_VUE=400
LIMIT_TS=300

# Not covered, on purpose:
#   backend/**/Migrations, backend/**/CompiledModels  generated
#   backend/tests, web/tests, admin/tests             a long test file is far cheaper to read
#   web/server/utils/dev-api-mock.ts                  dev-only fixture, no production reader
list_sources() {
  # Only search roots that exist: the test suite runs the same logic against scratch trees that
  # carry a subset of them.
  existing() { for d in "$@"; do [ -d "$d" ] && printf '%s\n' "$d"; done; return 0; }

  existing backend | while read -r root; do
    find "$root" -name '*.cs' \
      -not -path '*/obj/*' -not -path '*/bin/*' \
      -not -path '*/Migrations/*' -not -path '*/CompiledModels/*' \
      -not -path "$root/tests/*" -print | sed "s/$/\t$LIMIT_CS/"
  done

  existing web/app web/shared admin/app admin/shared | while read -r root; do
    find "$root" -name '*.vue' -print | sed "s/$/\t$LIMIT_VUE/"
  done

  existing web/app web/shared web/server admin/app admin/shared admin/server | while read -r root; do
    find "$root" -name '*.ts' -print
  done | { grep -v '^web/server/utils/dev-api-mock\.ts$' || true; } | sed "s/$/\t$LIMIT_TS/"
}

# "path<TAB>lines<TAB>limit" for every file over its limit, sorted by path.
current_offenders() {
  list_sources | while IFS='	' read -r file limit; do
    lines=$(wc -l < "$file" | tr -d ' ')
    if [ "$lines" -gt "$limit" ]; then
      printf '%s\t%s\t%s\n' "$file" "$lines" "$limit"
    fi
  done | sort
}

offenders=$(current_offenders)

if [ "${1:-}" = "--update" ]; then
  {
    echo "# Files over the size limit, recorded so they can only shrink."
    echo "# Regenerate with .github/scripts/check-file-size.sh --update"
    echo "# path<TAB>lines<TAB>limit"
    echo "$offenders"
  } > "$BASELINE"
  echo "Baseline written: $(printf '%s\n' "$offenders" | grep -c . || true) file(s) over the limit."
  exit 0
fi

if [ ! -f "$BASELINE" ]; then
  echo "Missing $BASELINE — create it with: .github/scripts/check-file-size.sh --update" >&2
  exit 1
fi

printf '%s\n' "$offenders" | awk -F'\t' -v baseline="$BASELINE" '
  BEGIN {
    while ((getline line < baseline) > 0) {
      if (line ~ /^#/ || line == "") continue
      split(line, f, "\t")
      base[f[1]] = f[2]
      known[f[1]] = 1
    }
  }
  /^$/ { next }
  {
    cur[$1] = $2
    limit[$1] = $3
    if (!($1 in base)) {
      newly[++n] = sprintf("  %s — %s lines (limit %s)", $1, $2, $3)
    } else if ($2 + 0 > base[$1] + 0) {
      grew[++g] = sprintf("  %s — %s lines, was %s", $1, $2, base[$1])
    } else if ($2 + 0 < base[$1] + 0) {
      shrank[++s] = sprintf("  %s — %s lines, was %s", $1, $2, base[$1])
    }
  }
  END {
    for (p in base) {
      if (!(p in cur)) fixed[++x] = sprintf("  %s — no longer over the limit", p)
    }

    if (n) { print "New file over the size limit:"; for (i = 1; i <= n; i++) print newly[i]; print "" }
    if (g) { print "Already-oversized file that grew:"; for (i = 1; i <= g; i++) print grew[i]; print "" }

    if (s || x) {
      print "Progress — refresh the baseline with .github/scripts/check-file-size.sh --update"
      for (i = 1; i <= s; i++) print shrank[i]
      for (i = 1; i <= x; i++) print fixed[i]
      print ""
    }

    if (n || g) {
      print "Split the file, or record a deliberate exception by refreshing the baseline."
      exit 1
    }

    printf "File sizes OK — %d file(s) over the limit, none new, none grown.\n", length(base)
  }
'
