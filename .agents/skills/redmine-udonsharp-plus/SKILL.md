---
name: redmine-udonsharp-plus
description: Access and manage the Redmine project associated with this UdonSharpPlus workspace. Use for Redmine issues, project metadata, status, trackers, priorities, versions, and other Redmine work concerning UdonSharp Plus.
---

# UdonSharp Plus Redmine

Use the Redmine REST API at `http://192.168.86.102:8080/` for work associated with this repository.

## Project

- Name: `UdonSharp Plus`
- Numeric project ID: `9`
- Identifier: `customudonsharp`

Keep operations scoped to this project unless the user explicitly asks for another scope. When creating or moving an issue, send `project_id: 9` and verify the returned issue belongs to project 9.

## Authentication

Read the API key at runtime from `~/.redmine_api_key`. Send it in the `X-Redmine-API-Key` request header.

Never print, log, commit, copy into generated files, or include the key in a URL. Trim trailing whitespace after reading the file. If the file is missing or empty, stop and tell the user that Redmine credentials are unavailable.

In PowerShell, initialize requests like this:

```powershell
$keyPath = Join-Path $HOME '.redmine_api_key'
$apiKey = (Get-Content -LiteralPath $keyPath -Raw).Trim()
$headers = @{ 'X-Redmine-API-Key' = $apiKey }
```

Use `Invoke-RestMethod` with `$headers` and JSON endpoints under the base URL. Send JSON request bodies as UTF-8-compatible JSON with `Content-Type: application/json`.

## Operating rules

- Treat listing, searching, and viewing Redmine data as read-only operations.
- Create, edit, transition, relate, or delete Redmine records only when the user's request authorizes that mutation.
- Before a mutation, resolve tracker, status, priority, version, category, and assignee IDs from the server instead of guessing them.
- For updates, fetch the current issue first and change only the requested fields. Preserve existing descriptions and notes unless replacement is explicitly requested.
- Do not use a destructive API operation when an ordinary update or closure satisfies the request.
- Report the affected issue number and a direct URL such as `http://192.168.86.102:8080/issues/<id>` after a successful mutation.

Useful endpoints include `/users/current.json`, `/projects/customudonsharp.json`, `/issues.json?project_id=9`, `/issues/<id>.json`, `/trackers.json`, `/issue_statuses.json`, `/enumerations/issue_priorities.json`, and `/projects/9/versions.json`.
