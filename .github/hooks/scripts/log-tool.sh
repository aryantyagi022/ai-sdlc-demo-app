#!/usr/bin/env bash
# postToolUse / postToolUseFailure -> raw log + ai-interactions.jsonl
#
# Usage: log-tool.sh success|failure
#
# ask_user is deliberately skipped here; log-ask-user.sh owns it so the record
# carries the real human_decision instead of null.

set -uo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

OUTCOME="${1:-success}"
INPUT=$(cat)

if [ "$OUTCOME" = "failure" ]; then
  write_raw "postToolUseFailure" "$INPUT"
else
  write_raw "postToolUse" "$INPUT"
fi

TOOL_NAME=$(printf '%s' "$INPUT" | jq -r '.toolName // ""' 2>/dev/null)
[ -n "$TOOL_NAME" ] || { echo '{}'; exit 0; }
[ "$TOOL_NAME" = "ask_user" ] && { echo '{}'; exit 0; }

SESSION_ID=$(printf '%s' "$INPUT" | jq -r '.sessionId // ""' 2>/dev/null)
AGENT_ID=$(printf '%s' "$INPUT" | jq -r '.agentId // ""' 2>/dev/null)
AGENT_TYPE_IN=$(printf '%s' "$INPUT" | jq -r '.agentType // ""' 2>/dev/null)
PARENT_AGENT=$(printf '%s' "$INPUT" | jq -r '.parentAgentId // ""' 2>/dev/null)
CWD=$(printf '%s' "$INPUT" | jq -r '.cwd // ""' 2>/dev/null)

# toolArgs may arrive as an object or as a JSON-encoded string.
ARGS=$(printf '%s' "$INPUT" | jq -c '
  (.toolArgs // {}) as $a
  | if ($a|type) == "string" then (try ($a|fromjson) catch {}) else $a end' 2>/dev/null)
[ -n "$ARGS" ] || ARGS='{}'

# Files this tool call touched.
R_ARTIFACTS=$(printf '%s' "$ARGS" | jq -c '
  [ .path?, .file_path?, .target?,
    (.paths? | if type=="array" then .[] elif type=="string" then . else empty end) ]
  | map(select(type=="string" and length>0)) | unique' 2>/dev/null)
[ -n "$R_ARTIFACTS" ] || R_ARTIFACTS='[]'

R_SKILLS='[]'
if [ "$TOOL_NAME" = "skill" ]; then
  R_SKILLS=$(printf '%s' "$ARGS" | jq -c '
    [{name: (.skill // ""), type: "skill",
      purpose: "Loaded skill into the session", invocation: "automatic"}]' 2>/dev/null)
fi

R_SUBAGENTS='[]'
if [ "$TOOL_NAME" = "task" ]; then
  STATUS_WORD="completed"
  [ "$OUTCOME" = "failure" ] && STATUS_WORD="failed"
  R_SUBAGENTS=$(printf '%s' "$ARGS" | jq -c --arg st "$STATUS_WORD" '
    [{name: (.name // ""), type: (.agent_type // ""),
      task: (.description // ""), status: $st}]' 2>/dev/null)
fi

R_MCP='[]'
MCP_SERVER=$(mcp_server_for "$TOOL_NAME")
if [ -n "$MCP_SERVER" ]; then
  R_MCP=$(jq -nc --arg server "$MCP_SERVER" --arg tool "$TOOL_NAME" \
    '[{server:$server, tool:$tool, purpose:("Called " + $tool + " via MCP")}]' 2>/dev/null)
fi

# A short human-readable description of what the tool call did.
SUGGESTION=$(printf '%s' "$ARGS" | jq -r --arg tool "$TOOL_NAME" '
  (.description? // .command? // .query? // .pattern? // .prompt? // .path? // .url? // "")
  | tostring
  | if length > 300 then .[0:300] + "..." else . end
  | if length > 0 then $tool + ": " + . else $tool end' 2>/dev/null)
[ -n "$SUGGESTION" ] || SUGGESTION="$TOOL_NAME"

if [ "$OUTCOME" = "failure" ]; then
  R_STATUS="failed"
  R_LIMITATION=$(printf '%s' "$INPUT" | jq -c '
    (.error // "Tool execution failed") | tostring
    | if length > 1000 then .[0:1000] + "..." else . end' 2>/dev/null)
  [ -n "$R_LIMITATION" ] || R_LIMITATION='null'
  R_VERIFICATION="Tool reported failure; result not applied."
else
  R_STATUS="applied"
  R_LIMITATION='null'
  R_VERIFICATION=$(printf '%s' "$INPUT" | jq -r '
    (.toolResult.textResultForLlm // "") | tostring
    | if length > 500 then .[0:500] + "..." else . end' 2>/dev/null)
  [ -n "$R_VERIFICATION" ] || R_VERIFICATION="Tool completed successfully."
fi

if [ -n "$AGENT_ID" ]; then
  R_AGENT_TYPE="sub-agent"
  R_AGENT_NAME="${AGENT_TYPE_IN:-subagent}"
else
  R_AGENT_TYPE="built-in"
  R_AGENT_NAME="copilot-cli"
fi

R_PARENT_TASK='null'
[ -n "$PARENT_AGENT" ] && R_PARENT_TASK=$(jq -nc --arg p "$PARENT_AGENT" '$p')

R_RELATED_IDS=$(jq -nc --arg s "$SESSION_ID" 'if $s == "" then [] else [$s] end')
R_TOOLS=$(jq -nc --arg t "$TOOL_NAME" '[$t]')
R_STAGE=$(read_stage)
R_CONTEXT="session=$SESSION_ID cwd=$CWD"
R_REQUEST=$(printf '%s' "$ARGS" | jq -c 'if length > 0 then . else {} end' 2>/dev/null | cut -c1-2000)
R_SUGGESTION="$SUGGESTION"
R_HUMAN_DECISION='null'

export R_STAGE R_AGENT_NAME R_AGENT_TYPE R_RELATED_IDS R_PARENT_TASK R_CONTEXT \
       R_REQUEST R_SKILLS R_SUBAGENTS R_MCP R_TOOLS R_SUGGESTION R_HUMAN_DECISION \
       R_VERIFICATION R_LIMITATION R_STATUS R_ARTIFACTS

emit_interaction

echo '{}'
exit 0
