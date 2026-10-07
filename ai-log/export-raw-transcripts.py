"""Copy locally available project session logs without editing their bytes.

Run again after finishing AI sessions and before committing the submission.
Source selection uses the Codex session working directory and Claude project folder.
This script does not generate, summarize, redact, or reconstruct conversations.
"""

import hashlib
import json
import os
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
HOME = Path.home()
DESTINATION = ROOT / "ai-log" / "raw"


def normalized(path):
    return str(path).replace("\\", "/").rstrip("/").casefold()


def belongs_to_project(cwd):
    value = normalized(cwd)
    root = normalized(ROOT)
    return value == root or value.startswith(root + "/")


def discover():
    sources = []
    for folder in ("sessions", "archived_sessions"):
        base = HOME / ".codex" / folder
        if not base.exists():
            continue
        for path in sorted(base.rglob("*.jsonl")):
            with path.open(encoding="utf-8") as stream:
                first = stream.readline()
            if not first:
                continue
            metadata = json.loads(first)
            if metadata.get("type") != "session_meta":
                raise ValueError(f"Unexpected Codex session format: {path}")
            if belongs_to_project(metadata.get("payload", {}).get("cwd", "")):
                sources.append(("codex", path, Path("codex") / folder / path.relative_to(base)))

    # Claude encodes the absolute project directory in its local folder name.
    project_name = "".join(character if character.isascii() and character.isalnum()
                           else "-" for character in str(ROOT))
    projects = HOME / ".claude" / "projects"
    if projects.exists():
        for base in sorted(projects.iterdir()):
            if base.is_dir() and base.name.casefold() == project_name.casefold():
                for path in sorted(base.rglob("*.jsonl")):
                    sources.append(("claude-code", path, Path("claude-code") / base.name / path.relative_to(base)))
    return sources


def main():
    records = []
    for tool, source, relative in discover():
        # Read the complete file bytes once; an active session may append afterward.
        data = source.read_bytes()
        events = [json.loads(line) for line in data.splitlines() if line.strip()]
        target = DESTINATION / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        temporary = target.with_suffix(".jsonl.tmp")
        temporary.write_bytes(data)
        os.replace(temporary, target)
        digest = hashlib.sha256(data).hexdigest()
        if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
            raise RuntimeError(f"Copy verification failed: {target}")
        records.append({
            "tool": tool,
            "source": str(source),
            "file": target.relative_to(ROOT).as_posix(),
            "bytes": len(data),
            "sha256": digest,
            "jsonlRecords": len(events),
            "lastRecordedTimestamp": next((event.get("timestamp") for event in reversed(events)
                                            if event.get("timestamp")), None),
            "sourceUnchangedAtVerification": source.read_bytes() == data,
        })

    if not records:
        raise RuntimeError("No local session logs found for this project.")
    manifest = {
        "snapshotUtc": datetime.now(timezone.utc).isoformat(),
        "project": str(ROOT),
        "method": "Original JSONL file bytes copied unchanged; no redaction or reserialization.",
        "scope": "Locally available Codex sessions with this project cwd (including descendants), "
                 "and all Claude Code JSONL files in this project's directory, including subagents.",
        "limitations": "Active sessions may append after the snapshot. Run this script again before "
                       "the final commit. Deleted logs, other machines, browser-only conversations, "
                       "and other tools without local logs are not recoverable by this script.",
        "counts": dict(Counter(record["tool"] for record in records)),
        "files": records,
    }
    DESTINATION.mkdir(parents=True, exist_ok=True)
    (DESTINATION / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"counts": manifest["counts"], "files": len(records),
                      "bytes": sum(record["bytes"] for record in records),
                      "verified": True, "manifest": "ai-log/raw/manifest.json"}))


if __name__ == "__main__":
    main()
