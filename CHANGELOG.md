# Changelog — com.layers.amba (Unity SDK)

All notable changes to this package are documented here.

## [4.0.5] — 2026-10-15

- Ships the 4.0.12 core: `Inventory.PurchaseAsync` resolves with the purchase receipt (`purchased`, `currency_code`, `price`, `balance_after`), and a claim of a code no user owns has a null `referrer_id`.

### Packaging

- 4.0.4 was never published, so moving from 4.0.3 to 4.0.5 also brings the 4.0.4 changes listed below.
- The iOS plugin `Runtime/Plugins/iOS/libamba_core.a` is 40 MiB (81 MiB in 4.0.3). It leaves out the SDK's embedded bitcode, which the Xcode linker discards, and the standard library's debug info. It exports the same `amba_*` functions.

### Server behavior

These ship with the API, so apps on earlier versions get them without an upgrade.

- `config.fetch()` evaluates each key's segment and percentage conditions for the signed-in user. Before sign-in, or with an expired session token, it returns each key's default value as before.
- Deep links, inventory, content items, friendships and referral codes come back with the fields the SDK types declare, so `deepLinks.get`, `inventory.getItems` / `getItem` / `consume`, `content.library` / `item` / `createItem` / `updateItem`, every `friends` method and `referrals.getReferralCode` / `claimReferral` return their typed results. Earlier these calls failed reading the response, after the server had already made the change. Claiming a code no user owns needs this version.
- `inventory.consume({ item_id })` takes the catalog item id or the `id` of a row from `inventory.getItems()`.
- A single-item `inventory.purchase` response also carries the inventory row it added to, so earlier versions read it without failing; a bundle purchase needs this version.
- Each Apple or Google sign-in nonce is accepted once; sending a sign-in or link request again with the same nonce returns `401 NONCE_REUSED`.
- `DELETE /v1/client/users/me/push-token` deactivates only the signed-in user's own tokens.

## [4.0.4] — 2026-10-14

### Security

- Ids, keys, short codes and names placed in a request path are percent-encoded, so `/`, `?` and `#` stay part of the value and the request keeps its route. An id of `.`, `..` or the empty string fails with `VALIDATION_ERROR` before any request is sent.

### Server behavior

- New free-tier projects can receive `402 FREE_TIER_LIMIT_REACHED` from the sign-in request calls (magic link, email OTP, SMS OTP) once the project's daily sign-in budget is spent, and from email sends, which are a paid feature on those projects. The error's `details.limit` names the limit and `details.upgrade` carries the one-call upgrade. Projects created before the free-tier limits keep their previous behavior, and no SDK upgrade is needed.
- A free project owned by an agent sandbox account that nobody has claimed answers writes with `403 SANDBOX_CLAIM_REQUIRED` once it has spent 14 days in that account, until the account is claimed.

### Added

- `Amba.OfferingsAsync(offeringId = null)` / `AmbaClient.OfferingsAsync` —
  subscription offerings (the packages your paywall renders,
  provider-neutral). Pass an offering id for one offering; omit it for all.
  Parity with Web/Node and the iOS/Android 4.1.0 SDKs.
- `Amba.Entitlements.RestoreAsync()` — Restore Purchases: re-syncs the user's
  owned entitlements server-side and returns the active set (the App Store
  3.1.1 requirement). Fails without changing access when the validation
  engine is unreachable.

Both methods are generated from the same single-source surface manifest that
drives every other Amba SDK, so the Unity surface can no longer silently lag
the rest of the family.

## [4.0.3] — 2026-06-02

Fixes a packaging bug that made the entire SDK inert when installed from the
UPM mirror. The published package shipped with **no `.meta` files** — neither
for the C# sources/asmdefs nor for the native `libamba_core` binaries — so Unity
dropped every asset on import ("has no meta file, but it's in an immutable
folder. The asset will be ignored"), and the SDK simply wasn't there.

The release pipeline now generates a `.meta` for every file and folder in the
package, with deterministic GUIDs (stable across releases) and correct
per-platform plugin settings for each native binary (Android arm64/armv7, iOS,
macOS universal, Linux x64, Windows x64). No API or behavior changes.

## [4.0.2] — 2026-05-27

Version-alignment release across every published Amba package. Brings
the infra-tier packages (`@layers/amba`, `@layers/amba-shared`,
`@layers/amba-mcp` — previously on 1.x) onto the same 4.x track as the
SDKs so customers can pin a single version everywhere. No surface or
behavior changes in this SDK.

Removed from the package family in this release: the deprecated
`@layers/amba-functions` and `@layers/amba-client` packages were
unpublished from npm. Amba functions are plain ES modules with the
standard `export default { async fetch(req, env, ctx) }` shape — no
wrapper. See docs.amba.dev/functions/runtime for the full contract.

## [4.0.1] — 2026-05-26

Version-alignment release across every Amba SDK. No surface or behavior
changes — bumps every SDK (web, node, react, react-native, expo, core,
kotlin, swift, flutter, unity) to 4.0.1 so customers can pin to a single
version across stacks. See `sdks/CANONICAL_API.md` for the 4.0 surface.

## [4.0.0] — 2026-05-25

Coordinated 4.0 release across all 9 Amba SDKs. Single-source canonical
API surface — see `sdks/CANONICAL_API.md`.

### Added (net-new namespaces)
- `Amba.Users` — `GetAsync(userId?)`, `UpdateAsync(patch, userId?)`
- `Amba.Sessions` — `ListAsync()`, `RevokeAsync(sessionId)`
- `Amba.Sync` — `PushChangesAsync(changes)`, `PullChangesAsync(entityType, checkpointToken?)`
- `Amba.Leagues` — `MeAsync()`, `CohortAsync()`

### Added (existing-namespace methods)
- `Amba.Auth.RequestMagicLinkAsync(email)` / `VerifyMagicLinkAsync(token)` / `LinkAccountAsync(provider, credential)`
- `Amba.Storage.ListAsync(prefix?)` / `DeleteAsync(assetId)` / `DownloadAsync(assetId)` (returns `byte[]`)
- `Amba.Messaging.CreateConversationAsync(participants, metadata?)` / `ListMessagesAsync(conversationId, limit?, offset?)` / `MarkReadAsync(conversationId)`
- `Amba.Friends.SendRequestAsync(userId)` / `AcceptRequestAsync(friendshipId)` / `DeclineRequestAsync(friendshipId)`
- `Amba.Collections.FindNearestAsync(name, vector, k, filter?)` / `CountAsync(name, filter?)`
- `Amba.Catalog.GetAsync(id)`

### Fixed
- **`Amba.Messaging.GetMessageAsync` no longer crashes on first call.** Pre-4.0 the
  wrapper invoked a non-existent `amba_messaging_get_message` native symbol — the
  first call raised `EntryPointNotFoundException`. Phase A implemented the symbol
  with a `(conversation_id, message_id)` signature; the wrapper now passes both
  args and returns the message envelope (or null JSON when not found).
- **README install URL** — fragment changed from `#v1.0.0` to `#4.0.0` to match
  the mirror's tag convention (no `v` prefix). Copy-paste install from the README
  now actually works.

### Added (lifecycle)
- `Amba.ResetAsync()` / `AmbaClient.ResetAsync()` — releases the native core
  singleton so a subsequent `ConfigureAsync` can wire a fresh tenant (multi-tenant
  flow). Maps to `amba_reset` (Phase A).

## [1.0.1] — 2026-05-20

- Added `Amba.Diagnostics.Ping()` — wire-verify primitive that returns a
  server-decided `PingResult { Ok, ServerProjectId, Environment, KeyFingerprint, LatencyMs, Error }`.
  Customers (or installer agents) call this once after `ConfigureAsync` to confirm the
  SDK is talking to the expected project with the expected key in the expected
  environment. Logs to `UnityEngine.Debug.Log` (success) / `Debug.LogError` (failure)
  with `[Amba SDK]` prefix.

## [1.0.0] — 2026-05-17

Initial release.

- 25 namespaces: `Auth`, `Events`, `Collections`, `Storage`, `Push`, `Entitlements`,
  `Ai`, `Config`, `Flags`, `Achievements`, `Challenges`, `Currencies`, `Inventory`,
  `Leaderboards`, `Stores`, `Xp`, `Streaks`, `Feeds`, `Friends`, `Groups`, `Messaging`,
  `Moderation`, `Reviews`, `Roles`, `Referrals`, `Catalog`, `Content`, `DeepLinks`, `Onboarding`
- P/Invoke bridge to the native core library (`libamba_core`) via the `INativeMethods` interface
- Constructor dependency-injection design: tests replace `DefaultNativeMethods` with a fake without touching any global state
- `Amba.*` static facade for single-instance apps; direct `AmbaClient` construction for multi-tenant and testing scenarios
- Typed model classes: `Streak`, `XpBalance`, `Session`
- `AmbaApiError` with structured `Code` property and message-prefix inference
- Supported build targets: iOS, Android (arm64-v8a + armeabi-v7a), macOS, Windows (x86_64), Linux (x86_64)
- `link.xml` to prevent IL2CPP AOT stripping of JSON-deserialized types
- Minimum Unity version: 2022.3 LTS
