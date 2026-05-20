// Fixtures.cs — Smoke fixture loader, C# / .NET companion to the per-run
// fixture provisioning that `e2e/lib/bootstrap.sh` performs as task #16.
//
// bootstrap.sh provisions per-run fixtures (a collection schema and an
// AI prompt — plus optionally a storage bucket) and exports the
// resulting IDs/names via env vars. This file reads them on the C# side
// so smokes don't re-implement env parsing per language.
//
// ── Behavior ─────────────────────────────────────────────────────────
//
//   AMBA_REQUIRE_FIXTURES unset or "0"
//     → returns SmokeFixtures with `null` for any missing env vars.
//       Today's narrow smoke (no fixtures provisioned) uses this mode.
//
//   AMBA_REQUIRE_FIXTURES=1
//     → throws InvalidOperationException naming the first missing env
//       var. The expanded smoke (task #26, post-#16) flips this on so
//       a missing fixture is a loud failure, not a silent skip.
//
// Env vars consumed:
//   - AMBA_SMOKE_COLLECTION_ID
//   - AMBA_SMOKE_COLLECTION_NAME
//   - AMBA_SMOKE_AI_PROMPT_ID
//   - AMBA_SMOKE_AI_PROMPT_KEY
//   - AMBA_SMOKE_STORAGE_BUCKET
//
// ── Where it lives ───────────────────────────────────────────────────
//
// Sits under `unity/Tests/Integration/` next to SmokeTest.cs. The class
// has no `[Category]` attribute so its UNIT tests at
// `Tests/Integration/FixturesTest.cs` run in the `unity-tests` job
// (filter `Category!=Integration` matches uncategorized tests too).
// The fixture LOADER itself is just a helper — the smoke imports it
// when needed.

using System;

namespace Layers.Amba.Tests.Integration
{
    /// <summary>
    /// Fixture handles provisioned by bootstrap.sh for the current smoke
    /// run. Every property is nullable so the loader can return a
    /// partial struct when <c>AMBA_REQUIRE_FIXTURES</c> is not set
    /// (today's narrow smoke). The expanded smoke checks
    /// <see cref="AllPresent"/> before reading individual fields.
    /// </summary>
    public sealed class SmokeFixtures
    {
        public string CollectionId { get; init; }
        public string CollectionName { get; init; }
        public string AiPromptId { get; init; }
        public string AiPromptKey { get; init; }
        public string StorageBucket { get; init; }

        /// <summary>
        /// <c>true</c> iff every fixture handle was provisioned and
        /// present in the env. The expanded smoke gates its expanded
        /// surface paths on this so it can't half-run with three of
        /// five fixtures.
        /// </summary>
        public bool AllPresent =>
            !string.IsNullOrEmpty(CollectionId) &&
            !string.IsNullOrEmpty(CollectionName) &&
            !string.IsNullOrEmpty(AiPromptId) &&
            !string.IsNullOrEmpty(AiPromptKey) &&
            !string.IsNullOrEmpty(StorageBucket);

        public override string ToString() =>
            $"SmokeFixtures(CollectionId: {CollectionId}, " +
            $"CollectionName: {CollectionName}, " +
            $"AiPromptId: {AiPromptId}, " +
            $"AiPromptKey: {AiPromptKey}, " +
            $"StorageBucket: {StorageBucket})";
    }

    public static class Fixtures
    {
        /// <summary>
        /// Load fixture handles from the process env.
        /// </summary>
        /// <param name="envReader">
        /// Injection seam for tests so they can pass a synthetic env
        /// reader instead of mutating real process env. Production
        /// callers omit and the loader uses
        /// <see cref="Environment.GetEnvironmentVariable(string)"/>.
        /// </param>
        public static SmokeFixtures Load(Func<string, string> envReader = null)
        {
            envReader ??= Environment.GetEnvironmentVariable;
            var require = envReader("AMBA_REQUIRE_FIXTURES") == "1";

            string Read(string name)
            {
                var v = envReader(name);
                if (!string.IsNullOrEmpty(v)) return v;
                if (require)
                {
                    throw new InvalidOperationException(
                        $"{name} is required when AMBA_REQUIRE_FIXTURES=1 " +
                        "(provisioned by e2e/lib/bootstrap.sh after task #16 lands)");
                }
                return null;
            }

            return new SmokeFixtures
            {
                CollectionId = Read("AMBA_SMOKE_COLLECTION_ID"),
                CollectionName = Read("AMBA_SMOKE_COLLECTION_NAME"),
                AiPromptId = Read("AMBA_SMOKE_AI_PROMPT_ID"),
                AiPromptKey = Read("AMBA_SMOKE_AI_PROMPT_KEY"),
                StorageBucket = Read("AMBA_SMOKE_STORAGE_BUCKET"),
            };
        }
    }
}
