# AI interaction logging hooks

Logs every AI interaction in this repository using [Copilot CLI hooks](https://docs.github.com/en/copilot/reference/hooks-reference).

## Outputs

| File | Tracked in git | Contents |
| --- | --- | --- |
| `ai-logs/raw/<date>.jsonl` | No | Every hook payload, verbatim and redacted |
| `ai-logs/ai-interactions.jsonl` | Yes | One record per interaction, in the agreed schema |
| `ai-logs/human-decisions.jsonl` | Yes | Questions put to the human plus their exact answer |

## What fires when

| Event | Script | Purpose |
| --- | --- | --- |
| `preToolUse` | `guard.sh` → `guard.py` | Blocks unsafe or non-conforming tool calls |
| `sessionStart`, `sessionEnd`, `agentStop`, `subagentStart`, `subagentStop`, `errorOccurred`, `notification` | `log-event.sh` | Lifecycle records |
| `userPromptSubmitted` | `log-prompt.sh` | Prompt record, stage tracking, `DECISION:`/`VERDICT:` capture |
| `postToolUse` / `postToolUseFailure` (tool `ask_user`) | `log-ask-user.sh` | Human decisions |
| `postToolUse` / `postToolUseFailure` (all other tools) | `log-tool.sh` | Tool-call records |

## Guards

`guard.py` runs on `preToolUse` and denies, with an explanation the agent can act on:

| Rule | Blocked | Allowed |
| --- | --- | --- |
| Commit format | `git commit -m "added stuff"` | `type(scope): summary` plus a `Why:` line |
| Credentials | tokens, JWTs, private keys, `password = "..."` | environment variable names |
| Force push | `git push --force`, `git push -f` | `git push --force-with-lease` |
| History/files | `git reset --hard`, `rm -rf` | `git reset --soft`, `rm <file>` |
| Stryker | `dotnet tool install -g dotnet-stryker` | the same command with `--version 4.13.0` |

Only mutating tools (`bash`, `create`, `edit`, `apply_patch`, …) are scanned, so searching
for the word `password` with `grep` is still allowed. Paths under `.github/hooks/` and
`ai-logs/` are exempt from the credential scan, because the detector's own patterns and the
logs it writes would otherwise trip it.

`--force-with-lease` is permitted deliberately: it refuses to overwrite commits you have not
fetched, so it cannot silently destroy a teammate's work.

Both the guard and its wrapper always exit `0`. Command `preToolUse` hooks are fail-closed,
so an unhandled error would otherwise deny every tool call in the session.

## Task-level entries

The hooks log individual tool calls. For one summary record per completed task, use the
`ai-interaction-log` skill in `.github/skills/ai-interaction-log/`.


## Capturing human decisions

Two independent paths feed `human-decisions.jsonl`:

1. **`ask_user` questions.** `ask_user` is an ordinary tool, so `postToolUse` delivers both the
   question (`toolArgs`) and the answer (`toolResult.textResultForLlm`). The answer is copied
   verbatim and marked `"verbatim": true`.

   If the question is declined or cancelled the CLI raises `postToolUseFailure` instead. That is
   logged with `answer: null` and `status: "declined"`. **A missing answer is never inferred or
   reworded** — `human_decision` stays `null`.

2. **Prompt directives.** Any line starting with `DECISION:` or `VERDICT:` is captured from
   `userPromptSubmitted` and stored with the text exactly as typed.

## Stage tracking

A prompt containing a `Stage: <name>` line sets the current stage, which is stored in
`ai-logs/.state/stage` and stamped on every later record.

## Redaction

`redact()` in `lib.sh` strips GitHub/GitLab/Slack/AWS/Google tokens, JWTs, `Bearer` headers,
private keys, and `key: value` pairs for password/secret/token/api_key style names, before
anything is written. Replacements contain no quotes or backslashes, so redacted output stays
valid JSON.

## Notes

- **Hooks load when the CLI starts.** Restart Copilot CLI after changing these files.
- Scripts require `jq`, `flock`, and `uuidgen`, and always exit `0` so logging can never
  interrupt a session.
- Only `bash` entries are configured, so these hooks run on Linux and macOS. Add `powershell`
  equivalents to `ai-logging.json` for Windows.
- `model` and `mode` are read from `COPILOT_MODEL` and `COPILOT_MODE` when set, otherwise they
  fall back to `unknown` and `agent`.

## Testing a script by hand

```bash
echo '{"sessionId":"t","cwd":"/tmp","toolName":"bash","toolArgs":{"command":"ls"},"toolResult":{"resultType":"success","textResultForLlm":"ok"}}' \
  | .github/hooks/scripts/log-tool.sh success
```
