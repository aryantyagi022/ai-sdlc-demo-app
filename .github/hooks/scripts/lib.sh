#!/usr/bin/env bash
# Shared helpers for the Copilot CLI AI interaction logging hooks.
#
# Every hook script sources this file. Nothing here may ever fail the session:
# callers are expected to exit 0 and emit `{}` on stdout.

set -uo pipefail

LOG_ROOT="${AI_LOG_ROOT:-$(git rev-parse --show-toplevel 2>/dev/null || echo "$PWD")}"
LOGS_DIR="$LOG_ROOT/ai-logs"
RAW_DIR="$LOGS_DIR/raw"
STATE_DIR="$LOGS_DIR/.state"
INTERACTIONS_LOG="$LOGS_DIR/ai-interactions.jsonl"
HUMAN_DECISIONS_LOG="$LOGS_DIR/human-decisions.jsonl"

mkdir -p "$RAW_DIR" "$STATE_DIR" 2>/dev/null || true

AI_TOOL_NAME="github-copilot-cli"

now_iso() { date -u +%Y-%m-%dT%H:%M:%S.%3NZ; }

new_id() { uuidgen 2>/dev/null || printf 'id-%s-%s' "$(date -u +%s)" "$RANDOM"; }

# Strip anything that looks like a credential. Replacements contain no quotes or
# backslashes so a redacted string stays valid inside JSON.
redact() {
  sed -E \
    -e 's/gh[pousr]_[A-Za-z0-9]{16,}/[REDACTED_GITHUB_TOKEN]/g' \
    -e 's/github_pat_[A-Za-z0-9_]{20,}/[REDACTED_GITHUB_PAT]/g' \
    -e 's/glpat-[A-Za-z0-9_-]{16,}/[REDACTED_GITLAB_TOKEN]/g' \
    -e 's/xox[baprs]-[A-Za-z0-9-]{10,}/[REDACTED_SLACK_TOKEN]/g' \
    -e 's/sk-[A-Za-z0-9_-]{20,}/[REDACTED_API_KEY]/g' \
    -e 's/AKIA[0-9A-Z]{16}/[REDACTED_AWS_KEY]/g' \
    -e 's/AIza[0-9A-Za-z_-]{30,}/[REDACTED_GOOGLE_KEY]/g' \
    -e 's/eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}/[REDACTED_JWT]/g' \
    -e 's/(Bearer|Basic|Token) +[A-Za-z0-9._~+\/=-]{12,}/\1 [REDACTED]/g' \
    -e 's/-----BEGIN[A-Z ]*PRIVATE KEY-----/[REDACTED_PRIVATE_KEY]/g' \
    -e 's/(ssh-rsa|ssh-ed25519) +[A-Za-z0-9+\/=]{32,}/\1 [REDACTED_PUBLIC_KEY]/g' \
    -e 's/([Pp]assword|PASSWORD|[Pp]asswd|[Ss]ecret|SECRET|[Tt]oken|TOKEN|[Aa]pi[_-]?[Kk]ey|API[_-]?KEY|[Cc]lient[_-]?[Ss]ecret|[Aa]ccess[_-]?[Kk]ey)("?[[:space:]]*[:=][[:space:]]*"?)[A-Za-z0-9._~+\/-]{6,}/\1\2[REDACTED]/g'
}

# Append one line to a JSONL file under an exclusive lock so parallel tool calls
# cannot interleave partial writes.
append_jsonl() {
  local target="$1" line="$2"
  [ -n "$line" ] || return 0
  (
    exec 9>>"$target.lock" 2>/dev/null || exit 0
    flock -w 5 9 2>/dev/null || true
    printf '%s\n' "$line" >>"$target"
  ) 2>/dev/null || true
}

# Persist the raw, redacted hook payload exactly as received.
write_raw() {
  local event="$1" payload="$2"
  local file="$RAW_DIR/$(date -u +%Y-%m-%d).jsonl"
  local line
  line=$(jq -nc \
    --arg event "$event" \
    --arg received_at "$(now_iso)" \
    --argjson payload "$(printf '%s' "$payload" | jq -c '.' 2>/dev/null || echo 'null')" \
    '{event:$event, received_at:$received_at, payload:$payload}' 2>/dev/null) || return 0
  append_jsonl "$file" "$(printf '%s' "$line" | redact)"
}

# The pipeline stage, set from any prompt containing a "Stage: <name>" line.
read_stage() { cat "$STATE_DIR/stage" 2>/dev/null || echo ""; }
write_stage() { printf '%s' "$1" >"$STATE_DIR/stage" 2>/dev/null || true; }

# MCP tool names are prefixed with their server name.
mcp_server_for() {
  case "$1" in
    github-mcp-server-*) echo "github-mcp-server" ;;
    playwright-*)        echo "playwright" ;;
    azure-devops-*)      echo "azure-devops" ;;
    azure-mcp-*)         echo "azure-mcp" ;;
    ide-*)               echo "ide" ;;
    *)                   echo "" ;;
  esac
}

# Emit one record in the agreed ai-interactions.jsonl schema. Field order here
# matches the agreed format exactly.
emit_interaction() {
  local line
  line=$(jq -nc \
    --arg id "${R_ID:-$(new_id)}" \
    --arg timestamp "${R_TIMESTAMP:-$(now_iso)}" \
    --arg stage "${R_STAGE:-}" \
    --arg agent "agent" \
    --arg agent_name "${R_AGENT_NAME:-}" \
    --arg agent_type "${R_AGENT_TYPE:-built-in}" \
    --arg ai_tool "$AI_TOOL_NAME" \
    --arg model "${R_MODEL:-${COPILOT_MODEL:-unknown}}" \
    --arg mode "${R_MODE:-${COPILOT_MODE:-agent}}" \
    --argjson related_ids "${R_RELATED_IDS:-[]}" \
    --argjson parent_task "${R_PARENT_TASK:-null}" \
    --arg context "${R_CONTEXT:-}" \
    --arg request "${R_REQUEST:-}" \
    --argjson skills_used "${R_SKILLS:-[]}" \
    --argjson subagents_used "${R_SUBAGENTS:-[]}" \
    --argjson mcp_tools_used "${R_MCP:-[]}" \
    --argjson tools_used "${R_TOOLS:-[]}" \
    --arg suggestion "${R_SUGGESTION:-}" \
    --argjson human_decision "${R_HUMAN_DECISION:-null}" \
    --arg action_verification "${R_VERIFICATION:-}" \
    --argjson limitation "${R_LIMITATION:-null}" \
    --arg status "${R_STATUS:-applied}" \
    --argjson artifacts "${R_ARTIFACTS:-[]}" \
    '{id:$id, timestamp:$timestamp, stage:$stage, agent:$agent, agent_name:$agent_name,
      agent_type:$agent_type, ai_tool:$ai_tool, model:$model, mode:$mode,
      related_ids:$related_ids, parent_task:$parent_task, context:$context, request:$request,
      skills_used:$skills_used, subagents_used:$subagents_used, mcp_tools_used:$mcp_tools_used,
      tools_used:$tools_used, suggestion:$suggestion, human_decision:$human_decision,
      action_verification:$action_verification, limitation:$limitation, status:$status,
      artifacts:$artifacts}' 2>/dev/null) || return 0
  append_jsonl "$INTERACTIONS_LOG" "$(printf '%s' "$line" | redact)"
}
