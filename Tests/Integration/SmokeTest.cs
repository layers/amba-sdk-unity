// SmokeTest.cs — Customer-shoes integration smoke for the Unity / C# SDK.
//
// Mirrors the structure of e2e/exercise/node.mjs: a single sequential
// procedure that signs up a fresh dev tenant on staging, drives every
// SDK surface reachable on a freshly-provisioned project, then tears the
// tenant down on every exit path.
//
// Per CLAUDE.md Rule 1 (customer-shoes), the smoke runs from a clean
// shell with internal-team env stripped — `AMBA_API_URL` is the only
// preset input. PAT, project_id, client_key are minted at run time via
// the public `/v1/auth/developer/signup` endpoint.
//
// ── Invocation ────────────────────────────────────────────────────────
//
// Local (with libamba_core on the search path):
//   AMBA_API_URL=https://amba-api-staging-...uc.a.run.app \
//   LD_LIBRARY_PATH=/path/to/target/release \
//   dotnet test unity/Tests/Amba.Tests.csproj --filter Category=Integration
//
// CI: `customer-smoke (unity)` job in .github/workflows/customer-smoke.yml
// builds the .so for the host platform and runs the smoke on ubuntu-latest.
//
// The unit-tests job filters with `Category!=Integration` so this file
// is compile-time present but never executed there. The smoke job uses
// the inverse filter.
//
// ── Surface coverage scope ───────────────────────────────────────────
//
// Mirrors e2e/exercise/node.mjs deliberately — drives ConfigureAsync,
// auth (SignInAnonymouslyAsync + SignOutAsync), events.TrackAsync,
// entitlements (ListAsync + HasAsync), config.FetchAsync. `Auth.MeAsync`,
// `Push.*`, `Storage.*`, `Collections.*`, `Ai.*`, `Flags.FetchAsync` are
// omitted because of documented SDK ↔ server protocol drift (tracked
// as #16 schema provisioning + downstream items). When those land,
// expand here and remove this note.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Layers.Amba.Tests.Integration
{
    [TestFixture]
    [Category("Integration")]
    public class SmokeTest
    {
        // Bootstrap state — populated by signup, read by Cleanup.
        // OneTimeTearDown runs even on test failure so the tenant gets
        // DELETEd on every exit path.
        private string _pat = "";
        private string _projectId = "";
        private string _clientKey = "";
        private string _apiUrl = "";

        // Shared HttpClient — disposing per-call leaks sockets in TIME_WAIT
        // on Linux runners and the smoke can run out of ephemeral ports.
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        [OneTimeTearDown]
        public async Task Cleanup()
        {
            if (string.IsNullOrEmpty(_projectId) || string.IsNullOrEmpty(_pat))
                return;

            try
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"{_apiUrl}/v1/admin/projects/{_projectId}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _pat);
                var resp = await _http.SendAsync(req);
                Console.Error.WriteLine(
                    $"→ Cleanup: DELETE project {_projectId} — status={(int)resp.StatusCode}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"→ Cleanup failed (best-effort): {e.Message}");
            }
        }

        [Test]
        public async Task UnityCustomerShoesSmoke()
        {
            _apiUrl = Environment.GetEnvironmentVariable("AMBA_API_URL") ?? "";
            Assert.That(_apiUrl, Is.Not.Empty, "AMBA_API_URL is required");
            Assert.That(_apiUrl.StartsWith("http"), Is.True,
                $"AMBA_API_URL must be an http(s) URL, got: {_apiUrl}");

            var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local";
            var attempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1";

            // ── Bootstrap: signup a fresh dev tenant ──────────────────
            //
            // Email composition mirrors bootstrap.sh: GITHUB_RUN_ID +
            // GITHUB_RUN_ATTEMPT + epoch_ms + a small random suffix.
            // Re-runs within the same wall second don't collide on
            // email (signup would 409).
            var epochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var rand = Random.Shared.Next(0, 1_000_000);
            var email = $"smoke-unity-{runId}-{attempt}-{epochMs}-{rand}@layers.com";
            var password = $"smoke-{Guid.NewGuid():N}";

            Console.Error.WriteLine($"→ Signup: {email} at {_apiUrl}");
            var signupBody = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["email"] = email,
                ["password"] = password,
                ["name"] = $"smoke-unity-{runId}",
            });
            var signupResp = await _http.PostAsync(
                $"{_apiUrl}/v1/auth/developer/signup",
                new StringContent(signupBody, Encoding.UTF8, "application/json"));
            var signupText = await signupResp.Content.ReadAsStringAsync();
            Assert.That(
                signupResp.IsSuccessStatusCode,
                Is.True,
                $"signup failed: {(int)signupResp.StatusCode} {signupText}");

            {
                var signupDoc = JToken.Parse(signupText);
                var data = signupDoc["data"];
                var project = data["project"];
                _pat = (string)data["pat"] ?? "";
                _projectId = (string)project["project_id"] ?? "";
                _clientKey = (string)project["client_key"] ?? "";
            }

            Assert.That(_pat, Is.Not.Empty);
            Assert.That(_projectId, Is.Not.Empty);
            Assert.That(_clientKey, Is.Not.Empty);
            Console.Error.WriteLine(
                $"  ✓ project={_projectId} client_key={_clientKey[..14]}…");

            // ── Wait for provisioning_status=active (up to 5 min) ─────
            Console.Error.WriteLine("→ Wait for provisioning_status=active");
            var deadline = DateTime.UtcNow.AddMinutes(5);
            var settled = false;
            while (DateTime.UtcNow < deadline)
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{_apiUrl}/v1/admin/projects/{_projectId}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _pat);
                var resp = await _http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                Assert.That(
                    resp.IsSuccessStatusCode,
                    Is.True,
                    $"project poll failed: {(int)resp.StatusCode} {text}");

                var doc = JToken.Parse(text);
                var statusEl = doc["data"]?["provisioning_status"];
                var status = statusEl != null ? ((string)statusEl ?? "") : "";
                Console.Error.WriteLine($"  status={status}");
                if (status == "active") { settled = true; break; }
                Assert.That(status, Is.Not.EqualTo("failed"), "provisioning_status=failed");
                await Task.Delay(5000);
            }
            Assert.That(settled, Is.True,
                "provisioning did not reach active within 5 min");

            // NOTE: Per-run fixture provisioning (collection schemas +
            // AI prompt) is task #16. Until that lands, the surface
            // walk skips collections.* / ai.* — same as node.mjs.

            // ── Configure SDK ─────────────────────────────────────────
            await Amba.ConfigureAsync(apiKey: _clientKey, baseUrl: _apiUrl);
            Assert.That(Amba.AnonymousId, Does.Match(@"^[0-9a-f-]{32,}$"),
                $"anonymousId should be uuid-ish: {Amba.AnonymousId}");
            Assert.That(Amba.IsAuthenticated, Is.False,
                "IsAuthenticated must be false before SignIn");

            // ── Auth: signIn anonymously, capture session ────────────
            var anon = await Amba.Auth.SignInAnonymouslyAsync();
            var sessionToken = (string)anon["session_token"];
            Assert.That(sessionToken, Is.Not.Empty, $"signIn missing session_token");
            var userEl = anon["user"];
            Assert.That(userEl, Is.Not.Null, "signIn missing user object");
            var userId = (string)userEl["id"];
            Assert.That(userId, Is.Not.Empty, "signIn missing user.id");
            Assert.That(Amba.IsAuthenticated, Is.True);
            Assert.That(Amba.AppUserId, Is.EqualTo(userId),
                "AppUserId should track signed-in user");

            // ── Events: track an authenticated event ──────────────────
            await Amba.Events.TrackAsync(
                "unity_smoke_started",
                new Dictionary<string, object>
                {
                    ["run_id"] = runId,
                    ["attempt"] = attempt,
                    ["sdk"] = "csharp",
                });

            // ── Entitlements: list + has(unknown=false) ──────────────
            //
            // Entitlements.ListAsync returns a JToken straight from the
            // wire — the Unity wrapper deliberately doesn't unwrap (unlike
            // Dart's `_expectJsonArray`, which accepts both shapes and
            // returns a List). Staging today returns the bare-array shape
            // `[…]`, but the wrapper-level contract documents both, so
            // the smoke accepts either:
            //   - bare array:  `[{...}, ...]`
            //   - envelope:    `{"data": [{...}, ...]}`
            var entitlements = await Amba.Entitlements.ListAsync();
            JToken entitlementsArr;
            if (entitlements.Type == JTokenType.Array)
            {
                entitlementsArr = entitlements;
            }
            else if (entitlements.Type == JTokenType.Object &&
                     entitlements["data"] is JArray dataEl)
            {
                entitlementsArr = dataEl;
            }
            else
            {
                Assert.Fail(
                    "entitlements.list expected array or {data: [...]} envelope, " +
                    $"got {entitlements.Type}: {entitlements}");
                return; // unreachable, compiler hint
            }
            Assert.That(entitlementsArr.Type, Is.EqualTo(JTokenType.Array));

            var hasUnknown = await Amba.Entitlements.HasAsync("__smoke_unknown__");
            Assert.That(hasUnknown, Is.False,
                "unknown entitlement should be false");

            // ── Config: fetch, verify shape ──────────────────────────
            //
            // After #12 (ConfigBundle ↔ /v1/client/config schema), the
            // payload is `{values: {...}, version: <string|null>}`. The
            // wrapper passes through as JsonElement; we assert structure
            // rather than typed fields.
            var config = await Amba.Config.FetchAsync();
            var valuesEl = config["values"];
            Assert.That(valuesEl, Is.Not.Null, "config missing `values`");
            Assert.That(valuesEl.Type, Is.EqualTo(JTokenType.Object),
                "config.values should be an object");

            // version is Option<String>; null OR non-empty string both OK.
            var versionEl = config["version"];
            if (versionEl != null)
            {
                Assert.That(
                    versionEl.Type == JTokenType.Null ||
                        (versionEl.Type == JTokenType.String &&
                         !string.IsNullOrEmpty((string)versionEl)),
                    Is.True,
                    $"config.version should be null or non-empty string, got: {versionEl}");
            }

            // ── Deliberately omitted (protocol drift, tracked) ────────
            //
            // Auth.MeAsync, Push.*, Storage.*, Collections.*,
            // Ai.Anthropic.*, Flags.FetchAsync — restore after the
            // matching server-side items land (see e2e/exercise/node.mjs
            // for the same skip list).

            // ── SignOut: verify session terminates ───────────────────
            await Amba.Auth.SignOutAsync();
            Assert.That(Amba.IsAuthenticated, Is.False,
                "IsAuthenticated should flip false after SignOut");

            // ── Track after signOut MUST fail with Unauthorized ──────
            //
            // Asserting that it threw is not enough — we need to confirm
            // the error is auth-related (not e.g. a network blip or a
            // cleanup-race). Match the error message shape.
            var threw = false;
            var errMsg = "";
            try
            {
                await Amba.Events.TrackAsync("unity_smoke_should_not_send");
            }
            catch (Exception e)
            {
                threw = true;
                errMsg = e.Message;
            }
            Assert.That(threw, Is.True, "events.track after signOut must throw");
            Assert.That(
                Regex.IsMatch(
                    errMsg,
                    @"unauthor|401|session.*(missing|expired)|missing.*authorization",
                    RegexOptions.IgnoreCase),
                Is.True,
                $"expected Unauthorized-ish error, got: {errMsg}");

            Console.Error.WriteLine("✅ customer-smoke (unity) PASSED");
        }
    }
}
