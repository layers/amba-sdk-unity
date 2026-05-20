# Changelog — com.layers.amba (Unity SDK)

All notable changes to this package are documented here.

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
