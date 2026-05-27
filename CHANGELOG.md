# Changelog — com.layers.amba (Unity SDK)

All notable changes to this package are documented here.

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
  wrapper invoked a non-existent `amba_messaging_get_message` Rust symbol — the
  first call raised `EntryPointNotFoundException`. Phase A implemented the symbol
  with a `(conversation_id, message_id)` signature; the wrapper now passes both
  args and returns the message envelope (or null JSON when not found).
- **README install URL** — fragment changed from `#v1.0.0` to `#4.0.0` to match
  the mirror's tag convention (no `v` prefix). Copy-paste install from the README
  now actually works.

### Added (lifecycle)
- `Amba.ResetAsync()` / `AmbaClient.ResetAsync()` — releases the Rust core
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
- P/Invoke bridge to the Rust core (`libamba_core`) via the `INativeMethods` interface
- Constructor dependency-injection design: tests replace `DefaultNativeMethods` with a fake without touching any global state
- `Amba.*` static facade for single-instance apps; direct `AmbaClient` construction for multi-tenant and testing scenarios
- Typed model classes: `Streak`, `XpBalance`, `Session`
- `AmbaApiError` with structured `Code` property and message-prefix inference
- Supported build targets: iOS, Android (arm64-v8a + armeabi-v7a), macOS, Windows (x86_64), Linux (x86_64)
- `link.xml` to prevent IL2CPP AOT stripping of JSON-deserialized types
- Minimum Unity version: 2022.3 LTS
