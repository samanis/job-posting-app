# AI log

Transcripts of the AI-assisted sessions that built this repository, in order.

## Raw, unedited session logs

The submission originals are in [`raw/`](raw/manifest.json): 15 Codex JSONL files
and 8 Claude Code JSONL files, including agent sessions. These are byte-for-byte
copies of locally stored logs, without shortening, redaction, or conversion to Markdown.
The [manifest](raw/manifest.json) records each original path, copied byte count,
SHA-256 hash, JSONL record count, and last recorded timestamp. Every copied file
was parsed and its hash verified.

These files are snapshots. Refresh them after finishing AI sessions and before
the final submission commit:

```sh
python ai-log/export-raw-transcripts.py
```

The script copies Codex logs whose session working directory is this repository
or a descendant, and Claude Code logs in this project's local folder. It does not
copy unrelated project sessions. It cannot recover deleted logs or conversations
from other machines, browser-only tools, or tools without available local logs.
The active Codex session can append after copying; its final messages will only
be included by a later refresh.
