# Issue Tracker

This repo uses a lightweight JSONL issue tracker. Each epic is one file in `issues/`.
If an issue does not belong to an epic, place it in `issues/default.jsonl`.

## Files
- `issues/<epic>.jsonl`: one epic per file
- `issues/default.jsonl`: issues without an epic

## Schema (per line)
Each line is a single JSON object.

Required fields:
- `id` (string): unique within the epic file, short and stable
- `title` (string)
- `status` (string): `todo`, `doing`, or `done`

Optional fields:
- `type` (string): `bug`, `feature`, `chore`, `docs`, `spike`
- `priority` (string): `low`, `med`, `high`, `urgent`
- `depends_on` (array of string): issue `id` values in the same epic file
- `created` (string): `YYYY-MM-DD`
- `updated` (string): `YYYY-MM-DD`
- `owner` (string)
- `tags` (array of string)
- `notes` (string)

## Dependency Rules
- Dependencies are only within the same epic file.
- Use `depends_on` to list prerequisite issue IDs.

## Example
```json
{"id":"search-001","title":"Add search bar UI","status":"todo","type":"feature","priority":"med","depends_on":["search-000"],"created":"2026-02-06"}
```

## Helper Script
- Create: `python scripts/issue.py create --id search-001 --title "Add search bar UI" --epic search`
- Close: `python scripts/issue.py close --id search-001 --epic search`
- List: `python scripts/issue.py list --epic search`
- List all epics: `python scripts/issue.py list --all`
- Summary: `python scripts/issue.py summary`
- Validate all: `python scripts/issue.py validate`
