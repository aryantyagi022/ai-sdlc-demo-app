#!/usr/bin/env bash
# Append one ai-interactions.jsonl entry for a completed task.
#
# Used by the ai-interaction-log skill. Reuses the hook library so the schema,
# redaction, and locking behaviour are identical to the automatic hook records.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || echo "$PWD")"
# shellcheck source=/dev/null
. "$REPO_ROOT/.github/hooks/scripts/lib.sh"

usage() {
  cat <<'USAGE'
Usage: append-entry.sh [options]

Required:
  --request TEXT          What the user asked for.
  --suggestion TEXT       What the AI did or proposed.
  --verification TEXT     How the result was checked.

Optional:
  --stage NAME            Defaults to the tracked stage.
  --status STATUS         applied (default) | rejected | failed | partial
  --context TEXT          Free-form context.
  --tools a,b,c           Comma-separated tool names.
  --artifacts a,b         Comma-separated file paths.
  --related id1,id2       Comma-separated related entry ids.
  --parent-task ID        Parent task id.
  --human-decision TEXT   Must be the user's own words. Omit if they did not decide.
  --limitation TEXT       Anything that did not work or was out of scope.
  --agent-name NAME       Defaults to copilot-cli.
  --agent-type TYPE       built-in (default) | custom | sub-agent
  --model NAME            Defaults to $COPILOT_MODEL or unknown.
  --mode MODE             ask | plan | agent (default) | edit
  --skills JSON           JSON array for skills_used.
  --subagents JSON        JSON array for subagents_used.
  --mcp JSON              JSON array for mcp_tools_used.
  --id ID                 Use a specific entry id.
  --timestamp ISO8601     Use a specific timestamp (for backfilling).
USAGE
}

csv_to_json() {
  [ -z "${1:-}" ] && { echo '[]'; return; }
  printf '%s' "$1" | jq -Rc 'split(",") | map(gsub("^\\s+|\\s+$";"")) | map(select(length>0))'
}

ARG_STATUS="applied"
ARG_HUMAN=""
HAS_HUMAN=0
ARG_LIMITATION=""
HAS_LIMITATION=0
ARG_PARENT=""
ARG_TOOLS=""
ARG_ARTIFACTS=""
ARG_RELATED=""
R_SKILLS='[]'
R_SUBAGENTS='[]'
R_MCP='[]'
R_REQUEST=""
R_SUGGESTION=""
R_VERIFICATION=""
R_CONTEXT=""
R_STAGE=""
R_AGENT_NAME="copilot-cli"
R_AGENT_TYPE="built-in"
R_MODEL=""
R_MODE=""
R_ID=""
R_TIMESTAMP=""

while [ $# -gt 0 ]; do
  case "$1" in
    --request)        R_REQUEST="${2:-}"; shift 2 ;;
    --suggestion)     R_SUGGESTION="${2:-}"; shift 2 ;;
    --verification)   R_VERIFICATION="${2:-}"; shift 2 ;;
    --context)        R_CONTEXT="${2:-}"; shift 2 ;;
    --stage)          R_STAGE="${2:-}"; shift 2 ;;
    --status)         ARG_STATUS="${2:-}"; shift 2 ;;
    --tools)          ARG_TOOLS="${2:-}"; shift 2 ;;
    --artifacts)      ARG_ARTIFACTS="${2:-}"; shift 2 ;;
    --related)        ARG_RELATED="${2:-}"; shift 2 ;;
    --parent-task)    ARG_PARENT="${2:-}"; shift 2 ;;
    --human-decision) ARG_HUMAN="${2:-}"; HAS_HUMAN=1; shift 2 ;;
    --limitation)     ARG_LIMITATION="${2:-}"; HAS_LIMITATION=1; shift 2 ;;
    --agent-name)     R_AGENT_NAME="${2:-}"; shift 2 ;;
    --agent-type)     R_AGENT_TYPE="${2:-}"; shift 2 ;;
    --model)          R_MODEL="${2:-}"; shift 2 ;;
    --mode)           R_MODE="${2:-}"; shift 2 ;;
    --skills)         R_SKILLS="${2:-[]}"; shift 2 ;;
    --subagents)      R_SUBAGENTS="${2:-[]}"; shift 2 ;;
    --mcp)            R_MCP="${2:-[]}"; shift 2 ;;
    --id)             R_ID="${2:-}"; shift 2 ;;
    --timestamp)      R_TIMESTAMP="${2:-}"; shift 2 ;;
    -h|--help)        usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

if [ -z "$R_REQUEST" ] || [ -z "$R_SUGGESTION" ] || [ -z "$R_VERIFICATION" ]; then
  echo "Error: --request, --suggestion and --verification are required." >&2
  usage
  exit 2
fi

[ -n "$R_STAGE" ] || R_STAGE="$(read_stage)"

R_TOOLS=$(csv_to_json "$ARG_TOOLS")
R_ARTIFACTS=$(csv_to_json "$ARG_ARTIFACTS")
R_RELATED_IDS=$(csv_to_json "$ARG_RELATED")

if [ -n "$ARG_PARENT" ]; then
  R_PARENT_TASK=$(jq -nc --arg v "$ARG_PARENT" '$v')
else
  R_PARENT_TASK='null'
fi

# human_decision stays null unless the user actually said something.
if [ "$HAS_HUMAN" -eq 1 ] && [ -n "$ARG_HUMAN" ]; then
  R_HUMAN_DECISION=$(jq -nc --arg v "$ARG_HUMAN" '$v')
else
  R_HUMAN_DECISION='null'
fi

if [ "$HAS_LIMITATION" -eq 1 ] && [ -n "$ARG_LIMITATION" ]; then
  R_LIMITATION=$(jq -nc --arg v "$ARG_LIMITATION" '$v')
else
  R_LIMITATION='null'
fi

R_STATUS="$ARG_STATUS"
[ -n "$R_MODEL" ] || R_MODEL="${COPILOT_MODEL:-unknown}"
[ -n "$R_MODE" ] || R_MODE="${COPILOT_MODE:-agent}"
[ -n "$R_ID" ] || R_ID="$(new_id)"
[ -n "$R_TIMESTAMP" ] || R_TIMESTAMP="$(now_iso)"

export R_ID R_TIMESTAMP R_STAGE R_AGENT_NAME R_AGENT_TYPE R_MODEL R_MODE \
       R_RELATED_IDS R_PARENT_TASK R_CONTEXT R_REQUEST R_SKILLS R_SUBAGENTS \
       R_MCP R_TOOLS R_SUGGESTION R_HUMAN_DECISION R_VERIFICATION R_LIMITATION \
       R_STATUS R_ARTIFACTS

emit_interaction

echo "Logged entry $R_ID to ai-logs/ai-interactions.jsonl"
