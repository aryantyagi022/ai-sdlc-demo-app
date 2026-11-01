#!/usr/bin/env bash
# Session and agent lifecycle events -> raw log + ai-interactions.jsonl
#
# Usage: log-event.sh sessionStart|sessionEnd|agentStop|subagentStart|subagentStop|errorOccurred|notification

set -uo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

EVENT="${1:-unknown}"
INPUT=$(cat)
write_raw "$EVENT" "$INPUT"

SESSION_ID=$(printf '%s' "$INPUT" | jq -r '.sessionId // ""' 2>/dev/null)
CWD=$(printf '%s' "$INPUT" | jq -r '.cwd // ""' 2>/dev/null)
STAGE=$(read_stage)

R_AGENT_NAME="copilot-cli"
R_AGENT_TYPE="built-in"
R_SUBAGENTS='[]'
R_LIMITATION='null'
R_STATUS="applied"
R_PARENT_TASK='null'
R_REQUEST=""

case "$EVENT" in
  sessionStart)
    SOURCE=$(printf '%s' "$INPUT" | jq -r '.source // ""' 2>/dev/null)
    R_REQUEST=$(printf '%s' "$INPUT" | jq -r '.initialPrompt // ""' 2>/dev/null | cut -c1-4000)
    R_SUGGESTION="Session started (source: $SOURCE)."
    R_VERIFICATION="Logging hooks active for this session."
    ;;
  sessionEnd)
    REASON=$(printf '%s' "$INPUT" | jq -r '.reason // ""' 2>/dev/null)
    R_SUGGESTION="Session ended (reason: $REASON)."
    R_VERIFICATION="Final log flush for session $SESSION_ID."
    [ "$REASON" = "error" ] || [ "$REASON" = "timeout" ] && R_STATUS="failed"
    ;;
  agentStop)
    R_SUGGESTION="Agent finished a turn."
    R_VERIFICATION="Turn completed; transcript at $(printf '%s' "$INPUT" | jq -r '.transcriptPath // "unknown"' 2>/dev/null)."
    ;;
  subagentStart)
    NAME=$(printf '%s' "$INPUT" | jq -r '.agentName // ""' 2>/dev/null)
    R_AGENT_NAME="$NAME"
    R_AGENT_TYPE="sub-agent"
    R_SUBAGENTS=$(jq -nc --arg n "$NAME" \
      --arg d "$(printf '%s' "$INPUT" | jq -r '.agentDescription // ""' 2>/dev/null)" \
      '[{name:$n, type:"sub-agent", task:$d, status:"partial"}]' 2>/dev/null)
    R_SUGGESTION="Subagent $NAME started."
    R_VERIFICATION="Subagent spawned; result pending."
    ;;
  subagentStop)
    NAME=$(printf '%s' "$INPUT" | jq -r '.agentName // ""' 2>/dev/null)
    R_AGENT_NAME="$NAME"
    R_AGENT_TYPE="sub-agent"
    R_PARENT_TASK=$(printf '%s' "$INPUT" | jq -c '.parentAgentId // null' 2>/dev/null)
    [ -n "$R_PARENT_TASK" ] || R_PARENT_TASK='null'
    R_SUBAGENTS=$(jq -nc --arg n "$NAME" \
      --arg t "$(printf '%s' "$INPUT" | jq -r '.agentType // ""' 2>/dev/null)" \
      '[{name:$n, type:$t, task:"", status:"completed"}]' 2>/dev/null)
    R_SUGGESTION=$(printf '%s' "$INPUT" | jq -r '
      (.response // "") | tostring
      | if length > 2000 then .[0:2000] + "..." else . end' 2>/dev/null)
    [ -n "$R_SUGGESTION" ] || R_SUGGESTION="Subagent $NAME completed."
    R_VERIFICATION="Subagent returned its final response to the parent agent."
    ;;
  errorOccurred)
    R_STATUS="failed"
    R_LIMITATION=$(printf '%s' "$INPUT" | jq -c '
      ((.error.name // "Error") + ": " + (.error.message // "")) 
      | if length > 1000 then .[0:1000] + "..." else . end' 2>/dev/null)
    [ -n "$R_LIMITATION" ] || R_LIMITATION='null'
    R_SUGGESTION="Error during $(printf '%s' "$INPUT" | jq -r '.errorContext // "unknown"' 2>/dev/null)."
    R_VERIFICATION="Recoverable: $(printf '%s' "$INPUT" | jq -r '.recoverable // "unknown"' 2>/dev/null)."
    ;;
  notification)
    # Fire-and-forget: raw capture only, no interaction record.
    echo '{}'
    exit 0
    ;;
  *)
    echo '{}'
    exit 0
    ;;
esac

R_STAGE="$STAGE"
R_RELATED_IDS=$(jq -nc --arg s "$SESSION_ID" 'if $s == "" then [] else [$s] end')
R_CONTEXT="session=$SESSION_ID cwd=$CWD event=$EVENT"
R_SKILLS='[]'
R_MCP='[]'
R_TOOLS='[]'
R_HUMAN_DECISION='null'
R_ARTIFACTS='[]'

export R_STAGE R_AGENT_NAME R_AGENT_TYPE R_RELATED_IDS R_PARENT_TASK R_CONTEXT \
       R_REQUEST R_SKILLS R_SUBAGENTS R_MCP R_TOOLS R_SUGGESTION R_HUMAN_DECISION \
       R_VERIFICATION R_LIMITATION R_STATUS R_ARTIFACTS

emit_interaction

echo '{}'
exit 0
