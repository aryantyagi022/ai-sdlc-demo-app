#!/usr/bin/env bash
# preToolUse -> raw log + guard decision.
#
# Command preToolUse hooks are fail-closed, so this wrapper must never exit
# non-zero: a crash would deny every tool call in the session.

set -uo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

INPUT=$(cat)
write_raw "preToolUse" "$INPUT"

DECISION=$(printf '%s' "$INPUT" | python3 "$(dirname "$0")/guard.py" 2>/dev/null)

if [ -z "$DECISION" ]; then
  echo '{}'
  exit 0
fi

# Only emit the guard's output if it is valid JSON.
if printf '%s' "$DECISION" | jq -e . >/dev/null 2>&1; then
  printf '%s\n' "$DECISION"
else
  echo '{}'
fi

exit 0
