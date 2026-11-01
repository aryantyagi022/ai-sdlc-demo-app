#!/usr/bin/env python3
"""preToolUse guard for Copilot CLI.

Blocks, with an explanation the agent can act on:
  * git commits whose message is not `type(scope): summary` plus a `Why:` line
  * anything that looks like a credential being written or executed
  * force pushes, `git reset --hard`, `rm -rf`, and unpinned Stryker installs

Always exits 0. Command `preToolUse` hooks are fail-closed, so a crash here
would deny every tool call; every failure path falls through to "allow".
"""

import json
import os
import re
import shlex
import sys

PINNED_STRYKER_VERSION = "4.13.0"

# Tools that can introduce or execute content. Read-only tools are not scanned,
# so searching for the word "password" is still allowed.
MUTATING_TOOLS = {
    "bash", "powershell", "shell",
    "create", "edit", "apply_patch", "str_replace_editor",
}

# Paths allowed to contain credential-shaped text: the detector's own patterns
# and the logs it writes.
SCAN_EXEMPT_PREFIXES = (
    ".github/hooks/",
    "ai-logs/",
)

CREDENTIAL_PATTERNS = [
    (r"gh[pousr]_[A-Za-z0-9]{16,}", "GitHub token"),
    (r"github_pat_[A-Za-z0-9_]{20,}", "GitHub fine-grained PAT"),
    (r"glpat-[A-Za-z0-9_-]{16,}", "GitLab token"),
    (r"xox[baprs]-[A-Za-z0-9-]{10,}", "Slack token"),
    (r"sk-[A-Za-z0-9_-]{20,}", "API key"),
    (r"AKIA[0-9A-Z]{16}", "AWS access key ID"),
    (r"AIza[0-9A-Za-z_-]{30,}", "Google API key"),
    (r"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", "JWT"),
    (r"-----BEGIN[A-Z ]*PRIVATE KEY-----", "private key"),
    (
        r"(?i)\b(password|passwd|secret|api[_-]?key|client[_-]?secret|"
        r"access[_-]?key|auth[_-]?token|token)\b[\"']?\s*[:=]\s*[\"']?[A-Za-z0-9._~+/-]{8,}",
        "hardcoded credential",
    ),
]

CONVENTIONAL_SUBJECT = re.compile(
    r"^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)"
    r"(\([a-zA-Z0-9._/\- ]+\))?!?: .+$"
)
WHY_LINE = re.compile(r"^Why:\s*\S", re.MULTILINE)


def respond(decision=None, reason=None):
    if decision:
        print(json.dumps({
            "permissionDecision": decision,
            "permissionDecisionReason": reason,
        }))
    else:
        print("{}")
    sys.exit(0)


def deny(reason):
    respond("deny", reason)


def split_commands(command):
    """Split a shell line on separators so each segment is checked alone."""
    return re.split(r"&&|\|\||[;|\n]", command)


def tokenize(segment):
    try:
        return shlex.split(segment)
    except ValueError:
        return segment.split()


def iter_strings(value):
    """Yield every string in a nested structure, unescaped."""
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for item in value.values():
            yield from iter_strings(item)
    elif isinstance(value, (list, tuple)):
        for item in value:
            yield from iter_strings(item)


def check_credentials(tool_name, args):
    paths = [
        str(args.get(key, ""))
        for key in ("path", "file_path", "target")
    ]
    for path in paths:
        if not path:
            continue
        normalized = path.replace("\\", "/")
        while normalized.startswith("./"):
            normalized = normalized[2:]
        for prefix in SCAN_EXEMPT_PREFIXES:
            if normalized.startswith(prefix) or ("/" + prefix) in normalized:
                return

    for text in iter_strings(args):
        for pattern, label in CREDENTIAL_PATTERNS:
            if re.search(pattern, text):
                deny(
                    f"Blocked: this {tool_name} call contains what looks like a {label}. "
                    "Never commit or echo real credentials. Use an environment variable "
                    "or a secret store, and pass the variable name instead of its value."
                )


def check_dangerous(segment, tokens):
    lowered = [token.lower() for token in tokens]
    if not tokens:
        return

    if "git" in lowered:
        git_index = lowered.index("git")
        rest = lowered[git_index + 1:]
        subcommand = next((t for t in rest if not t.startswith("-")), "")

        if subcommand == "push":
            for flag in ("--force", "-f"):
                if flag in rest:
                    deny(
                        f"Blocked: force push (`{flag}`) is not allowed. It can destroy "
                        "pushed history for everyone on the branch. Pull and merge or "
                        "rebase, then push normally. If history really must be rewritten, "
                        "`--force-with-lease` is permitted because it refuses to overwrite "
                        "work you have not seen."
                    )

        if subcommand == "reset" and "--hard" in rest:
            deny(
                "Blocked: `git reset --hard` discards uncommitted work irreversibly. "
                "Use `git stash` to set changes aside, or `git restore <path>` to revert "
                "specific files."
            )

    # rm with both recursive and force flags, in any order or combination.
    for index, token in enumerate(lowered):
        if token != "rm" and not token.endswith("/rm"):
            continue
        flags = set()
        for candidate in lowered[index + 1:]:
            if candidate.startswith("--"):
                if candidate == "--recursive":
                    flags.add("r")
                if candidate == "--force":
                    flags.add("f")
            elif candidate.startswith("-") and len(candidate) > 1:
                flags.update(candidate[1:])
            else:
                break
        if "r" in flags and "f" in flags:
            deny(
                "Blocked: `rm -rf` deletes recursively with no confirmation and no undo. "
                "Delete specific paths without `-f`, or use `git clean -n` first to see "
                "what would go."
            )

    # Stryker must stay pinned; newer versions break on .NET 8.
    if "dotnet" in lowered and "tool" in lowered:
        if any("stryker" in token for token in lowered):
            action = "install" if "install" in lowered else (
                "update" if "update" in lowered else "")
            if action:
                version = ""
                if "--version" in lowered:
                    version_index = lowered.index("--version")
                    if version_index + 1 < len(lowered):
                        version = lowered[version_index + 1]
                if version != PINNED_STRYKER_VERSION:
                    deny(
                        f"Blocked: Stryker must be pinned to {PINNED_STRYKER_VERSION}; "
                        "newer versions break on .NET 8. Run "
                        f"`dotnet tool install -g dotnet-stryker --version {PINNED_STRYKER_VERSION}`."
                    )


def extract_commit_message(tokens, cwd):
    """Return the commit message from -m/-F arguments, or None."""
    messages = []
    index = 0
    while index < len(tokens):
        token = tokens[index]
        if token in ("-m", "--message") and index + 1 < len(tokens):
            messages.append(tokens[index + 1])
            index += 2
            continue
        if token.startswith("--message="):
            messages.append(token.split("=", 1)[1])
            index += 1
            continue
        if token.startswith("-m") and len(token) > 2:
            messages.append(token[2:])
            index += 1
            continue
        if token in ("-F", "--file") and index + 1 < len(tokens):
            path = tokens[index + 1]
            if not os.path.isabs(path):
                path = os.path.join(cwd or ".", path)
            try:
                with open(path, "r", encoding="utf-8") as handle:
                    messages.append(handle.read())
            except OSError:
                return None
            index += 2
            continue
        index += 1
    if not messages:
        return None
    return "\n\n".join(messages)


def check_commit(segment, tokens, cwd):
    lowered = [token.lower() for token in tokens]
    if "git" not in lowered or "commit" not in lowered:
        return

    message = extract_commit_message(tokens, cwd)
    if message is None:
        # No -m/-F: git opens an editor and we cannot inspect the message.
        return

    stripped = message.strip()
    if not stripped:
        deny("Blocked: the commit message is empty.")

    lines = stripped.splitlines()
    subject = lines[0].strip()

    if not CONVENTIONAL_SUBJECT.match(subject):
        deny(
            f"Blocked: commit subject {subject!r} is not `type(scope): summary`. "
            "Use one of feat, fix, docs, style, refactor, perf, test, build, ci, chore, "
            "revert, an optional (scope), then ': ' and a summary. "
            "Example: `feat(approval): add per-role approval limits`."
        )

    if not WHY_LINE.search(stripped):
        deny(
            "Blocked: the commit message has no `Why:` line. Add a line starting with "
            "`Why:` explaining the reason for the change, not just what changed. "
            "Example: `Why: approvers could sign off invoices above their authority.`"
        )


def main():
    try:
        payload = json.loads(sys.stdin.read())
    except (ValueError, TypeError):
        respond()

    if not isinstance(payload, dict):
        respond()

    tool_name = payload.get("toolName") or ""
    cwd = payload.get("cwd") or "."
    args = payload.get("toolArgs")

    if isinstance(args, str):
        try:
            args = json.loads(args)
        except ValueError:
            args = {"command": args}
    if not isinstance(args, dict):
        args = {}

    if tool_name not in MUTATING_TOOLS:
        respond()

    check_credentials(tool_name, args)

    command = args.get("command") or ""
    if command:
        for segment in split_commands(command):
            tokens = tokenize(segment)
            check_dangerous(segment, tokens)
            check_commit(segment, tokens, cwd)

    respond()


if __name__ == "__main__":
    try:
        main()
    except Exception:  # never fail closed on an unexpected error
        print("{}")
        sys.exit(0)
