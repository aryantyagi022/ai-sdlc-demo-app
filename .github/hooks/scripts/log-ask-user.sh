#!/usr/bin/env bash
# ask_user -> human-decisions.jsonl + ai-interactions.jsonl
#
# Usage: log-ask-user.sh answered|declined
#
# The answer is copied verbatim from the tool result. Nothing here ever
# synthesises a human_decision: if no answer was returned, it is recorded as
# null with status "declined".

set -uo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

OUTCOME="${1:-answered}"
INPUT=$(cat)

if [ "$OUTCOME" = "declined" ]; then
  write_raw "postToolUseFailure:ask_user" "$INPUT"
else
  write_raw "postToolUse:ask_user" "$INPUT"
fi

TOOL_NAME=$(printf '%s' "$INPUT" | jq -r '.toolName // ""' 2>/dev/null)
[ "$TOOL_NAME" = "ask_user" ] || { echo '{}'; exit 0; }

SESSION_ID=$(printf '%s' "$INPUT" | jq -r '.sessionId // ""' 2>/dev/null)
CWD=$(printf '%s' "$INPUT" | jq -r '.cwd // ""' 2>/dev/null)
STAGE=$(read_stage)
ENTRY_ID=$(new_id)
TS=$(now_iso)

ARGS=$(printf '%s' "$INPUT" | jq -c '
  (.toolArgs // {}) as $a
  | if ($a|type) == "string" then (try ($a|fromjson) catch {}) else $a end' 2>/dev/null)
[ -n "$ARGS" ] || ARGS='{}'

QUESTION=$(printf '%s' "$ARGS" | jq -r '.message // ""' 2>/dev/null)
SCHEMA=$(printf '%s' "$ARGS" | jq -c '.requestedSchema // {}' 2>/dev/null)
[ -n "$SCHEMA" ] || SCHEMA='{}'

# The fields that were put to the user, flattened for readability.
FIELDS=$(printf '%s' "$SCHEMA" | jq -c '
  [ (.properties // {}) | to_entries[]
    | { field: .key,
        title: (.value.title // ""),
        choices: ( (.value.enum // [])
                   + [ (.value.oneOf // [])[] | .const ]
                   + [ (.value.items.enum // [])[] ]
                   + [ (.value.items.anyOf // [])[] | .const ] ),
        default: (.value.default // null) } ]' 2>/dev/null)
[ -n "$FIELDS" ] || FIELDS='[]'

if [ "$OUTCOME" = "declined" ]; then
  ANSWER_JSON='null'
  DECISION_STATUS="declined"
  ERROR_TEXT=$(printf '%s' "$INPUT" | jq -c '(.error // "") | tostring' 2>/dev/null)
  [ -n "$ERROR_TEXT" ] || ERROR_TEXT='""'
else
  # Verbatim, exactly as the user answered. Never reworded.
  ANSWER_JSON=$(printf '%s' "$INPUT" | jq -c '.toolResult.textResultForLlm // null' 2>/dev/null)
  [ -n "$ANSWER_JSON" ] || ANSWER_JSON='null'
  DECISION_STATUS="answered"
  ERROR_TEXT='""'
fi

DECISION_LINE=$(jq -nc \
  --arg id "$ENTRY_ID" \
  --arg timestamp "$TS" \
  --arg session_id "$SESSION_ID" \
  --arg stage "$STAGE" \
  --arg source "ask_user" \
  --arg question "$QUESTION" \
  --argjson fields "$FIELDS" \
  --argjson requested_schema "$SCHEMA" \
  --argjson answer "$ANSWER_JSON" \
  --arg status "$DECISION_STATUS" \
  --argjson error "$ERROR_TEXT" \
  '{id:$id, timestamp:$timestamp, session_id:$session_id, stage:$stage, source:$source,
    question:$question, fields:$fields, requested_schema:$requested_schema,
    answer:$answer, verbatim:true, status:$status, error:$error}' 2>/dev/null)

append_jsonl "$HUMAN_DECISIONS_LOG" "$(printf '%s' "$DECISION_LINE" | redact)"

R_ID="$ENTRY_ID"
R_TIMESTAMP="$TS"
R_STAGE="$STAGE"
R_AGENT_NAME="copilot-cli"
R_AGENT_TYPE="built-in"
R_RELATED_IDS=$(jq -nc --arg s "$SESSION_ID" 'if $s == "" then [] else [$s] end')
R_PARENT_TASK='null'
R_CONTEXT="session=$SESSION_ID cwd=$CWD"
R_REQUEST="$QUESTION"
R_SKILLS='[]'
R_SUBAGENTS='[]'
R_MCP='[]'
R_TOOLS='["ask_user"]'
R_SUGGESTION="Asked the user to decide: $QUESTION"
R_HUMAN_DECISION="$ANSWER_JSON"
R_ARTIFACTS='[]'

if [ "$DECISION_STATUS" = "declined" ]; then
  R_STATUS="rejected"
  R_VERIFICATION="User declined or cancelled the question; no decision recorded."
  R_LIMITATION='"ask_user returned no answer, so human_decision is null rather than inferred."'
else
  R_STATUS="applied"
  R_VERIFICATION="Answer captured verbatim from the ask_user tool result."
  R_LIMITATION='null'
fi

export R_ID R_TIMESTAMP R_STAGE R_AGENT_NAME R_AGENT_TYPE R_RELATED_IDS R_PARENT_TASK \
       R_CONTEXT R_REQUEST R_SKILLS R_SUBAGENTS R_MCP R_TOOLS R_SUGGESTION \
       R_HUMAN_DECISION R_VERIFICATION R_LIMITATION R_STATUS R_ARTIFACTS

emit_interaction

echo '{}'
exit 0
