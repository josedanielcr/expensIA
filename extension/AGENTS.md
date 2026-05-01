# AGENTS.md

## Scope
Chrome extension source lives here. Keep the popup simple and put review-specific flows in dedicated screens/files.

## Backend endpoint
- Do not hardcode environment URLs in JavaScript.
- `background/lib/core.js` resolves the backend from the first `*.azurewebsites.net` host permission in `manifest.json`.
- Keep `manifest.prod.json` and `manifest.staging.json` as the source of truth.

## Sync behavior
- Include Gmail `messageId` and `subject` in email payloads sent to the backend.
- The extension should not call Supabase directly.
- Backend persistence happens before Google Sheets append.
- Append only approved/sheet-ready transactions, then mark them synced in the backend.
- Current happy path still appends backend-returned `entries` to Google Sheets.

## Packaging
- Use `./build-extension-zip.sh prod` or `./build-extension-zip.sh staging`.
- Do not commit generated zip files.
