#!/usr/bin/env bash
# userPromptSubmitted -> raw log + ai-interactions.jsonl
#
# Also does two extra jobs:
#   1. Tracks the current pipeline stage from a "Stage: <name>" line.
#   2. Captures any line starting with DECISION: or VERDICT: as a human decision.

set -uo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

INPUT=$(cat)
write_raw "userPromptSubmitted" "$INPUT"

PROMPT=$(printf '%s' "$INPUT" | jq -r '.prompt // ""' 2>/dev/null)
SESSION_ID=$(printf '%s' "$INPUT" | jq -r '.sessionId // ""' 2>/dev/null)
CWD=$(printf '%s' "$INPUT" | jq -r '.cwd // ""' 2>/dev/null)
TS=$(now_iso)
ENTRY_ID=$(new_id)

# 1. Stage tracking.
NEW_STAGE=$(printf '%s' "$PROMPT" | grep -oP '^\s*Stage:\s*\K\S+' | head -1)
[ -n "$NEW_STAGE" ] && write_stage "$NEW_STAGE"
STAGE=$(read_stage)

# 2. Explicit human decisions, copied verbatim from the prompt.
DIRECTIVES=$(printf '%s' "$PROMPT" | grep -E '^[[:space:]]*(DECISION|VERDICT):' || true)

R_HUMAN_DECISION='null'
if [ -n "$DIRECTIVES" ]; then
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    KIND=$(printf '%s' "$line" | sed -E 's/^[[:space:]]*(DECISION|VERDICT):.*/\1/')
    TEXT=$(printf '%s' "$line" | sed -E 's/^[[:space:]]*(DECISION|VERDICT):[[:space:]]*//')
    DECISION_LINE=$(jq -nc \
      --arg id "$(new_id)" \
      --arg timestamp "$TS" \
      --arg session_id "$SESSION_ID" \
      --arg stage "$STAGE" \
      --arg source "prompt_directive" \
      --arg directive "$KIND" \
      --arg answer "$TEXT" \
      --arg raw_line "$line" \
      '{id:$id, timestamp:$timestamp, session_id:$session_id, stage:$stage, source:$source,
        directive:$directive, question:null, answer:$answer, raw_line:$raw_line,
        verbatim:true, status:"stated"}' 2>/dev/null)
    append_jsonl "$HUMAN_DECISIONS_LOG" "$(printf '%s' "$DECISION_LINE" | redact)"
  done <<<"$DIRECTIVES"

  R_HUMAN_DECISION=$(printf '%s' "$DIRECTIVES" | jq -Rsc 'rtrimstr("\n")' 2>/dev/null)
  [ -n "$R_HUMAN_DECISION" ] || R_HUMAN_DECISION='null'
fi

R_ID="$ENTRY_ID"
R_TIMESTAMP="$TS"
R_STAGE="$STAGE"
R_AGENT_NAME="copilot-cli"
R_AGENT_TYPE="built-in"
R_RELATED_IDS=$(jq -nc --arg s "$SESSION_ID" 'if $s == "" then [] else [$s] end')
R_PARENT_TASK='null'
R_CONTEXT="session=$SESSION_ID cwd=$CWD"
R_REQUEST=$(printf '%s' "$PROMPT" | cut -c1-4000)
R_SKILLS='[]'
R_SUBAGENTS='[]'
R_MCP='[]'
R_TOOLS='[]'
R_SUGGESTION="User prompt submitted; awaiting agent response."
R_VERIFICATION="Prompt recorded as received by the CLI."
R_LIMITATION='null'
R_STATUS="applied"
R_ARTIFACTS='[]'

export R_ID R_TIMESTAMP R_STAGE R_AGENT_NAME R_AGENT_TYPE R_RELATED_IDS R_PARENT_TASK \
       R_CONTEXT R_REQUEST R_SKILLS R_SUBAGENTS R_MCP R_TOOLS R_SUGGESTION \
       R_HUMAN_DECISION R_VERIFICATION R_LIMITATION R_STATUS R_ARTIFACTS

emit_interaction

echo '{}'
exit 0
