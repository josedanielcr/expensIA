# AGENTS.md

## Scope
Chrome extension source lives here. Keep the popup simple and put review-specific flows in dedicated screens/files.
- Review-specific UI lives in `review.html`, `review.css`, and `review.js`.
- Keep review page styling aligned with `options.css` so settings and review feel like the same product surface.

## Backend endpoint
- Do not hardcode environment URLs in JavaScript.
- `background/lib/core.js` resolves the backend from the first `*.azurewebsites.net` host permission in `manifest.json`.
- Keep `manifest.prod.json` and `manifest.staging.json` as the source of truth.

## Sync behavior
- Include Gmail `messageId` and `subject` in email payloads sent to the backend.
- The extension should not call Supabase directly.
- Backend persistence happens before Google Sheets append.
- Append only approved/sheet-ready transactions, then mark them synced in the backend.
- Current happy path appends backend-returned `entries` to Google Sheets; backend filters those `entries` to sheet-ready transactions only.
- When `OnEmailPush` returns `pendingReview > 0`, the popup opens the review page.

## Review UI behavior
- Review UI fetches pending rows through the background worker, which calls `/api/review/transactions`.
- Review rows include `id`, `date`, `amount`, `category`, `description`, `confidence_score`, `review_reason`, `subject`, and `sender`.
- The review page supports editing date, amount, category, and description before approval.
- The review page also lets users add a missing category inline and provides a shortcut to the full settings page.
- Inline category additions are saved to `chrome.storage.sync.categories` and immediately selected in the review form.
- Approval is not purely local: the background worker posts corrections to the backend, appends the returned row to Google Sheets, then marks the row `sheet_synced` through the same backend action endpoint.
- Display `review_reason` directly when the backend provides Spanish text; only map legacy/internal codes as fallback.
- Show a queue loading spinner while pending rows load.
- During approval, disable the form, category controls, refresh, and settings shortcut; show an `Aprobando` spinner in the approve button and ignore duplicate submits.
- Keep the refresh control as an icon button, not a full text button.

## Packaging
- Use `./build-extension-zip.sh prod` or `./build-extension-zip.sh staging`.
- Do not commit generated zip files.
