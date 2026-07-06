#!/usr/bin/env bash
# Set a single-select field on GitHub Project #2 ("TrueMain") for an issue.
#
# Usage:
#   project-set.sh <issue-number> Status "Todo" | "In Progress" | "Done"
#   project-set.sh <issue-number> Priority P0 | P1 | P2 | P3
#
# Adds the issue to the project first if it is not on the board yet.
set -euo pipefail

ISSUE="${1:?usage: project-set.sh <issue> <Status|Priority> <value>}"
FIELD="${2:?missing field (Status|Priority)}"
VALUE="${3:?missing value}"

OWNER="ilyanfraimbault"
REPO="TrueMain"
PROJECT_NUMBER=2
PROJECT_ID="PVT_kwHOAhairc4BYUu-"

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
  *) echo "unknown field '$FIELD' (Status|Priority)" >&2; exit 1 ;;
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

gh project item-edit --id "$ITEM_ID" --project-id "$PROJECT_ID" \
  --field-id "$FIELD_ID" --single-select-option-id "$OPTION_ID" >/dev/null

echo "issue #$ISSUE: $FIELD -> $VALUE"
