#!/usr/bin/env bash
# Set a field on GitHub Project #2 ("TrueMain") for an issue.
#
# Usage:
#   project-set.sh <issue-number> Status "Todo" | "In Progress" | "Done"
#   project-set.sh <issue-number> Priority P0 | P1 | P2 | P3
#   project-set.sh <issue-number> Sprint current | next | none | "Sprint 4"
#
# Sprint is the project's iteration field — when the work is planned. Iteration
# ids rotate every time GitHub rolls a new iteration, so current/next are
# resolved from the live configuration by date, never hardcoded.
#
# Adds the issue to the project first if it is not on the board yet.
set -euo pipefail

ISSUE="${1:?usage: project-set.sh <issue> <Status|Priority|Sprint> <value>}"
FIELD="${2:?missing field (Status|Priority|Sprint)}"
VALUE="${3:?missing value}"

OWNER="ilyanfraimbault"
REPO="TrueMain"
PROJECT_NUMBER=2
PROJECT_ID="PVT_kwHOAhairc4BYUu-"
SPRINT_FIELD_ID="PVTIF_lAHOAhairc4BYUu-zhWtII0"

# Emit "id<TAB>title<TAB>startDate<TAB>duration" for every iteration, past and
# future, sorted by start date.
sprint_iterations() {
  gh api graphql \
    -f query='query($owner:String!,$number:Int!){user(login:$owner){projectV2(number:$number){field(name:"Sprint"){... on ProjectV2IterationField{configuration{iterations{id title startDate duration} completedIterations{id title startDate duration}}}}}}}' \
    -f owner="$OWNER" -F number="$PROJECT_NUMBER" \
    -q '.data.user.projectV2.field.configuration
        | (.iterations + .completedIterations)
        | sort_by(.startDate)[]
        | [.id, .title, .startDate, (.duration|tostring)]
        | @tsv'
}

# current = the iteration containing today; next = the first one starting after
# today. Anything else is matched on title.
resolve_sprint() {
  local want="$1" today id title start duration end
  today="$(date -u +%F)"

  while IFS=$'\t' read -r id title start duration; do
    [ -n "$id" ] || continue
    end="$(date -u -d "$start +$duration days" +%F)"
    case "$want" in
      current) [[ "$today" < "$end" && ! "$today" < "$start" ]] && { echo "$id	$title"; return; } ;;
      next)    [[ "$start" > "$today" ]] && { echo "$id	$title"; return; } ;;
      *)       [ "$title" = "$want" ] && { echo "$id	$title"; return; } ;;
    esac
  done < <(sprint_iterations)

  return 1
}

case "$FIELD" in
  Status)
    FIELD_ID="PVTSSF_lAHOAhairc4BYUu-zhTbY7k"
    case "$VALUE" in
      "Todo")        OPTION_ID="f75ad846" ;;
      "In Progress") OPTION_ID="47fc9ee4" ;;
      "Done")        OPTION_ID="98236657" ;;
      *) echo "unknown Status '$VALUE' (Todo|In Progress|Done)" >&2; exit 1 ;;
    esac ;;
  Priority)
    FIELD_ID="PVTSSF_lAHOAhairc4BYUu-zhTbY_g"
    case "$VALUE" in
      P0) OPTION_ID="6d197579" ;;
      P1) OPTION_ID="aca932bd" ;;
      P2) OPTION_ID="8c1b7213" ;;
      P3) OPTION_ID="0a1cef53" ;;
      *) echo "unknown Priority '$VALUE' (P0|P1|P2|P3)" >&2; exit 1 ;;
    esac ;;
  Sprint)
    FIELD_ID="$SPRINT_FIELD_ID"
    if [ "$VALUE" != "none" ]; then
      if ! RESOLVED="$(resolve_sprint "$VALUE")"; then
        echo "no iteration matches '$VALUE'. Known iterations:" >&2
        sprint_iterations | cut -f2 | sed 's/^/  /' >&2
        exit 1
      fi
      ITERATION_ID="${RESOLVED%%	*}"
      VALUE="${RESOLVED#*	}"
    fi ;;
  *) echo "unknown field '$FIELD' (Status|Priority|Sprint)" >&2; exit 1 ;;
esac

item_id() {
  gh api graphql \
    -f query='query($owner:String!,$repo:String!,$n:Int!){repository(owner:$owner,name:$repo){issue(number:$n){projectItems(first:10){nodes{id project{number}}}}}}' \
    -f owner="$OWNER" -f repo="$REPO" -F n="$ISSUE" \
    -q ".data.repository.issue.projectItems.nodes[] | select(.project.number==$PROJECT_NUMBER) | .id"
}

ITEM_ID="$(item_id)"
if [ -z "$ITEM_ID" ]; then
  gh project item-add "$PROJECT_NUMBER" --owner "$OWNER" \
    --url "https://github.com/$OWNER/$REPO/issues/$ISSUE" >/dev/null
  ITEM_ID="$(item_id)"
fi

if [ "$FIELD" = "Sprint" ] && [ "$VALUE" = "none" ]; then
  gh project item-edit --id "$ITEM_ID" --project-id "$PROJECT_ID" \
    --field-id "$FIELD_ID" --clear >/dev/null
elif [ "$FIELD" = "Sprint" ]; then
  gh project item-edit --id "$ITEM_ID" --project-id "$PROJECT_ID" \
    --field-id "$FIELD_ID" --iteration-id "$ITERATION_ID" >/dev/null
else
  gh project item-edit --id "$ITEM_ID" --project-id "$PROJECT_ID" \
    --field-id "$FIELD_ID" --single-select-option-id "$OPTION_ID" >/dev/null
fi

echo "issue #$ISSUE: $FIELD -> $VALUE"
