# Phase 10 — Chat (SignalR): Manual Test Cases

> **Automated coverage**: 11 integration tests (8 EF in-memory: thread
> open / start-new / resolve, message append + preview bump, oldest-first
> ordering, mark-read directionality, recent-list ordering; 3 Mongo via
> Testcontainers: thread doc persistence, message + preview round-trip,
> resolve flips status). All passing. Project total: 235 unit + 210
> integration.
>
> **Note**: ChatHub itself isn't covered by automated tests because it
> requires a hosted SignalR server. The service layer (which the hub
> sits on top of) is fully covered.

## Setup

1. As SuperAdmin, grant `chat.send` to a test "User" role and `chat.reply`
   to "Editor"/"Admin".
2. Sign in as the test user → `_ChatWidget` should render (FAB bottom-right).
3. In another browser/incognito, sign in as the editor/admin → admin sidebar
   shows "Messaging > Chat".

## 1. User widget

| # | Action | Expected |
|---|--------|----------|
| 1.1 | FAB visible bottom-right (56×56 px circle, blue, chat icon). | Only when authenticated and `chat.send` granted. |
| 1.2 | Anonymous visitor → no FAB rendered. | Widget early-returns. |
| 1.3 | Click FAB → popup opens 380×500 (desktop). On `<576px` screens, popup goes full-viewport. | CSS media query `@media (max-width: 575.98px)`. |
| 1.4 | Type a message → Enter → bubble appears right-aligned in blue. | If SignalR connected, sent via hub; else AJAX `/chat/send`. |
| 1.5 | Click ✕ → panel hides, FAB stays. | State preserved. |
| 1.6 | Click ↻ → current thread closes, fresh empty thread opens. | `/chat/new-thread` returns new threadId. |

## 2. Admin panel (`/admin/chat`)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Page renders thread list on left (UserDisplayName + last message preview + status badge) and empty detail on right. | Threads ordered by `LastMessageAt` desc. |
| 2.2 | Click a thread → header shows thread id, messages load right side, oldest-first. | `GET /chat/messages?threadId=...`. |
| 2.3 | Type reply → Enter → bubble appears right-aligned grey on admin side; user widget receives it via SignalR within ~1s. | `SendReply` hub call; AJAX fallback if disconnected. |
| 2.4 | Click "Resolve" → user widget shows banner "This conversation is closed…", input disabled. | `ThreadResolved` event fired to user group. |
| 2.5 | New user message in another thread → list refreshes (thread bubbles to top with new preview). | `NewThreadActivity` event lazy-reloads `/admin/chat/threads`. |
| 2.6 | Admin without `chat.reply` → `/admin/chat` returns 403. Hub `SendReply` / `ResolveThread` throws `HubException("Forbidden.")`. | Verified by `[FcmsAuthorize]` + permission guard inside hub. |

## 3. SignalR fallback to AJAX

| # | Action | Expected |
|---|--------|----------|
| 3.1 | DevTools → block `/hubs/chat` → user widget still works (each send goes through `/chat/send`). | `connection?.state !== 'Connected'` branch. |
| 3.2 | Block `/hubs/chat` then send → admin must refresh to see new messages (no realtime push). | Acceptable degradation. |
| 3.3 | Restore connection → next message uses hub again. | `withAutomaticReconnect()` reconnects. |

## 4. File upload (`/chat/upload`)

| # | Action | Expected |
|---|--------|----------|
| 4.1 | Click 📎, choose `.jpg` (1 MB) → upload succeeds → image bubble appears inline. | Magic bytes `FF D8 FF` validated. |
| 4.2 | Choose `.pdf` → file bubble with "📎 filename.pdf" link. | Magic bytes `25 50 44 46`. |
| 4.3 | Rename `evil.exe` → `evil.jpg` (mismatched magic bytes) → server rejects "File content does not match its extension." | Spoof protection. |
| 4.4 | Disallowed extension (`.svg`, `.sh`) → "Extension '.xxx' not allowed." | Whitelist from `ChatSettings.AllowedExtensions`. |
| 4.5 | Oversized file (>5 MB default) → "File exceeds 5 MB." | `ChatSettings.MaxUploadSizeMb`. |
| 4.6 | Saved files land under `App_Data/storage/uploads/chat/{yyyy}/{MM}/{guid}.{ext}` and the URL is publicly servable. | Local file storage default. |

## 5. Thread state machine

| # | Action | Expected |
|---|--------|----------|
| 5.1 | First user message → `GetOrCreateOpenThreadAsync` creates `ThreadStatus=Open`. | Verified by `GetOrCreateOpenThreadAsync_creates_when_none_exists_then_returns_existing`. |
| 5.2 | Admin resolves → `ThreadStatus=Resolved`, `ResolvedByUserId` set, `ResolvedAt` populated. | Verified by `ResolveThreadAsync_flips_status_and_records_resolver`. |
| 5.3 | User clicks ↻ → original thread becomes `Closed`, fresh `Open` thread inserted. | Verified by `StartNewThreadAsync_closes_open_and_creates_fresh`. |
| 5.4 | Two users each have at most one Open thread; calling `GetOrCreate` repeatedly returns the same row. | Same test. |

## 6. Cross-side read receipts

| # | Action | Expected |
|---|--------|----------|
| 6.1 | User loads `/chat/messages` → all admin-side messages flip `IsRead=true`. | Verified by `MarkReadAsync_user_marks_admin_messages_only` (the user-loads-messages flow calls MarkReadAsync). |
| 6.2 | Admin loads thread detail → all user-side messages flip read. | Same — admin role goes through opposite branch. |

## 7. Database storage cross-check

- **EF**: `SELECT user_id, thread_status, last_message_at FROM fcms_chat_threads ORDER BY last_message_at DESC;`
- **Mongo**: `db.fcms_chat_threads.find({}, {userId:1, threadStatus:1, lastMessageAt:1}).sort({lastMessageAt:-1})` — uses index `ix_chat_threads_user_status` and `ix_chat_threads_last_message_at`.
- **Messages Mongo**: `db.fcms_chat_messages.find({threadId: BinData(...)}).sort({createdAt:1})` — uses `ix_chat_messages_thread_created`.

## 8. Permissions

| # | Action | Expected |
|---|--------|----------|
| 8.1 | User without `chat.send` → widget loads but `SendMessage`/`/chat/send` returns 403. | Permission service guard. |
| 8.2 | User without role/perm at all → no FAB rendered (widget early-returns at view time when not authed). | Defense in depth. |
| 8.3 | Admin without `chat.reply` → `/admin/chat` 403; hub rejects `SendReply` + `ResolveThread`. | Hub-level + controller-level. |

## 9. Edge cases

| # | Action | Expected |
|---|--------|----------|
| 9.1 | User force-quits browser mid-send → message either lands (committed before connection close) or doesn't (failed before commit). No orphaned partial state. | EF UoW transaction. |
| 9.2 | Admin opens panel, leaves it for hours → SignalR reconnects automatically (withAutomaticReconnect). | Built-in. |
| 9.3 | Two admin browsers reply to same thread simultaneously → both messages persist in CreatedAt order (no collision). | UTC ms timestamps. |
| 9.4 | User sends 1000-char message → preview trimmed to 121 chars (120 + ellipsis) for admin list. | Verified by `AddMessageAsync_long_body_is_trimmed_to_120_chars_for_preview`. |
| 9.5 | Module deactivated mid-conversation → existing chat data retained; user widget keeps working (chat is core, not a module). | N/A. |

## 10. Out of scope (future phases)

- **Multi-admin claim/unclaim** — currently any admin can reply to any thread.
- **Typing indicators** — Phase 14 (Engagement) candidate.
- **Push notifications when widget is closed** — leverages Phase 9 notification bell instead.
- **Chat search across threads** — Phase 16 (full-text search).
- **Voice / video** — out of scope entirely.
- **End-to-end encryption** — out of scope; messages are stored plaintext in the DB (admins have full visibility by design).
