// FixturesTest.cs — Unit tests for the smoke fixture loader.
//
// No `[Category]` attribute on this fixture so the unit-tests CI job
// (filter `Category!=Integration`) picks it up while the smoke job
// (filter `Category=Integration`) skips it. The loader is pure env-
// parsing logic — no HTTP, no native lib, no fixture data needed.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Layers.Amba.Tests.Integration
{
    [TestFixture]
    public class FixturesTest
    {
        private static Dictionary<string, string> AllEnv() => new()
        {
            ["AMBA_SMOKE_COLLECTION_ID"] = "coll_smoke_runs_123",
            ["AMBA_SMOKE_COLLECTION_NAME"] = "smoke_runs",
            ["AMBA_SMOKE_AI_PROMPT_ID"] = "prompt_smoke_abc",
            ["AMBA_SMOKE_AI_PROMPT_KEY"] = "smoke-prompt",
            ["AMBA_SMOKE_STORAGE_BUCKET"] = "smoke-bucket",
        };

        private static Func<string, string> Reader(IDictionary<string, string> env) =>
            key => env.TryGetValue(key, out var v) ? v : null;

        // ── AMBA_REQUIRE_FIXTURES unset / "0" — tolerant mode ─────

        [Test]
        public void Load_ReturnsNulls_WhenEnvEmpty()
        {
            var env = new Dictionary<string, string>();
            var f = Fixtures.Load(Reader(env));
            Assert.That(f.CollectionId, Is.Null);
            Assert.That(f.CollectionName, Is.Null);
            Assert.That(f.AiPromptId, Is.Null);
            Assert.That(f.AiPromptKey, Is.Null);
            Assert.That(f.StorageBucket, Is.Null);
            Assert.That(f.AllPresent, Is.False);
        }

        [Test]
        public void Load_ReturnsNulls_WhenRequireFixturesIsZero()
        {
            // Explicit opt-out of strictness. No throw, even with no
            // other fixture vars present.
            var env = new Dictionary<string, string>
            {
                ["AMBA_REQUIRE_FIXTURES"] = "0",
            };
            var f = Fixtures.Load(Reader(env));
            Assert.That(f.AllPresent, Is.False);
        }

        [Test]
        public void Load_ReadsPresentVars_LeavesMissingAsNull()
        {
            var env = new Dictionary<string, string>
            {
                ["AMBA_SMOKE_COLLECTION_ID"] = "coll_partial",
                // others omitted
            };
            var f = Fixtures.Load(Reader(env));
            Assert.That(f.CollectionId, Is.EqualTo("coll_partial"));
            Assert.That(f.CollectionName, Is.Null);
            Assert.That(f.AllPresent, Is.False);
        }

        // ── AMBA_REQUIRE_FIXTURES=1 — strict mode ─────────────────

        [Test]
        public void Load_ReturnsFullyPopulated_WhenAllEnvPresent()
        {
            var env = AllEnv();
            env["AMBA_REQUIRE_FIXTURES"] = "1";

            var f = Fixtures.Load(Reader(env));

            Assert.That(f.CollectionId, Is.EqualTo("coll_smoke_runs_123"));
            Assert.That(f.CollectionName, Is.EqualTo("smoke_runs"));
            Assert.That(f.AiPromptId, Is.EqualTo("prompt_smoke_abc"));
            Assert.That(f.AiPromptKey, Is.EqualTo("smoke-prompt"));
            Assert.That(f.StorageBucket, Is.EqualTo("smoke-bucket"));
            Assert.That(f.AllPresent, Is.True);
        }

        [Test]
        public void Load_Throws_WhenRequiredFieldMissing_NamingTheVar()
        {
            var env = AllEnv();
            env["AMBA_REQUIRE_FIXTURES"] = "1";
            env.Remove("AMBA_SMOKE_AI_PROMPT_KEY");

            var ex = Assert.Throws<InvalidOperationException>(
                () => Fixtures.Load(Reader(env)));
            Assert.That(ex.Message, Does.Contain("AMBA_SMOKE_AI_PROMPT_KEY"));
        }

        [Test]
        public void Load_TreatsEmptyStringEnvVarAsMissing()
        {
            // bootstrap.sh might export `=""` if a provisioning step
            // failed silently. The loader treats empty the same as
            // absent so the smoke can't accidentally call API endpoints
            // with empty IDs.
            var env = AllEnv();
            env["AMBA_REQUIRE_FIXTURES"] = "1";
            env["AMBA_SMOKE_COLLECTION_ID"] = "";

            var ex = Assert.Throws<InvalidOperationException>(
                () => Fixtures.Load(Reader(env)));
            Assert.That(ex.Message, Does.Contain("AMBA_SMOKE_COLLECTION_ID"));
        }

        [Test]
        public void Load_ErrorMessage_PointsAtBootstrapSh()
        {
            // Operators reading the test output need to know where
            // these fixtures come from. Hard-code the path in the
            // error so debugging is one greppable hop.
            var env = new Dictionary<string, string>
            {
                ["AMBA_REQUIRE_FIXTURES"] = "1",
            };
            var ex = Assert.Throws<InvalidOperationException>(
                () => Fixtures.Load(Reader(env)));
            Assert.That(ex.Message, Does.Contain("e2e/lib/bootstrap.sh"));
        }

        // ── AllPresent semantics ──────────────────────────────────

        [Test]
        public void AllPresent_FalseWhenAnyFieldIsNull()
        {
            var f = new SmokeFixtures
            {
                CollectionId = "a",
                CollectionName = "b",
                AiPromptId = "c",
                AiPromptKey = "d",
                // StorageBucket: null
            };
            Assert.That(f.AllPresent, Is.False);
        }

        [Test]
        public void AllPresent_TrueOnlyWhenEveryFieldIsNonEmpty()
        {
            var f = new SmokeFixtures
            {
                CollectionId = "a",
                CollectionName = "b",
                AiPromptId = "c",
                AiPromptKey = "d",
                StorageBucket = "e",
            };
            Assert.That(f.AllPresent, Is.True);
        }

        [Test]
        public void AllPresent_FalseWhenAnyFieldIsEmptyString()
        {
            // Matches the loader's "empty == missing" semantic above.
            var f = new SmokeFixtures
            {
                CollectionId = "a",
                CollectionName = "b",
                AiPromptId = "c",
                AiPromptKey = "d",
                StorageBucket = "",
            };
            Assert.That(f.AllPresent, Is.False);
        }
    }
}
