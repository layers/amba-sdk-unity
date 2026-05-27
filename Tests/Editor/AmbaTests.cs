// AmbaTests.cs — NUnit tests for the Unity / C# wrapper.
//
// Coverage scope: the Dart-side glue equivalent. The Rust core's behavior
// is covered by 276+ tests in `core/`. These tests verify the C# wrapper's
// JSON encoding, dispatch to the right C function, response decoding
// (success / error / malformed), small validation rules enforced locally
// (e.g. apiKey non-empty), and the constructor DI isolation guarantee
// that the SDK ships.
//
// Test seam: tests construct `new AmbaClient(fakeBindings)` directly.
// No `Amba.*` static state is touched (except for read-only `Amba.Version`).
// No `libamba_core` lookup ever happens.
//
// Runnable two ways:
//  1. `dotnet test unity/Tests/Amba.Tests.csproj` (CI — no Unity license)
//  2. Unity Editor → Window → General → Test Runner → EditMode

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Layers.Amba.Tests
{
    [TestFixture]
    public class AmbaTests
    {
        private FakeNativeMethods _fake;
        private AmbaClient _client;

        [SetUp]
        public void SetUp()
        {
            _fake = new FakeNativeMethods();
            _client = new AmbaClient(_fake);
        }

        // ── meta / sanity ────────────────────────────────────────────

        [Test]
        public void Version_IsNonEmptySemver()
        {
            Assert.That(Amba.Version, Is.Not.Null.And.Not.Empty);
            StringAssert.IsMatch(@"^\d+\.\d+\.\d+", Amba.Version);
        }

        [Test]
        public void AmbaException_MessageIsPreserved()
        {
            var e = new AmbaException("boom");
            Assert.That(e.Message, Is.EqualTo("boom"));
        }

        [Test]
        public void AmbaClient_RejectsNullBindings()
        {
            Assert.Throws<ArgumentNullException>(() => new AmbaClient(null));
        }

        // ── AmbaClient.InitializeAsync / Amba.ConfigureAsync ─────────

        [Test]
        public void InitializeAsync_RejectsEmptyApiKey()
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.InitializeAsync(""));
            // The fake never saw an init call.
            Assert.That(_fake.Calls, Is.Empty);
        }

        [Test]
        public void InitializeAsync_RejectsNullApiKey()
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.InitializeAsync(null));
        }

        [Test]
        public async System.Threading.Tasks.Task InitializeAsync_PassesJsonWithCSharpPlatform()
        {
            _fake.Enqueue("init", null);

            await _client.InitializeAsync("pk_test_123");

            var call = _fake.Calls[0];
            Assert.That(call.Name, Is.EqualTo("init"));
            var root = JToken.Parse(call.Args[0]);
            Assert.That((string)root["api_key"], Is.EqualTo("pk_test_123"));
            Assert.That((string)root["sdk_platform"], Is.EqualTo("csharp"));
            Assert.That(
                (string)root["sdk_wrapper_version"],
                Is.EqualTo($"amba-csharp/{Amba.Version}"));
            Assert.That((bool)root["consent_required"], Is.False);
            Assert.That((bool)root["debug"], Is.False);
            Assert.That(root["base_url"], Is.Null,
                "base_url should be omitted when null");
        }

        [Test]
        public async System.Threading.Tasks.Task InitializeAsync_HonorsBaseUrlAndFlags()
        {
            _fake.Enqueue("init", null);

            await _client.InitializeAsync(
                "pk_live_xyz",
                baseUrl: "https://staging.amba.host",
                consentRequired: true,
                debug: true);

            var root = JToken.Parse(_fake.Calls[0].Args[0]);
            Assert.That((string)root["base_url"], Is.EqualTo("https://staging.amba.host"));
            Assert.That((bool)root["consent_required"], Is.True);
            Assert.That((bool)root["debug"], Is.True);
        }

        [Test]
        public void InitializeAsync_ThrowsAmbaExceptionOnErrorPayload()
        {
            _fake.Enqueue("init", "{\"error\":\"invalid api key\"}");

            // `AmbaApiError` extends `AmbaException` — catch blocks
            // written against the old type still work.
            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.InitializeAsync("pk_bad"));
            Assert.That(ex.Message, Is.EqualTo("invalid api key"));
        }

        [Test]
        public void Amba_StaticFacade_ThrowsWhenNotConfigured()
        {
            // The static facade demands ConfigureAsync first. Reading
            // any namespace through Amba.* should fail loudly. We don't
            // call ConfigureAsync anywhere in the test suite — every
            // test constructs its own AmbaClient — so this state is
            // reproducible.
            Assert.Throws<InvalidOperationException>(() => { var _ = Amba.Events; });
            Assert.Throws<InvalidOperationException>(() => { var _ = Amba.Auth; });
            Assert.Throws<InvalidOperationException>(() => { var _ = Amba.Client; });
            Assert.Throws<InvalidOperationException>(() => { var _ = Amba.AnonymousId; });
        }

        [Test]
        public void Amba_ConfigureAsync_RejectsEmptyApiKey()
        {
            // Validation fires inline before constructing real bindings,
            // so a misconfigured app gets an ArgumentException rather
            // than a DllNotFoundException for libamba_core.
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await Amba.ConfigureAsync(""));
        }

        // ── events ───────────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task EventsTrack_ForwardsNameAndProperties()
        {
            _fake.Enqueue("events_track", null);

            await _client.Events.TrackAsync(
                "purchase",
                new Dictionary<string, object> { ["amount"] = 9.99, ["currency"] = "USD" });

            var call = _fake.Calls[0];
            Assert.That(call.Name, Is.EqualTo("events_track"));
            Assert.That(call.Args[0], Is.EqualTo("purchase"));
            var root = JToken.Parse(call.Args[1]);
            Assert.That((double)root["amount"], Is.EqualTo(9.99));
            Assert.That((string)root["currency"], Is.EqualTo("USD"));
        }

        [Test]
        public async System.Threading.Tasks.Task EventsTrack_PassesNullForOmittedProperties()
        {
            _fake.Enqueue("events_track", null);

            await _client.Events.TrackAsync("app_open");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("app_open"));
            Assert.That(call.Args[1], Is.Null, "properties should marshal as null IntPtr");
        }

        [Test]
        public void EventsTrack_ThrowsOnErrorPayload()
        {
            // Use the canonical "Rate limited:" prefix from the Rust core's
            // AmbaError Display impl so the code-inference helper recognizes it.
            _fake.Enqueue("events_track", "{\"error\":\"Rate limited: try again\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Events.TrackAsync("x"));
            Assert.That(ex.Message, Is.EqualTo("Rate limited: try again"));
            Assert.That(ex.Code, Is.EqualTo("RATE_LIMITED"),
                "Code should be inferred from the `Rate limited` message prefix.");
        }

        // ── auth ─────────────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Auth_SignInAnonymously_DispatchesAndDecodes()
        {
            _fake.Enqueue("auth_sign_in_anonymously", "{\"session_token\":\"sess_a\"}");

            var result = await _client.Auth.SignInAnonymouslyAsync();

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("auth_sign_in_anonymously"));
            Assert.That((string)result["session_token"], Is.EqualTo("sess_a"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_SignInWithEmail_DispatchesArgs()
        {
            _fake.Enqueue("auth_sign_in_with_email", "{\"session_token\":\"sess_e\"}");

            await _client.Auth.SignInWithEmailAsync("alice@example.com", "hunter2");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("alice@example.com"));
            Assert.That(call.Args[1], Is.EqualTo("hunter2"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_SignUpWithEmail_DispatchesArgs()
        {
            _fake.Enqueue("auth_sign_up_with_email", "{\"session_token\":\"sess_s\"}");

            await _client.Auth.SignUpWithEmailAsync("bob@example.com", "pw");

            var call = _fake.Calls[0];
            Assert.That(call.Name, Is.EqualTo("auth_sign_up_with_email"));
            Assert.That(call.Args[0], Is.EqualTo("bob@example.com"));
            Assert.That(call.Args[1], Is.EqualTo("pw"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_SignInWithApple_SendsAppleProvider()
        {
            _fake.Enqueue("auth_sign_in_with_social", "{\"session_token\":\"sess_a\"}");

            await _client.Auth.SignInWithAppleAsync("apple-id-token");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("apple"));
            Assert.That(call.Args[1], Is.EqualTo("apple-id-token"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_SignInWithGoogle_SendsGoogleProvider()
        {
            _fake.Enqueue("auth_sign_in_with_social", "{\"session_token\":\"sess_g\"}");

            await _client.Auth.SignInWithGoogleAsync("google-id-token");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("google"));
            Assert.That(call.Args[1], Is.EqualTo("google-id-token"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_SignOut_PassesRotateFlag()
        {
            _fake.Enqueue("auth_sign_out", null);
            _fake.Enqueue("auth_sign_out", null);

            await _client.Auth.SignOutAsync();
            await _client.Auth.SignOutAsync(rotateAnonymousId: true);

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("0"));
            Assert.That(_fake.Calls[1].Args[0], Is.EqualTo("1"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_RefreshAndMe_DispatchWithNoArgs()
        {
            _fake.Enqueue("auth_refresh", "{\"session_token\":\"r\"}");
            _fake.Enqueue("auth_me", "{\"id\":\"u_1\"}");

            var refreshed = await _client.Auth.RefreshAsync();
            var me = await _client.Auth.MeAsync();

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("auth_refresh"));
            Assert.That(_fake.Calls[1].Name, Is.EqualTo("auth_me"));
            Assert.That((string)refreshed["session_token"], Is.EqualTo("r"));
            Assert.That((string)me["id"], Is.EqualTo("u_1"));
        }

        // ── auth: OTP (4.0 coverage backfill) ────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Auth_RequestEmailOtp_DispatchesEmail()
        {
            _fake.Enqueue("auth_request_email_otp", null);

            await _client.Auth.RequestEmailOtpAsync("alice@example.com");

            var call = _fake.Calls[0];
            Assert.That(call.Name, Is.EqualTo("auth_request_email_otp"));
            Assert.That(call.Args[0], Is.EqualTo("alice@example.com"));
        }

        [Test]
        public void Auth_RequestEmailOtp_ThrowsOnErrorPayload()
        {
            _fake.Enqueue("auth_request_email_otp", "{\"error\":\"Rate limited: too many OTPs\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Auth.RequestEmailOtpAsync("alice@example.com"));
            Assert.That(ex.Code, Is.EqualTo("RATE_LIMITED"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_VerifyEmailOtp_DispatchesAndNotifies()
        {
            _fake.Enqueue("auth_verify_email_otp", "{\"session_token\":\"s\",\"user\":{\"id\":\"u_1\"}}");
            Session captured = null;
            using var sub = _client.Auth.OnAuthStateChange(s => captured = s);

            var result = await _client.Auth.VerifyEmailOtpAsync("alice@example.com", "123456");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("alice@example.com"));
            Assert.That(call.Args[1], Is.EqualTo("123456"));
            Assert.That((string)result["session_token"], Is.EqualTo("s"));
            // OnAuthStateChange must fire on a successful OTP verify.
            Assert.That(captured, Is.Not.Null);
            Assert.That((string)captured.User["id"], Is.EqualTo("u_1"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_RequestSmsOtp_DispatchesPhone()
        {
            _fake.Enqueue("auth_request_sms_otp", null);

            await _client.Auth.RequestSmsOtpAsync("+15551234567");

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("auth_request_sms_otp"));
            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("+15551234567"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_VerifySmsOtp_DispatchesAndNotifies()
        {
            _fake.Enqueue("auth_verify_sms_otp", "{\"session_token\":\"s\",\"user\":{\"id\":\"u_p\"}}");
            Session captured = null;
            using var sub = _client.Auth.OnAuthStateChange(s => captured = s);

            var result = await _client.Auth.VerifySmsOtpAsync("+15551234567", "987654");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("+15551234567"));
            Assert.That(_fake.Calls[0].Args[1], Is.EqualTo("987654"));
            Assert.That((string)result["session_token"], Is.EqualTo("s"));
            Assert.That(captured, Is.Not.Null);
            Assert.That((string)captured.User["id"], Is.EqualTo("u_p"));
        }

        // ── auth: magic link + linkAccount (NEW in 4.0) ──────────────

        [Test]
        public async System.Threading.Tasks.Task Auth_RequestMagicLink_DispatchesEmail()
        {
            _fake.Enqueue("auth_request_magic_link", null);

            await _client.Auth.RequestMagicLinkAsync("alice@example.com");

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("auth_request_magic_link"));
            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("alice@example.com"));
        }

        [Test]
        public void Auth_RequestMagicLink_ThrowsOnErrorPayload()
        {
            _fake.Enqueue("auth_request_magic_link", "{\"error\":\"Validation error: bad email\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Auth.RequestMagicLinkAsync("not-an-email"));
            Assert.That(ex.Code, Is.EqualTo("VALIDATION_ERROR"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_VerifyMagicLink_DispatchesAndNotifies()
        {
            _fake.Enqueue("auth_verify_magic_link", "{\"session_token\":\"s\",\"user\":{\"id\":\"u_m\"}}");
            Session captured = null;
            using var sub = _client.Auth.OnAuthStateChange(s => captured = s);

            var result = await _client.Auth.VerifyMagicLinkAsync("magic-token-abc");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("magic-token-abc"));
            Assert.That((string)result["session_token"], Is.EqualTo("s"));
            Assert.That(captured, Is.Not.Null);
            Assert.That((string)captured.User["id"], Is.EqualTo("u_m"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_LinkAccount_DispatchesProviderAndCredential()
        {
            _fake.Enqueue("auth_link_account", "{\"session_token\":\"s\",\"user\":{\"id\":\"u_link\"}}");
            Session captured = null;
            using var sub = _client.Auth.OnAuthStateChange(s => captured = s);

            var result = await _client.Auth.LinkAccountAsync("apple", "apple-id-token-xyz");

            var call = _fake.Calls[0];
            Assert.That(call.Name, Is.EqualTo("auth_link_account"));
            Assert.That(call.Args[0], Is.EqualTo("apple"));
            Assert.That(call.Args[1], Is.EqualTo("apple-id-token-xyz"));
            Assert.That((string)result["user"]["id"], Is.EqualTo("u_link"));
            Assert.That(captured, Is.Not.Null);
        }

        // ── users (NEW in 4.0) ────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Users_Get_PassesNullUserIdForCurrent()
        {
            _fake.Enqueue("users_get", "{\"id\":\"u_current\"}");

            var result = await _client.Users.GetAsync();

            Assert.That(_fake.Calls[0].Args[0], Is.Null, "null user_id → current user");
            Assert.That((string)result["id"], Is.EqualTo("u_current"));
        }

        [Test]
        public async System.Threading.Tasks.Task Users_Get_ForwardsExplicitUserId()
        {
            _fake.Enqueue("users_get", "{\"id\":\"u_42\"}");

            await _client.Users.GetAsync("u_42");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("u_42"));
        }

        [Test]
        public async System.Threading.Tasks.Task Users_Update_SendsNullUserIdAndJsonPatch()
        {
            _fake.Enqueue("users_update", "{\"id\":\"u_current\",\"display_name\":\"Bob\"}");

            var result = await _client.Users.UpdateAsync(new Dictionary<string, object>
            {
                ["display_name"] = "Bob",
                ["avatar_url"] = "https://x/y.png",
            });

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.Null);
            var root = JToken.Parse(call.Args[1]);
            Assert.That((string)root["display_name"], Is.EqualTo("Bob"));
            Assert.That((string)root["avatar_url"], Is.EqualTo("https://x/y.png"));
            Assert.That((string)result["display_name"], Is.EqualTo("Bob"));
        }

        [Test]
        public void Users_Update_RejectsNullPatch()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _client.Users.UpdateAsync(null));
        }

        // ── sessions (NEW in 4.0) ─────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Sessions_List_DispatchesAndReturnsArray()
        {
            _fake.Enqueue("sessions_list", "{\"data\":[{\"id\":\"sess_1\"},{\"id\":\"sess_2\"}]}");

            var result = await _client.Sessions.ListAsync();

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("sessions_list"));
            Assert.That(_fake.Calls[0].Args, Is.Empty);
            Assert.That(((JArray)result["data"]).Count, Is.EqualTo(2));
        }

        [Test]
        public async System.Threading.Tasks.Task Sessions_Revoke_ForwardsSessionId()
        {
            _fake.Enqueue("sessions_revoke", null);

            await _client.Sessions.RevokeAsync("sess_xyz");

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("sessions_revoke"));
            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("sess_xyz"));
        }

        [Test]
        public void Sessions_Revoke_ThrowsOnErrorPayload()
        {
            _fake.Enqueue("sessions_revoke", "{\"error\":\"Not found: session\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Sessions.RevokeAsync("sess_gone"));
            Assert.That(ex.Code, Is.EqualTo("NOT_FOUND"));
        }

        // ── sync (NEW in 4.0) ─────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Sync_PushChanges_SerializesArray()
        {
            _fake.Enqueue("sync_push_changes", "{\"applied\":2,\"conflicts\":0}");

            var changes = new List<Dictionary<string, object>>
            {
                new() { ["op"] = "insert", ["collection"] = "posts", ["id"] = "p_1" },
                new() { ["op"] = "update", ["collection"] = "posts", ["id"] = "p_2" },
            };
            var result = await _client.Sync.PushChangesAsync(changes);

            var call = _fake.Calls[0];
            var arr = JArray.Parse(call.Args[0]);
            Assert.That(arr.Count, Is.EqualTo(2));
            Assert.That((string)arr[0]["op"], Is.EqualTo("insert"));
            Assert.That((int)result["applied"], Is.EqualTo(2));
        }

        [Test]
        public void Sync_PushChanges_RejectsNull()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _client.Sync.PushChangesAsync(null));
        }

        [Test]
        public async System.Threading.Tasks.Task Sync_PullChanges_OmitsCheckpointWhenNull()
        {
            _fake.Enqueue("sync_pull_changes", "{\"changes\":[],\"checkpoint_token\":\"ck_1\"}");

            await _client.Sync.PullChangesAsync("collections.posts");

            var root = JToken.Parse(_fake.Calls[0].Args[0]);
            Assert.That((string)root["entity_type"], Is.EqualTo("collections.posts"));
            Assert.That(root["checkpoint_token"], Is.Null);
        }

        [Test]
        public async System.Threading.Tasks.Task Sync_PullChanges_ForwardsCheckpoint()
        {
            _fake.Enqueue("sync_pull_changes", "{\"changes\":[],\"checkpoint_token\":\"ck_2\"}");

            await _client.Sync.PullChangesAsync("collections.posts", checkpointToken: "ck_1");

            var root = JToken.Parse(_fake.Calls[0].Args[0]);
            Assert.That((string)root["checkpoint_token"], Is.EqualTo("ck_1"));
        }

        [Test]
        public void Sync_PullChanges_RejectsEmptyEntityType()
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.Sync.PullChangesAsync(""));
        }

        // ── leagues (NEW in 4.0) ──────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Leagues_Me_DispatchesAndReturnsMembership()
        {
            _fake.Enqueue("leagues_me", "{\"tier\":\"gold\",\"position\":3}");

            var result = await _client.Leagues.MeAsync();

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("leagues_me"));
            Assert.That((string)result["tier"], Is.EqualTo("gold"));
        }

        [Test]
        public async System.Threading.Tasks.Task Leagues_Cohort_DispatchesAndReturnsCohort()
        {
            _fake.Enqueue("leagues_cohort", "{\"members\":[{\"id\":\"u_1\"},{\"id\":\"u_2\"}]}");

            var result = await _client.Leagues.CohortAsync();

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("leagues_cohort"));
            Assert.That(((JArray)result["members"]).Count, Is.EqualTo(2));
        }

        // ── lifecycle: Amba.ResetAsync / AmbaClient.ResetAsync ────────

        [Test]
        public async System.Threading.Tasks.Task Reset_OnClient_DispatchesAmbaReset()
        {
            _fake.Enqueue("reset", null);

            await _client.ResetAsync();

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("reset"));
            Assert.That(_fake.Calls[0].Args, Is.Empty);
        }

        [Test]
        public void Reset_ThrowsOnErrorPayload()
        {
            _fake.Enqueue("reset", "{\"error\":\"amba_reset: identity clear failed (core dropped anyway): persistence error\"}");

            // The Rust core only returns a payload when something went wrong;
            // a successful reset returns null.
            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.ResetAsync());
            Assert.That(ex.Message, Does.Contain("amba_reset"));
        }

        [Test]
        public void Reset_StaticFacade_ThrowsWhenNotConfigured()
        {
            // Symmetric with the rest of Amba.*: ResetAsync requires
            // ConfigureAsync first.
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Amba.ResetAsync());
        }

        [Test]
        public async System.Threading.Tasks.Task Reset_NotifiesAuthSubscribersWithNull()
        {
            // Cycle-3 BugBot finding: ResetAsync is documented as a
            // sign-out / multi-tenant-swap path, so OnAuthStateChange
            // subscribers must observe the session-cleared transition the
            // same way they do for SignOutAsync. Otherwise UI keeps
            // rendering the previous signed-in user.
            _fake.Enqueue("reset", null);
            Session captured = new Session(); // sentinel so we can detect explicit null
            int callCount = 0;
            using var sub = _client.Auth.OnAuthStateChange(s => { captured = s; callCount++; });

            await _client.ResetAsync();

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(captured, Is.Null, "Reset should publish null to indicate the session ended.");
        }

        [Test]
        public void Reset_DoesNotNotifyAuthSubscribersOnError()
        {
            // The notification fires on success only — a failing reset
            // leaves the native session state ambiguous, so we don't
            // signal "session ended" until amba_reset actually clears it.
            _fake.Enqueue("reset", "{\"error\":\"amba_reset: persistence error\"}");
            int callCount = 0;
            using var sub = _client.Auth.OnAuthStateChange(_ => callCount++);

            Assert.ThrowsAsync<AmbaApiError>(async () => await _client.ResetAsync());

            Assert.That(callCount, Is.EqualTo(0),
                "OnAuthStateChange must not fire when the native reset call fails.");
        }

        // ── collections ──────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Collections_Find_EncodesEmptyOptionsWhenOmitted()
        {
            _fake.Enqueue("collections_find", "{\"data\":[]}");

            await _client.Collections.FindAsync("posts");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("posts"));
            var root = JToken.Parse(call.Args[1]);
            Assert.That(((JObject)root).Count, Is.EqualTo(0));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Find_SerializesOptionsJson()
        {
            _fake.Enqueue("collections_find", "{}");

            await _client.Collections.FindAsync(
                "posts",
                new Dictionary<string, object>
                {
                    ["where"] = new Dictionary<string, object> { ["author_id"] = "u_1" },
                    ["limit"] = 25,
                });

            var root = JToken.Parse(_fake.Calls[0].Args[1]);
            Assert.That((int)root["limit"], Is.EqualTo(25));
            Assert.That(
                (string)root["where"]["author_id"],
                Is.EqualTo("u_1"));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_FindOne_PassesNameAndId()
        {
            _fake.Enqueue("collections_find_one", "{\"id\":\"row_1\"}");

            var result = await _client.Collections.FindOneAsync("posts", "row_1");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("posts"));
            Assert.That(_fake.Calls[0].Args[1], Is.EqualTo("row_1"));
            Assert.That((string)result["id"], Is.EqualTo("row_1"));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Insert_SerializesRowJson()
        {
            _fake.Enqueue("collections_insert", "{\"id\":\"row_new\"}");

            await _client.Collections.InsertAsync(
                "posts",
                new Dictionary<string, object> { ["title"] = "hello", ["votes"] = 0 });

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("posts"));
            var root = JToken.Parse(call.Args[1]);
            Assert.That((string)root["title"], Is.EqualTo("hello"));
            Assert.That((int)root["votes"], Is.EqualTo(0));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Update_SendsNameIdSet()
        {
            _fake.Enqueue("collections_update", "{\"id\":\"row_1\"}");

            await _client.Collections.UpdateAsync(
                "posts",
                "row_1",
                new Dictionary<string, object> { ["title"] = "edited" });

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("posts"));
            Assert.That(call.Args[1], Is.EqualTo("row_1"));
            var root = JToken.Parse(call.Args[2]);
            Assert.That((string)root["title"], Is.EqualTo("edited"));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Delete_ToleratesNullAndConfirmation()
        {
            _fake.Enqueue("collections_delete", null);
            await _client.Collections.DeleteAsync("posts", "row_1");

            _fake.Enqueue("collections_delete", "{\"deleted\":true}");
            await _client.Collections.DeleteAsync("posts", "row_2");

            Assert.That(_fake.Calls, Has.Count.EqualTo(2));
        }

        [Test]
        public void Collections_Delete_ThrowsOnErrorPayload()
        {
            _fake.Enqueue("collections_delete", "{\"error\":\"forbidden\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Collections.DeleteAsync("posts", "row_x"));
            Assert.That(ex.Message, Is.EqualTo("forbidden"));
        }

        // ── collections: 4.0 — findNearest + count ───────────────────

        [Test]
        public async System.Threading.Tasks.Task Collections_FindNearest_SerializesOptions()
        {
            _fake.Enqueue("collections_find_nearest", "{\"data\":[{\"id\":\"row_1\"}]}");

            var vector = new[] { 0.1f, 0.2f, 0.3f };
            var result = await _client.Collections.FindNearestAsync(
                "posts", vector, k: 5,
                filter: new Dictionary<string, object> { ["author_id"] = "u_1" },
                vectorField: "embedding");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("posts"));
            var root = JToken.Parse(call.Args[1]);
            Assert.That((string)root["vector_field"], Is.EqualTo("embedding"));
            Assert.That(((JArray)root["query_vector"]).Count, Is.EqualTo(3));
            Assert.That((int)root["k"], Is.EqualTo(5));
            // Shorthand `{ author_id: "u_1" }` is normalized to a single
            // Condition node so the Rust core's Filter deserializer accepts it.
            Assert.That((string)root["filter"]["column"], Is.EqualTo("author_id"));
            Assert.That((string)root["filter"]["op"], Is.EqualTo("eq"));
            Assert.That((string)root["filter"]["value"], Is.EqualTo("u_1"));
            Assert.That(((JArray)result["data"]).Count, Is.EqualTo(1));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_FindNearest_PassesThroughFilterTree()
        {
            _fake.Enqueue("collections_find_nearest", "{\"data\":[]}");

            var vector = new[] { 0.1f };
            await _client.Collections.FindNearestAsync(
                "posts", vector, k: 1,
                filter: new Dictionary<string, object>
                {
                    ["and"] = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object> { ["column"] = "a", ["op"] = "eq", ["value"] = 1 },
                        new Dictionary<string, object> { ["column"] = "b", ["op"] = "gt", ["value"] = 2 },
                    },
                });

            var root = JToken.Parse(_fake.Calls[0].Args[1]);
            // Already a Filter tree — pass through untouched.
            Assert.That(((JArray)root["filter"]["and"]).Count, Is.EqualTo(2));
        }

        [Test]
        public void Collections_FindNearest_RejectsNullVector()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _client.Collections.FindNearestAsync("posts", null, k: 1));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Count_PassesNullFilterWhenOmitted()
        {
            _fake.Enqueue("collections_count", "{\"data\":{\"count\":42}}");

            var result = await _client.Collections.CountAsync("posts");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("posts"));
            Assert.That(_fake.Calls[0].Args[1], Is.Null);
            Assert.That((int)result["data"]["count"], Is.EqualTo(42));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Count_ForwardsFilter()
        {
            _fake.Enqueue("collections_count", "{\"data\":{\"count\":7}}");

            await _client.Collections.CountAsync(
                "posts",
                new Dictionary<string, object> { ["author_id"] = "u_1" });

            var root = JToken.Parse(_fake.Calls[0].Args[1]);
            // Shorthand `{ author_id: "u_1" }` is normalized to a single
            // Condition node — the Rust core's `Filter::Condition` shape.
            Assert.That((string)root["column"], Is.EqualTo("author_id"));
            Assert.That((string)root["op"], Is.EqualTo("eq"));
            Assert.That((string)root["value"], Is.EqualTo("u_1"));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Count_FoldsMultiKeyShorthandIntoAnd()
        {
            _fake.Enqueue("collections_count", "{\"data\":{\"count\":3}}");

            await _client.Collections.CountAsync(
                "posts",
                new Dictionary<string, object>
                {
                    ["author_id"] = "u_1",
                    ["status"] = "published",
                });

            var root = JToken.Parse(_fake.Calls[0].Args[1]);
            var and = (JArray)root["and"];
            Assert.That(and.Count, Is.EqualTo(2));
            // Iteration order on Dictionary<,> is insertion order on modern CLRs.
            Assert.That((string)and[0]["column"], Is.EqualTo("author_id"));
            Assert.That((string)and[0]["op"], Is.EqualTo("eq"));
            Assert.That((string)and[1]["column"], Is.EqualTo("status"));
            Assert.That((string)and[1]["op"], Is.EqualTo("eq"));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Count_PassesThroughFilterTree()
        {
            _fake.Enqueue("collections_count", "{\"data\":{\"count\":1}}");

            await _client.Collections.CountAsync(
                "posts",
                new Dictionary<string, object>
                {
                    ["column"] = "votes",
                    ["op"] = "gt",
                    ["value"] = 10,
                });

            var root = JToken.Parse(_fake.Calls[0].Args[1]);
            Assert.That((string)root["column"], Is.EqualTo("votes"));
            Assert.That((string)root["op"], Is.EqualTo("gt"));
            Assert.That((int)root["value"], Is.EqualTo(10));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Count_PassesThroughNotTree()
        {
            _fake.Enqueue("collections_count", "{\"data\":{\"count\":0}}");

            await _client.Collections.CountAsync(
                "posts",
                new Dictionary<string, object>
                {
                    ["not"] = new Dictionary<string, object>
                    {
                        ["column"] = "deleted", ["op"] = "eq", ["value"] = true,
                    },
                });

            var root = JToken.Parse(_fake.Calls[0].Args[1]);
            Assert.That((string)root["not"]["column"], Is.EqualTo("deleted"));
        }

        [Test]
        public async System.Threading.Tasks.Task Collections_Count_TreatsEmptyDictAsNoFilter()
        {
            _fake.Enqueue("collections_count", "{\"data\":{\"count\":99}}");

            await _client.Collections.CountAsync(
                "posts", new Dictionary<string, object>());

            Assert.That(_fake.Calls[0].Args[1], Is.Null);
        }

        [Test]
        public void Collections_Count_RejectsConditionWithExtraKeys()
        {
            // Cycle-2 BugBot finding: Rust `Filter::Condition` deserializer
            // is `#[serde(untagged)]` so unknown fields silently drop. A
            // dict like `{column, op, value, author_id}` would ship to FFI
            // with `author_id` quietly dropped and the customer would see
            // wrong counts with no error. Surface it at the call site.
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.Collections.CountAsync(
                    "posts",
                    new Dictionary<string, object>
                    {
                        ["column"] = "votes",
                        ["op"] = "gt",
                        ["value"] = 10,
                        ["author_id"] = "u_1",
                    }));
        }

        [Test]
        public void Collections_Count_RejectsStructuralMixedWithExtraKeys()
        {
            // Same untagged-deserializer trap: `{and:[…], author_id:"u_1"}`
            // would silently drop one half. Reject at the wrapper.
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.Collections.CountAsync(
                    "posts",
                    new Dictionary<string, object>
                    {
                        ["and"] = new List<object>(),
                        ["author_id"] = "u_1",
                    }));
        }

        [Test]
        public void Collections_Count_RejectsColumnWithoutOp()
        {
            // Cycle-3 BugBot finding: `{column:"votes", author_id:"u_1"}`
            // (column present, op missing) used to fall through to the
            // shorthand path and ship spurious
            // `{column:"column", op:"eq", value:"votes"}` +
            // `{column:"author_id", op:"eq", value:"u_1"}` conditions to the
            // FFI — wrong rows, no error. Reject so the customer hears at
            // the call site rather than after-the-fact wrong counts.
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.Collections.CountAsync(
                    "posts",
                    new Dictionary<string, object>
                    {
                        ["column"] = "votes",
                        ["author_id"] = "u_1",
                    }));
        }

        [Test]
        public void Collections_Count_RejectsOpWithoutColumn()
        {
            // Symmetric guard — `{op:"eq", value:5}` is also a half-shaped
            // Condition that would silently shorthand-fold.
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.Collections.CountAsync(
                    "posts",
                    new Dictionary<string, object>
                    {
                        ["op"] = "eq",
                        ["value"] = 5,
                    }));
        }

        [Test]
        public void Collections_Count_RejectsColumnOnlyShorthand()
        {
            // Just `{column:"votes"}` — no `op`, no other keys. Looks like a
            // Condition the customer forgot to finish; the shorthand path
            // would have shipped `{column:"column", op:"eq", value:"votes"}`
            // (treating the literal key name as a column).
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.Collections.CountAsync(
                    "posts",
                    new Dictionary<string, object>
                    {
                        ["column"] = "votes",
                    }));
        }

        // ── storage ──────────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Storage_Presign_DefaultsRetentionDaysToMinusOne()
        {
            _fake.Enqueue("storage_presign", "{\"upload_id\":\"u_1\"}");

            await _client.Storage.PresignAsync(
                bucket: "media",
                filename: "cat.jpg",
                mimeType: "image/jpeg",
                sizeBytes: 12345);

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("media"));
            Assert.That(call.Args[1], Is.EqualTo("cat.jpg"));
            Assert.That(call.Args[2], Is.EqualTo("image/jpeg"));
            Assert.That(call.Args[3], Is.EqualTo("12345"));
            Assert.That(call.Args[4], Is.EqualTo("-1"));
        }

        [Test]
        public async System.Threading.Tasks.Task Storage_Presign_ForwardsRetentionDays()
        {
            _fake.Enqueue("storage_presign", "{\"upload_id\":\"u_1\"}");

            await _client.Storage.PresignAsync(
                bucket: "media",
                filename: "cat.jpg",
                mimeType: "image/jpeg",
                sizeBytes: 12345,
                retentionDays: 90);

            Assert.That(_fake.Calls[0].Args[4], Is.EqualTo("90"));
        }

        [Test]
        public async System.Threading.Tasks.Task Storage_Commit_PassesUploadAndAssetIds()
        {
            _fake.Enqueue("storage_commit", "{\"asset_id\":\"asset_1\"}");

            await _client.Storage.CommitAsync("upload_xyz", "asset_1");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("upload_xyz"));
            Assert.That(_fake.Calls[0].Args[1], Is.EqualTo("asset_1"));
        }

        // ── storage: 4.0 — list / delete / download ─────────────────

        [Test]
        public async System.Threading.Tasks.Task Storage_List_PassesNullPrefixWhenOmitted()
        {
            _fake.Enqueue("storage_list", "{\"data\":[]}");

            await _client.Storage.ListAsync();

            Assert.That(_fake.Calls[0].Args[0], Is.Null);
        }

        [Test]
        public async System.Threading.Tasks.Task Storage_List_ForwardsPrefix()
        {
            _fake.Enqueue("storage_list", "{\"data\":[{\"id\":\"a_1\"}]}");

            var result = await _client.Storage.ListAsync("avatars/");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("avatars/"));
            Assert.That(((JArray)result["data"]).Count, Is.EqualTo(1));
        }

        [Test]
        public async System.Threading.Tasks.Task Storage_Delete_ForwardsAssetId()
        {
            _fake.Enqueue("storage_delete", null);

            await _client.Storage.DeleteAsync("asset_xyz");

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("storage_delete"));
            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("asset_xyz"));
        }

        [Test]
        public async System.Threading.Tasks.Task Storage_Download_DecodesBase64Envelope()
        {
            // Wire shape is `{"data":"<base64>"}` per Phase A FFI. "Zm9vYmFy" = "foobar".
            _fake.Enqueue("storage_download", "{\"data\":\"Zm9vYmFy\"}");

            var bytes = await _client.Storage.DownloadAsync("asset_xyz");

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("storage_download"));
            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("asset_xyz"));
            Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo("foobar"));
        }

        [Test]
        public void Storage_Download_ThrowsOnMalformedEnvelope()
        {
            _fake.Enqueue("storage_download", "{\"unexpected\":\"shape\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Storage.DownloadAsync("asset_xyz"));
            Assert.That(ex.Code, Is.EqualTo("UNKNOWN_ERROR"));
            Assert.That(ex.Message, Does.Contain("missing data"));
        }

        [Test]
        public void Storage_Download_ThrowsAmbaApiErrorOnInvalidBase64()
        {
            // Cycle-3 BugBot finding: a corrupt `data` field used to raise
            // raw FormatException, breaking the storage namespace's uniform
            // failure surface (every other storage method routes through
            // JsonUtil → AmbaApiError). Verify the wrapper now surfaces
            // it as AmbaApiError with a useful message.
            _fake.Enqueue("storage_download", "{\"data\":\"not%%%valid%%%base64\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Storage.DownloadAsync("asset_xyz"));
            Assert.That(ex.Code, Is.EqualTo("UNKNOWN_ERROR"));
            Assert.That(ex.Message, Does.Contain("not valid base64"));
        }

        [Test]
        public void Storage_Download_ThrowsOnErrorPayload()
        {
            _fake.Enqueue("storage_download", "{\"error\":\"Not found: asset\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Storage.DownloadAsync("asset_missing"));
            Assert.That(ex.Code, Is.EqualTo("NOT_FOUND"));
        }

        // ── push ─────────────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Push_Register_DispatchesAllThreeArgs()
        {
            _fake.Enqueue("push_register", "{\"registered\":true}");

            await _client.Push.RegisterAsync("token_abc", "apns", "com.example.app");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("token_abc"));
            Assert.That(call.Args[1], Is.EqualTo("apns"));
            Assert.That(call.Args[2], Is.EqualTo("com.example.app"));
        }

        [Test]
        public async System.Threading.Tasks.Task Push_Register_PassesNullBundleIdWhenOmitted()
        {
            _fake.Enqueue("push_register", "{\"registered\":true}");

            await _client.Push.RegisterAsync("token_abc", "fcm");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("token_abc"));
            Assert.That(call.Args[1], Is.EqualTo("fcm"));
            Assert.That(call.Args[2], Is.Null);
        }

        [Test]
        public async System.Threading.Tasks.Task Push_Subscribe_ForwardsTopic()
        {
            _fake.Enqueue("push_subscribe", null);

            await _client.Push.SubscribeAsync("news");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("news"));
        }

        // ── entitlements ─────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Entitlements_List_ReturnsParsedJson()
        {
            _fake.Enqueue(
                "entitlements_list",
                "{\"data\":[{\"name\":\"pro\"},{\"name\":\"team\"}]}");

            var result = await _client.Entitlements.ListAsync();

            var arr = (JArray)result["data"];
            Assert.That(arr.Count, Is.EqualTo(2));
            Assert.That((string)arr[0]["name"], Is.EqualTo("pro"));
        }

        [Test]
        public async System.Threading.Tasks.Task Entitlements_Has_ReturnsBindingBoolean()
        {
            _fake.SeedEntitlementHas("pro", true);
            _fake.SeedEntitlementHas("enterprise", false);

            Assert.That(await _client.Entitlements.HasAsync("pro"), Is.True);
            Assert.That(await _client.Entitlements.HasAsync("enterprise"), Is.False);
        }

        // ── ai ───────────────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Ai_Anthropic_Messages_Create_SerializesRequestBody()
        {
            _fake.Enqueue(
                "ai_anthropic_messages",
                "{\"id\":\"msg_1\",\"content\":[{\"type\":\"text\",\"text\":\"hi\"}]}");

            var result = await _client.Ai.Anthropic.Messages.CreateAsync(
                new Dictionary<string, object>
                {
                    ["model"] = "claude-3-opus-20240229",
                    ["max_tokens"] = 256,
                    ["messages"] = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object>
                        {
                            ["role"] = "user",
                            ["content"] = "ping",
                        },
                    },
                });

            var call = _fake.Calls[0];
            var root = JToken.Parse(call.Args[0]);
            Assert.That((string)root["model"], Is.EqualTo("claude-3-opus-20240229"));
            Assert.That((int)root["max_tokens"], Is.EqualTo(256));
            Assert.That(
                (string)root["messages"][0]["role"],
                Is.EqualTo("user"));
            Assert.That((string)result["id"], Is.EqualTo("msg_1"));
        }

        // ── config + flags ───────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Config_Fetch_ReturnsDecodedObject()
        {
            _fake.Enqueue("config_fetch", "{\"version\":3,\"values\":{\"theme\":\"dark\"}}");

            var result = await _client.Config.FetchAsync();

            Assert.That((int)result["version"], Is.EqualTo(3));
            Assert.That((string)result["values"]["theme"],
                Is.EqualTo("dark"));
        }

        [Test]
        public async System.Threading.Tasks.Task Flags_Fetch_ReturnsDecodedArray()
        {
            _fake.Enqueue("flags_fetch", "[{\"key\":\"f1\",\"value\":true}]");

            var result = await _client.Flags.FetchAsync();

            Assert.That(result.Type, Is.EqualTo(JTokenType.Array));
            Assert.That(((JArray)result).Count, Is.EqualTo(1));
            Assert.That((string)result[0]["key"], Is.EqualTo("f1"));
        }

        // ── Client-level accessors ───────────────────────────────────

        [Test]
        public void AnonymousId_ThrowsWhenBindingsReturnNull()
        {
            _fake.EnqueueNullPtr("anonymous_id");
            Assert.Throws<InvalidOperationException>(() => { var _ = _client.AnonymousId; });
        }

        [Test]
        public void AnonymousId_ThrowsAmbaExceptionOnErrorPayload()
        {
            _fake.Enqueue("anonymous_id", "{\"error\":\"not initialized\"}");
            var ex = Assert.Throws<AmbaApiError>(() => { var _ = _client.AnonymousId; });
            Assert.That(ex.Message, Is.EqualTo("not initialized"));
            Assert.That(ex.Code, Is.EqualTo("NOT_INITIALIZED"));
        }

        [Test]
        public void AnonymousId_ReturnsRawValueOnSuccess()
        {
            _fake.Enqueue("anonymous_id", "anon-abc-123");
            Assert.That(_client.AnonymousId, Is.EqualTo("anon-abc-123"));
        }

        [Test]
        public void IsAuthenticated_ReflectsBindings()
        {
            _fake.SeedIsAuthenticated(true);
            Assert.That(_client.IsAuthenticated, Is.True);

            _fake.SeedIsAuthenticated(false);
            Assert.That(_client.IsAuthenticated, Is.False);
        }

        [Test]
        public void SetDebug_ForwardsBoolToBindings()
        {
            _client.SetDebug(true);
            Assert.That(_fake.LastSetDebug, Is.EqualTo(1u));

            _client.SetDebug(false);
            Assert.That(_fake.LastSetDebug, Is.EqualTo(0u));
        }

        // ── Auth.GetSessionAsync / AnonymousId / OnAuthStateChange ──

        [Test]
        public async System.Threading.Tasks.Task Auth_GetSessionAsync_ReturnsNull_WhenNotAuthenticated()
        {
            _fake.SeedIsAuthenticated(false);
            var session = await _client.Auth.GetSessionAsync();
            Assert.That(session, Is.Null);
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_GetSessionAsync_ReturnsSessionShell_WhenAuthenticated()
        {
            _fake.SeedIsAuthenticated(true);
            _fake.Enqueue("auth_me", "{\"id\":\"u_1\",\"email\":\"alice@example.com\"}");

            var session = await _client.Auth.GetSessionAsync();
            Assert.That(session, Is.Not.Null);
            // Mirrors TS Session: tokens are SDK-managed → empty strings
            // on the consumer side; user is the parsed `auth.me()` payload.
            Assert.That(session.SessionToken, Is.EqualTo(""));
            Assert.That(session.RefreshToken, Is.EqualTo(""));
            Assert.That(session.ExpiresAt, Is.EqualTo(""));
            Assert.That((string)session.User["id"], Is.EqualTo("u_1"));
            Assert.That((string)session.User["email"], Is.EqualTo("alice@example.com"));
        }

        [Test]
        public void Auth_AnonymousId_MirrorsClientLevelProperty()
        {
            _fake.Enqueue("anonymous_id", "anon-xyz-789");
            // Same value as _client.AnonymousId — surfaced here for TS parity.
            Assert.That(_client.Auth.AnonymousId, Is.EqualTo("anon-xyz-789"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_OnAuthStateChange_FiresOnSignIn()
        {
            _fake.Enqueue("auth_sign_in_with_email", "{\"session_token\":\"s\",\"user\":{\"id\":\"u_1\"}}");
            Session captured = null;
            int callCount = 0;
            using var sub = _client.Auth.OnAuthStateChange(s => { captured = s; callCount++; });

            await _client.Auth.SignInWithEmailAsync("alice@example.com", "hunter2");

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(captured, Is.Not.Null);
            Assert.That((string)captured.User["id"], Is.EqualTo("u_1"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_OnAuthStateChange_FiresOnSignOutWithNull()
        {
            _fake.Enqueue("auth_sign_out", null);
            Session captured = new Session(); // sentinel so we can detect explicit null
            int callCount = 0;
            using var sub = _client.Auth.OnAuthStateChange(s => { captured = s; callCount++; });

            await _client.Auth.SignOutAsync();

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(captured, Is.Null, "SignOut should publish null to indicate the session ended.");
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_OnAuthStateChange_FiresOnRefresh()
        {
            _fake.Enqueue("auth_refresh", "{\"session_token\":\"s\",\"user\":{\"id\":\"u_2\"}}");
            Session captured = null;
            using var sub = _client.Auth.OnAuthStateChange(s => captured = s);

            await _client.Auth.RefreshAsync();

            Assert.That(captured, Is.Not.Null);
            Assert.That((string)captured.User["id"], Is.EqualTo("u_2"));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_OnAuthStateChange_DisposeUnsubscribes()
        {
            _fake.Enqueue("auth_sign_in_with_email", "{\"user\":{\"id\":\"u_1\"}}");
            _fake.Enqueue("auth_sign_in_with_email", "{\"user\":{\"id\":\"u_1\"}}");
            int callCount = 0;
            var sub = _client.Auth.OnAuthStateChange(_ => callCount++);

            await _client.Auth.SignInWithEmailAsync("a@x.com", "pw");
            Assert.That(callCount, Is.EqualTo(1));

            sub.Dispose();
            await _client.Auth.SignInWithEmailAsync("a@x.com", "pw");
            Assert.That(callCount, Is.EqualTo(1), "Disposed subscription should not fire again.");
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_OnAuthStateChange_HandlerThrowing_DoesNotBreakOthers()
        {
            _fake.Enqueue("auth_sign_in_with_email", "{\"user\":{\"id\":\"u_1\"}}");
            int goodFired = 0;
            using var sub1 = _client.Auth.OnAuthStateChange(_ => throw new Exception("bad subscriber"));
            using var sub2 = _client.Auth.OnAuthStateChange(_ => goodFired++);

            await _client.Auth.SignInWithEmailAsync("a@x.com", "pw");

            Assert.That(goodFired, Is.EqualTo(1),
                "A throwing handler must not prevent later handlers from being notified.");
        }

        [Test]
        public void Auth_OnAuthStateChange_RejectsNullHandler()
        {
            Assert.Throws<ArgumentNullException>(() => _client.Auth.OnAuthStateChange(null));
        }

        [Test]
        public async System.Threading.Tasks.Task Auth_SubscribersIsolatedAcrossClients()
        {
            var b1 = new FakeNativeMethods();
            var b2 = new FakeNativeMethods();
            b1.Enqueue("auth_sign_in_with_email", "{\"user\":{\"id\":\"u_b1\"}}");

            var c1 = new AmbaClient(b1);
            var c2 = new AmbaClient(b2);

            int c1Hits = 0, c2Hits = 0;
            using var s1 = c1.Auth.OnAuthStateChange(_ => c1Hits++);
            using var s2 = c2.Auth.OnAuthStateChange(_ => c2Hits++);

            await c1.Auth.SignInWithEmailAsync("a@x.com", "pw");

            Assert.That(c1Hits, Is.EqualTo(1));
            Assert.That(c2Hits, Is.EqualTo(0),
                "Subscribers on one client must not leak to another.");
        }

        // ── AmbaApiError (Phase-A typed error class) ─────────────────

        [Test]
        public void AmbaApiError_IsAnAmbaException_ForBackCompat()
        {
            // AmbaApiError extends AmbaException so existing catch blocks
            // written against the older type still work. New code branches
            // on the typed Code instead.
            var e = new AmbaApiError("RATE_LIMITED", "slow down");
            Assert.That(e, Is.InstanceOf<AmbaException>());
            Assert.That(e.Code, Is.EqualTo("RATE_LIMITED"));
            Assert.That(e.Message, Is.EqualTo("slow down"));
            Assert.That(e.Details, Is.Null);
        }

        [Test]
        public void AmbaApiError_CodeFromMessage_RecognizesStandardPrefixes()
        {
            Assert.That(AmbaApiError.CodeFromMessage("Unauthorized: token expired"), Is.EqualTo("UNAUTHORIZED"));
            Assert.That(AmbaApiError.CodeFromMessage("Forbidden"), Is.EqualTo("FORBIDDEN"));
            Assert.That(AmbaApiError.CodeFromMessage("Not found: user u_1"), Is.EqualTo("NOT_FOUND"));
            Assert.That(AmbaApiError.CodeFromMessage("Conflict on insert"), Is.EqualTo("CONFLICT"));
            Assert.That(AmbaApiError.CodeFromMessage("Rate limited"), Is.EqualTo("RATE_LIMITED"));
            Assert.That(AmbaApiError.CodeFromMessage("Validation error: field foo"), Is.EqualTo("VALIDATION_ERROR"));
            Assert.That(AmbaApiError.CodeFromMessage("Network error"), Is.EqualTo("NETWORK_ERROR"));
            Assert.That(AmbaApiError.CodeFromMessage("HTTP error: 502"), Is.EqualTo("HTTP_ERROR"));
            Assert.That(AmbaApiError.CodeFromMessage("Circuit breaker open"), Is.EqualTo("CIRCUIT_OPEN"));
            Assert.That(AmbaApiError.CodeFromMessage("Consent not granted"), Is.EqualTo("CONSENT_NOT_GRANTED"));
            Assert.That(AmbaApiError.CodeFromMessage("amba not initialized"), Is.EqualTo("NOT_INITIALIZED"));
            Assert.That(AmbaApiError.CodeFromMessage("Invalid configuration"), Is.EqualTo("INVALID_CONFIG"));
            Assert.That(AmbaApiError.CodeFromMessage("Invalid argument: bar"), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(AmbaApiError.CodeFromMessage(null), Is.EqualTo("UNKNOWN_ERROR"));
            Assert.That(AmbaApiError.CodeFromMessage(""), Is.EqualTo("UNKNOWN_ERROR"));
            Assert.That(AmbaApiError.CodeFromMessage("Mystery"), Is.EqualTo("UNKNOWN_ERROR"));
        }

        [Test]
        public void AmbaApiError_From_PreservesExistingApiError()
        {
            var original = new AmbaApiError("FORBIDDEN", "no access");
            Assert.That(AmbaApiError.From(original), Is.SameAs(original),
                "Idempotent: From should not re-wrap an existing AmbaApiError.");
        }

        [Test]
        public void AmbaApiError_From_WrapsGenericException()
        {
            var wrapped = AmbaApiError.From(new Exception("Rate limited again"));
            Assert.That(wrapped.Code, Is.EqualTo("RATE_LIMITED"));
            Assert.That(wrapped.Message, Is.EqualTo("Rate limited again"));
        }

        [Test]
        public void MaybeThrow_UsesExplicitCodeWhenServerSendsOne()
        {
            // When the server emits a structured error envelope with
            // `code` + `details`, the wrapper preserves both rather than
            // falling back to message-prefix inference.
            var raw = "{\"error\":\"something\",\"code\":\"CUSTOM_CODE\",\"details\":{\"field\":\"x\"}}";
            var ex = Assert.Throws<AmbaApiError>(() => JsonUtil.MaybeThrow(raw));
            Assert.That(ex.Code, Is.EqualTo("CUSTOM_CODE"));
            Assert.That(ex.Message, Is.EqualTo("something"));
            Assert.That(ex.Details, Is.Not.Null);
        }

        [Test]
        public void ThrowFromRaw_TagsOpaqueBodyAsUnknown()
        {
            // Non-JSON / no `error` field → wrap whole payload as the
            // message with UNKNOWN_ERROR.
            var ex = Assert.Throws<AmbaApiError>(() => JsonUtil.ThrowFromRaw("totally opaque"));
            Assert.That(ex.Code, Is.EqualTo("UNKNOWN_ERROR"));
            Assert.That(ex.Message, Is.EqualTo("totally opaque"));
        }

        // ── content (Phase-A C-ABI changes) ──────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Content_Today_PassesNullChannelWhenOmitted()
        {
            // Phase A: null channel pointer = "default" server-side. The
            // wrapper should marshal as null IntPtr rather than building a
            // c-string just to hold the literal.
            _fake.Enqueue("content_get_today", "{\"data\":{}}");

            await _client.Content.TodayAsync();

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.Null, "channel should marshal as null IntPtr");
        }

        [Test]
        public async System.Threading.Tasks.Task Content_Today_ForwardsExplicitChannel()
        {
            _fake.Enqueue("content_get_today", "{\"data\":{}}");

            await _client.Content.TodayAsync("breaking_news");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("breaking_news"));
        }

        [Test]
        public async System.Threading.Tasks.Task Content_Library_DefaultsLimitToZeroAndCursorToNull()
        {
            // Phase A C-ABI: `amba_content_get_library(channel, limit:u32, cursor)`.
            // The Rust core treats `limit == 0` as "no limit". The wrapper
            // marshals null channel + null cursor as null IntPtrs.
            _fake.Enqueue("content_get_library", "{\"data\":[]}");

            await _client.Content.LibraryAsync();

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.Null);
            Assert.That(call.Args[1], Is.EqualTo("0"), "limit=0 means 'no limit'");
            Assert.That(call.Args[2], Is.Null);
        }

        [Test]
        public async System.Threading.Tasks.Task Content_Library_ForwardsLimitAndCursor()
        {
            _fake.Enqueue("content_get_library", "{\"data\":[]}");

            await _client.Content.LibraryAsync("news", limit: 25, cursor: "page_2");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("news"));
            Assert.That(call.Args[1], Is.EqualTo("25"));
            Assert.That(call.Args[2], Is.EqualTo("page_2"));
        }

        [Test]
        public async System.Threading.Tasks.Task Content_Library_NegativeLimitMapsToZero()
        {
            // Callers that previously used `-1` for "no limit" (matching
            // the leaderboards/xp helper convention) should still get the
            // "no limit" semantic via the u32 ABI's 0 sentinel.
            _fake.Enqueue("content_get_library", "{\"data\":[]}");

            await _client.Content.LibraryAsync("news", limit: -1);

            Assert.That(_fake.Calls[0].Args[1], Is.EqualTo("0"));
        }

        // ── Typed models: Streak, XpBalance ──────────────────────────

        [Test]
        public void Streak_Decodes_Status_And_FreezesRemaining()
        {
            // Phase-A added two fields the server now emits.
            var json = @"{
                ""id"": ""s_1"",
                ""key"": ""daily_login"",
                ""name"": ""Daily Login"",
                ""current_length"": 7,
                ""longest_length"": 30,
                ""last_qualified_on"": ""2026-05-12"",
                ""updated_at"": ""2026-05-13T00:00:00Z"",
                ""status"": ""qualified_today"",
                ""freezes_remaining"": 2
            }";
            var s = JToken.Parse(json).ToObject<Streak>();
            Assert.That(s.Id, Is.EqualTo("s_1"));
            Assert.That(s.Key, Is.EqualTo("daily_login"));
            Assert.That(s.CurrentLength, Is.EqualTo(7u));
            Assert.That(s.LongestLength, Is.EqualTo(30u));
            Assert.That(s.Status, Is.EqualTo("qualified_today"));
            Assert.That(s.FreezesRemaining, Is.EqualTo(2u));
        }

        [Test]
        public void Streak_DefaultsToZero_AgainstOlderServer()
        {
            // Servers that don't yet emit `status` / `freezes_remaining`
            // should decode without throwing — defaults are empty string + 0.
            var json = @"{
                ""id"": ""s_1"",
                ""key"": ""daily_login"",
                ""name"": ""Daily Login"",
                ""current_length"": 3,
                ""longest_length"": 9,
                ""updated_at"": ""2026-05-13T00:00:00Z""
            }";
            var s = JToken.Parse(json).ToObject<Streak>();
            Assert.That(s.Status, Is.Null.Or.Empty);
            Assert.That(s.FreezesRemaining, Is.EqualTo(0u));
        }

        [Test]
        public void XpBalance_Decodes_XpThisPeriod()
        {
            var json = @"{
                ""user_id"": ""u_1"",
                ""total_xp"": 12345,
                ""current_level"": 5,
                ""xp_into_level"": 200,
                ""xp_to_next_level"": 800,
                ""updated_at"": ""2026-05-13T00:00:00Z"",
                ""xp_this_period"": 450
            }";
            var x = JToken.Parse(json).ToObject<XpBalance>();
            Assert.That(x.UserId, Is.EqualTo("u_1"));
            Assert.That(x.TotalXp, Is.EqualTo(12345L));
            Assert.That(x.CurrentLevel, Is.EqualTo(5u));
            Assert.That(x.XpThisPeriod, Is.EqualTo(450L));
        }

        [Test]
        public void XpBalance_DefaultsToZero_AgainstOlderServer()
        {
            var json = @"{
                ""user_id"": ""u_1"",
                ""total_xp"": 0,
                ""current_level"": 1,
                ""xp_into_level"": 0,
                ""xp_to_next_level"": 100,
                ""updated_at"": ""2026-05-13T00:00:00Z""
            }";
            var x = JToken.Parse(json).ToObject<XpBalance>();
            Assert.That(x.XpThisPeriod, Is.EqualTo(0L));
        }

        // ── JsonUtil helpers ─────────────────────────────────────────

        [Test]
        public void MaybeThrow_ExtractsErrorFieldFromObject()
        {
            var ex = Assert.Throws<AmbaApiError>(() =>
                JsonUtil.MaybeThrow("{\"error\":\"server exploded\"}"));
            Assert.That(ex.Message, Is.EqualTo("server exploded"));
            // Unknown prefix → UNKNOWN_ERROR.
            Assert.That(ex.Code, Is.EqualTo("UNKNOWN_ERROR"));
        }

        [Test]
        public void MaybeThrow_DoesNotThrowOnMalformedJson()
        {
            Assert.DoesNotThrow(() => JsonUtil.MaybeThrow("not json {"));
        }

        [Test]
        public void MaybeThrow_DoesNotThrowOnObjectWithoutErrorField()
        {
            Assert.DoesNotThrow(() => JsonUtil.MaybeThrow("{\"id\":\"u_1\"}"));
        }

        [Test]
        public void MaybeThrow_DoesNotThrowOnJsonArray()
        {
            Assert.DoesNotThrow(() => JsonUtil.MaybeThrow("[1,2,3]"));
        }

        [Test]
        public void DecodeError_ExtractsErrorField()
        {
            Assert.That(
                JsonUtil.DecodeError("{\"error\":\"bad request\"}"),
                Is.EqualTo("bad request"));
        }

        [Test]
        public void DecodeError_FallsBackToRawOnMalformedJson()
        {
            Assert.That(
                JsonUtil.DecodeError("totally not json"),
                Is.EqualTo("totally not json"));
        }

        [Test]
        public void DecodeError_FallsBackToRawOnObjectWithoutErrorField()
        {
            Assert.That(
                JsonUtil.DecodeError("{\"detail\":\"oops\"}"),
                Is.EqualTo("{\"detail\":\"oops\"}"));
        }

        // ═══════════════════════════════════════════════════════════════
        // Constructor DI isolation guarantees
        //
        // The whole point of the Stripe / OkHttp / URLSession pattern is
        // that *two clients constructed with two different bindings see
        // no shared state*. The wrapper has zero hidden globals. These
        // tests exercise that explicitly — they will fail loudly if
        // anyone ever sneaks a static cache back in.
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public async System.Threading.Tasks.Task DI_TwoClients_DispatchIndependently()
        {
            var b1 = new FakeNativeMethods();
            var b2 = new FakeNativeMethods();
            b1.Enqueue("events_track", null);
            b2.Enqueue("events_track", null);

            var c1 = new AmbaClient(b1);
            var c2 = new AmbaClient(b2);

            await c1.Events.TrackAsync("on_c1");
            await c2.Events.TrackAsync("on_c2");

            Assert.That(b1.Calls, Has.Count.EqualTo(1));
            Assert.That(b2.Calls, Has.Count.EqualTo(1));
            Assert.That(b1.Calls[0].Args[0], Is.EqualTo("on_c1"));
            Assert.That(b2.Calls[0].Args[0], Is.EqualTo("on_c2"));
        }

        [Test]
        public void DI_ClientBindings_ExposesInjectedInstance()
        {
            var b1 = new FakeNativeMethods();
            var b2 = new FakeNativeMethods();

            var c1 = new AmbaClient(b1);
            var c2 = new AmbaClient(b2);

            Assert.That(c1.Bindings, Is.SameAs(b1));
            Assert.That(c2.Bindings, Is.SameAs(b2));
            Assert.That(c1.Bindings, Is.Not.SameAs(c2.Bindings));
        }

        [Test]
        public async System.Threading.Tasks.Task DI_SingleClient_WiresAllNamespacesToSameBinding()
        {
            var b = new FakeNativeMethods();
            b.Enqueue("events_track", null);
            b.Enqueue("auth_refresh", "{\"session_token\":\"x\"}");
            b.Enqueue("collections_find", "{}");
            b.Enqueue("config_fetch", "{\"v\":1}");
            b.Enqueue("flags_fetch", "[]");

            var c = new AmbaClient(b);
            await c.Events.TrackAsync("e");
            await c.Auth.RefreshAsync();
            await c.Collections.FindAsync("coll");
            await c.Config.FetchAsync();
            await c.Flags.FetchAsync();

            // Every namespace fired exactly one call against the SAME b.
            Assert.That(b.Calls, Has.Count.EqualTo(5));
            Assert.That(b.Calls[0].Name, Is.EqualTo("events_track"));
            Assert.That(b.Calls[1].Name, Is.EqualTo("auth_refresh"));
            Assert.That(b.Calls[2].Name, Is.EqualTo("collections_find"));
            Assert.That(b.Calls[3].Name, Is.EqualTo("config_fetch"));
            Assert.That(b.Calls[4].Name, Is.EqualTo("flags_fetch"));
        }

        [Test]
        public async System.Threading.Tasks.Task DI_CallingOnOneClient_NeverTouchesOther()
        {
            var b1 = new FakeNativeMethods();
            var b2 = new FakeNativeMethods();
            b1.Enqueue("auth_refresh", "{\"session_token\":\"on_b1\"}");

            var c1 = new AmbaClient(b1);
            var c2 = new AmbaClient(b2);

            var r = await c1.Auth.RefreshAsync();
            Assert.That((string)r["session_token"], Is.EqualTo("on_b1"));

            Assert.That(b1.Calls, Has.Count.EqualTo(1));
            Assert.That(b2.Calls, Is.Empty, "c2's binding should be untouched");

            // And c2 still works on its own binding when actually exercised.
            b2.Enqueue("auth_refresh", "{\"session_token\":\"on_b2\"}");
            var r2 = await c2.Auth.RefreshAsync();
            Assert.That((string)r2["session_token"], Is.EqualTo("on_b2"));
        }

        [Test]
        public async System.Threading.Tasks.Task DI_InitializeAsync_OnOneDoesNotLeakToAnother()
        {
            var b1 = new FakeNativeMethods();
            var b2 = new FakeNativeMethods();
            b1.Enqueue("init", null);
            b2.Enqueue("init", null);

            var c1 = new AmbaClient(b1);
            var c2 = new AmbaClient(b2);

            await c1.InitializeAsync("pk_one", baseUrl: "https://one");
            await c2.InitializeAsync("pk_two", baseUrl: "https://two");

            var d1 = JToken.Parse(b1.Calls[0].Args[0]);
            var d2 = JToken.Parse(b2.Calls[0].Args[0]);
            Assert.That((string)d1["api_key"], Is.EqualTo("pk_one"));
            Assert.That((string)d1["base_url"], Is.EqualTo("https://one"));
            Assert.That((string)d2["api_key"], Is.EqualTo("pk_two"));
            Assert.That((string)d2["base_url"], Is.EqualTo("https://two"));
        }

        [Test]
        public void DI_NamespaceInstances_AreStableAcrossReads()
        {
            // Customers may capture client.Events and reuse it. The
            // reference must be the same instance each time (no
            // per-access allocation).
            var e1 = _client.Events;
            var e2 = _client.Events;
            Assert.That(e1, Is.SameAs(e2));

            var a1 = _client.Ai.Anthropic.Messages;
            var a2 = _client.Ai.Anthropic.Messages;
            Assert.That(a1, Is.SameAs(a2));
        }

        [Test]
        public void DI_NamespaceInstances_OnDifferentClientsAreDistinct()
        {
            var c1 = new AmbaClient(new FakeNativeMethods());
            var c2 = new AmbaClient(new FakeNativeMethods());

            Assert.That(c1.Events, Is.Not.SameAs(c2.Events));
            Assert.That(c1.Auth, Is.Not.SameAs(c2.Auth));
            Assert.That(c1.Collections, Is.Not.SameAs(c2.Collections));
        }

        // ── diagnostics ──────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task DiagnosticsPing_DecodesHappyPathEnvelope()
        {
            _fake.Enqueue(
                "diagnostics_ping",
                "{\"ok\":true,\"server_project_id\":\"proj_abc\"," +
                "\"environment\":\"sandbox\",\"key_fingerprint\":\"4f8a\"," +
                "\"latency_ms\":73,\"error\":null}");

            var result = await _client.Diagnostics.PingAsync();

            Assert.That(result.Ok, Is.True);
            Assert.That(result.ServerProjectId, Is.EqualTo("proj_abc"));
            Assert.That(result.Environment, Is.EqualTo("sandbox"));
            Assert.That(result.KeyFingerprint, Is.EqualTo("4f8a"));
            Assert.That(result.LatencyMs, Is.EqualTo(73));
            Assert.That(result.Error, Is.Null);

            // Wrapper dispatched to the single native symbol with no args.
            Assert.That(_fake.Calls, Has.Count.EqualTo(1));
            Assert.That(_fake.Calls[0].Name, Is.EqualTo("diagnostics_ping"));
            Assert.That(_fake.Calls[0].Args, Is.Empty);
        }

        [Test]
        public async System.Threading.Tasks.Task DiagnosticsPing_DecodesServerSideFailureEnvelope()
        {
            // Wire shape: 200 OK with ok=false + populated error code.
            // The wrapper must surface as a structured PingResult, NOT throw.
            _fake.Enqueue(
                "diagnostics_ping",
                "{\"ok\":false,\"server_project_id\":\"proj_abc\"," +
                "\"environment\":null,\"key_fingerprint\":\"4f8a\"," +
                "\"latency_ms\":4,\"error\":\"DIAGNOSTICS_INTERNAL_ERROR\"}");

            var result = await _client.Diagnostics.PingAsync();

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.EqualTo("DIAGNOSTICS_INTERNAL_ERROR"));
            Assert.That(result.Environment, Is.Null,
                "environment should round-trip null for non-ok envelopes");
            Assert.That(result.KeyFingerprint, Is.EqualTo("4f8a"));
            Assert.That(result.LatencyMs, Is.EqualTo(4));
        }

        [Test]
        public void DiagnosticsPing_ThrowsOnTransportErrorEnvelope()
        {
            // Rust core surfaces 401 as `{"error": "Unauthorized..."}`
            // via `err_json(&e.to_string())`. The wrapper must throw
            // AmbaApiError with code UNAUTHORIZED, NOT decode silently
            // into a default PingResult.
            _fake.Enqueue(
                "diagnostics_ping",
                "{\"error\":\"Unauthorized: invalid api key\"}");

            var ex = Assert.ThrowsAsync<AmbaApiError>(async () =>
                await _client.Diagnostics.PingAsync());
            Assert.That(ex.Code, Is.EqualTo("UNAUTHORIZED"));
            Assert.That(ex.Message, Is.EqualTo("Unauthorized: invalid api key"));
        }

        [Test]
        public void DiagnosticsPing_PingAliasIsTheSameAsPingAsync()
        {
            // Public surface offered as `Diagnostics.Ping()` (brief) AND
            // `Diagnostics.PingAsync()` (in-house async-suffix convention).
            // Both must dispatch identically.
            _fake.Enqueue(
                "diagnostics_ping",
                "{\"ok\":true,\"server_project_id\":\"p\"," +
                "\"environment\":\"production\",\"key_fingerprint\":\"abcd\"," +
                "\"latency_ms\":1,\"error\":null}");

            var task = _client.Diagnostics.Ping();
            Assert.That(task, Is.Not.Null, "Ping() returns a Task<PingResult>");
            Assert.That(task.Result.Ok, Is.True);
            Assert.That(_fake.Calls, Has.Count.EqualTo(1));
            Assert.That(_fake.Calls[0].Name, Is.EqualTo("diagnostics_ping"));
        }

        [Test]
        public async System.Threading.Tasks.Task DiagnosticsPing_RoundTripsPingResultThroughJson()
        {
            // Spec self-test (per polyfill-spec-test rule): build a
            // canonical PingResult on the wire, decode, re-encode, and
            // confirm the byte-shape matches the on-wire convention.
            // Locks the JsonProperty wire-name contract — a future field
            // rename would silently break server-side comparison
            // ("server_project_id" → "serverProjectId" is a subtle 4am bug).
            var wire = "{\"ok\":true,\"server_project_id\":\"proj_xyz\"," +
                       "\"environment\":\"staging\",\"key_fingerprint\":\"deef\"," +
                       "\"latency_ms\":42,\"error\":null}";
            _fake.Enqueue("diagnostics_ping", wire);

            var decoded = await _client.Diagnostics.PingAsync();
            var reencoded = Newtonsoft.Json.JsonConvert.SerializeObject(decoded);

            // Compare normalized JTokens — field ordering is not stable
            // across Newtonsoft versions, but the (key, value) set must
            // match exactly.
            var original = JToken.Parse(wire);
            var reparsed = JToken.Parse(reencoded);
            Assert.That(JToken.DeepEquals(original, reparsed), Is.True,
                $"round-trip changed wire shape: original={original} re-encoded={reparsed}");
        }

        [Test]
        public void Amba_Static_Diagnostics_ThrowsWhenNotConfigured()
        {
            // Static facade discipline: Amba.Diagnostics must require
            // ConfigureAsync just like every other Amba.* namespace.
            Assert.Throws<InvalidOperationException>(() =>
            {
                var _ = Amba.Diagnostics;
            });
        }

        // ── messaging: 4.0 — createConversation / listMessages /
        //                getMessage (fix) / markRead ──────────────────

        [Test]
        public async System.Threading.Tasks.Task Messaging_CreateConversation_SerializesParticipantsAndMetadata()
        {
            _fake.Enqueue("messaging_create_conversation", "{\"id\":\"conv_1\"}");

            var result = await _client.Messaging.CreateConversationAsync(
                new[] { "u_1", "u_2" },
                new Dictionary<string, object> { ["type"] = "direct", ["name"] = "alice-bob" });

            var call = _fake.Calls[0];
            Assert.That(call.Name, Is.EqualTo("messaging_create_conversation"));
            var root = JToken.Parse(call.Args[0]);
            var participants = (JArray)root["participant_ids"];
            Assert.That(participants.Count, Is.EqualTo(2));
            Assert.That((string)participants[0], Is.EqualTo("u_1"));
            Assert.That((string)root["type"], Is.EqualTo("direct"));
            Assert.That((string)root["name"], Is.EqualTo("alice-bob"));
            Assert.That((string)result["id"], Is.EqualTo("conv_1"));
        }

        [Test]
        public void Messaging_CreateConversation_RejectsNullParticipants()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _client.Messaging.CreateConversationAsync(null));
        }

        [Test]
        public void Messaging_CreateConversation_RejectsParticipantIdsInMetadata()
        {
            // Cycle-2 BugBot finding: looping `metadata` into `body`
            // would let a `metadata["participant_ids"]` key silently
            // overwrite the typed `participants` argument. The participant
            // list is owned by the argument; conflict is a programming
            // error, not a silent overwrite.
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _client.Messaging.CreateConversationAsync(
                    new[] { "u_1", "u_2" },
                    new Dictionary<string, object> { ["participant_ids"] = new[] { "u_attacker" } }));
        }

        [Test]
        public async System.Threading.Tasks.Task Messaging_ListMessages_SendsSentinelWhenLimitOmitted()
        {
            _fake.Enqueue("messaging_list_messages", "{\"data\":[]}");

            await _client.Messaging.ListMessagesAsync("conv_1");

            var call = _fake.Calls[0];
            Assert.That(call.Args[0], Is.EqualTo("conv_1"));
            // u32::MAX == "not provided" sentinel per Phase A.
            Assert.That(call.Args[1], Is.EqualTo(uint.MaxValue.ToString()));
            Assert.That(call.Args[2], Is.EqualTo(uint.MaxValue.ToString()));
        }

        [Test]
        public async System.Threading.Tasks.Task Messaging_ListMessages_ForwardsLimitAndOffset()
        {
            _fake.Enqueue("messaging_list_messages", "{\"data\":[{\"id\":\"m_1\"}]}");

            await _client.Messaging.ListMessagesAsync("conv_1", limit: 25, offset: 50);

            var call = _fake.Calls[0];
            Assert.That(call.Args[1], Is.EqualTo("25"));
            Assert.That(call.Args[2], Is.EqualTo("50"));
        }

        [Test]
        public async System.Threading.Tasks.Task Messaging_GetMessage_PassesConversationIdAndMessageId()
        {
            // Phase A fix: the symbol now exists with a 2-arg signature
            // (conv_id, msg_id). Pre-4.0 the wrapper sent only `id` to
            // a missing symbol and threw EntryPointNotFoundException.
            _fake.Enqueue("messaging_get_message", "{\"id\":\"m_1\",\"body\":\"hi\"}");

            var result = await _client.Messaging.GetMessageAsync("conv_1", "m_1");

            var call = _fake.Calls[0];
            Assert.That(call.Name, Is.EqualTo("messaging_get_message"));
            Assert.That(call.Args[0], Is.EqualTo("conv_1"));
            Assert.That(call.Args[1], Is.EqualTo("m_1"));
            Assert.That((string)result["id"], Is.EqualTo("m_1"));
        }

        [Test]
        public async System.Threading.Tasks.Task Messaging_GetMessage_ReturnsNullJTokenWhenNotFound()
        {
            // The Rust core returns the literal `null` JSON when the
            // message isn't in the first 5,000 entries.
            _fake.Enqueue("messaging_get_message", "null");

            var result = await _client.Messaging.GetMessageAsync("conv_1", "m_missing");

            Assert.That(result.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public async System.Threading.Tasks.Task Messaging_MarkRead_ForwardsConversationId()
        {
            _fake.Enqueue("messaging_mark_read", null);

            await _client.Messaging.MarkReadAsync("conv_1");

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("messaging_mark_read"));
            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("conv_1"));
        }

        // ── friends: 4.0 — sendRequest / acceptRequest / declineRequest ──

        [Test]
        public async System.Threading.Tasks.Task Friends_SendRequest_ReturnsFriendship()
        {
            _fake.Enqueue("friends_send_request", "{\"id\":\"f_1\",\"status\":\"pending\"}");

            var result = await _client.Friends.SendRequestAsync("u_target");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("u_target"));
            Assert.That((string)result["status"], Is.EqualTo("pending"));
        }

        [Test]
        public async System.Threading.Tasks.Task Friends_AcceptRequest_ReturnsFriendship()
        {
            _fake.Enqueue("friends_accept_request", "{\"id\":\"f_1\",\"status\":\"accepted\"}");

            var result = await _client.Friends.AcceptRequestAsync("f_1");

            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("f_1"));
            Assert.That((string)result["status"], Is.EqualTo("accepted"));
        }

        [Test]
        public async System.Threading.Tasks.Task Friends_DeclineRequest_ForwardsFriendshipId()
        {
            _fake.Enqueue("friends_decline_request", null);

            await _client.Friends.DeclineRequestAsync("f_1");

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("friends_decline_request"));
            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("f_1"));
        }

        // ── catalog: 4.0 — get(id) ───────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Catalog_Get_ForwardsItemIdAndReturnsItem()
        {
            _fake.Enqueue("catalog_get", "{\"id\":\"sku_1\",\"name\":\"Coin Pack\"}");

            var result = await _client.Catalog.GetAsync("sku_1");

            Assert.That(_fake.Calls[0].Name, Is.EqualTo("catalog_get"));
            Assert.That(_fake.Calls[0].Args[0], Is.EqualTo("sku_1"));
            Assert.That((string)result["name"], Is.EqualTo("Coin Pack"));
        }

        // ── 4.0: version + namespace wiring ──────────────────────────

        [Test]
        public void Version_Is40()
        {
            // Coordinated 4.0 release across all 9 SDKs — pin the value
            // so a stray bump that doesn't match the canonical spec
            // breaks loudly in CI rather than silently shipping.
            Assert.That(Amba.Version, Is.EqualTo("4.0.0"));
        }

        [Test]
        public void NewNamespaces_AreWired()
        {
            // Smoke-test the 4 net-new namespaces resolve off both the
            // client and the static facade pattern (constructed via
            // AmbaClient here; the static facade test elsewhere covers
            // the InvalidOperationException path).
            Assert.That(_client.Users, Is.Not.Null);
            Assert.That(_client.Sessions, Is.Not.Null);
            Assert.That(_client.Sync, Is.Not.Null);
            Assert.That(_client.Leagues, Is.Not.Null);
        }

        [Test]
        public void NewNamespaces_AreStableAcrossReads()
        {
            // Same singleton contract as the existing namespace
            // instances (see DI_NamespaceInstances_AreStableAcrossReads).
            Assert.That(_client.Users, Is.SameAs(_client.Users));
            Assert.That(_client.Sessions, Is.SameAs(_client.Sessions));
            Assert.That(_client.Sync, Is.SameAs(_client.Sync));
            Assert.That(_client.Leagues, Is.SameAs(_client.Leagues));
        }
    }

    // ── Fake INativeMethods ──────────────────────────────────────────
    //
    // Records each call and returns a Rust-owned-style pointer for the
    // next response in the queue. The wrapper calls `amba_string_free`
    // on the pointer when done, which we map to `FreeHGlobal` since we
    // allocated it from the unmanaged heap.

    public sealed class FakeNativeMethods : INativeMethods
    {
        public readonly List<Call> Calls = new();
        public uint LastSetDebug { get; private set; }

        private readonly Queue<(string method, string response, bool nullPtr)> _responses = new();
        private readonly Dictionary<string, bool> _entitlements = new();
        private readonly Dictionary<string, bool> _permissions = new();
        private uint _isAuthenticated;

        public void Enqueue(string method, string response)
        {
            _responses.Enqueue((method, response, false));
        }

        /// <summary>
        /// Enqueue a response where the binding returns IntPtr.Zero rather
        /// than a non-null Rust-owned string. Used by tests that exercise
        /// the null-pointer code path (e.g. anonymousId before configure).
        /// </summary>
        public void EnqueueNullPtr(string method)
        {
            _responses.Enqueue((method, null, true));
        }

        public void SeedIsAuthenticated(bool value) => _isAuthenticated = value ? 1u : 0u;

        public void SeedEntitlementHas(string name, bool value) => _entitlements[name] = value;

        public void SeedRolesHasPermission(string name, bool value) => _permissions[name] = value;

        private IntPtr Respond(string expectedMethod, params string[] args)
        {
            Calls.Add(new Call(expectedMethod, args));

            if (_responses.Count == 0)
                throw new InvalidOperationException(
                    $"Fake: no enqueued response for `{expectedMethod}`. " +
                    "Did you forget to call Enqueue() in your test?");

            var (method, response, nullPtr) = _responses.Dequeue();
            if (method != expectedMethod)
                throw new InvalidOperationException(
                    $"Fake: enqueued response was for `{method}` but actual call was `{expectedMethod}`.");

            return nullPtr ? IntPtr.Zero : ToOwnedPtr(response);
        }

        private static IntPtr ToOwnedPtr(string s)
        {
            if (s == null) return IntPtr.Zero;
            byte[] utf8 = Encoding.UTF8.GetBytes(s + "\0");
            IntPtr p = Marshal.AllocHGlobal(utf8.Length);
            Marshal.Copy(utf8, 0, p, utf8.Length);
            return p;
        }

        private static string FromPtr(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            return Marshal.PtrToStringUTF8(p);
        }

        // ── INativeMethods impls ───────────────────────────────────

        public IntPtr amba_init(IntPtr configJson) => Respond("init", FromPtr(configJson));
        public IntPtr amba_reset() => Respond("reset");
        public IntPtr amba_anonymous_id() => Respond("anonymous_id");
        public IntPtr amba_app_user_id() => Respond("app_user_id");
        public uint amba_is_authenticated() => _isAuthenticated;
        public void amba_string_free(IntPtr ptr) => Marshal.FreeHGlobal(ptr);
        public void amba_set_debug(uint enabled) { LastSetDebug = enabled; }

        public IntPtr amba_events_track(IntPtr eventName, IntPtr propertiesJson) =>
            Respond("events_track", FromPtr(eventName), FromPtr(propertiesJson));

        public IntPtr amba_auth_sign_in_anonymously() => Respond("auth_sign_in_anonymously");
        public IntPtr amba_auth_sign_in_with_email(IntPtr email, IntPtr password) =>
            Respond("auth_sign_in_with_email", FromPtr(email), FromPtr(password));
        public IntPtr amba_auth_sign_up_with_email(IntPtr email, IntPtr password) =>
            Respond("auth_sign_up_with_email", FromPtr(email), FromPtr(password));
        public IntPtr amba_auth_sign_in_with_social(IntPtr provider, IntPtr idToken) =>
            Respond("auth_sign_in_with_social", FromPtr(provider), FromPtr(idToken));
        public IntPtr amba_auth_sign_out(uint rotateAnonymousId) =>
            Respond("auth_sign_out", rotateAnonymousId.ToString());
        public IntPtr amba_auth_refresh() => Respond("auth_refresh");
        public IntPtr amba_auth_me() => Respond("auth_me");

        public IntPtr amba_auth_request_email_otp(IntPtr email) =>
            Respond("auth_request_email_otp", FromPtr(email));
        public IntPtr amba_auth_verify_email_otp(IntPtr email, IntPtr code) =>
            Respond("auth_verify_email_otp", FromPtr(email), FromPtr(code));
        public IntPtr amba_auth_request_sms_otp(IntPtr phone) =>
            Respond("auth_request_sms_otp", FromPtr(phone));
        public IntPtr amba_auth_verify_sms_otp(IntPtr phone, IntPtr code) =>
            Respond("auth_verify_sms_otp", FromPtr(phone), FromPtr(code));

        public IntPtr amba_auth_request_magic_link(IntPtr email) =>
            Respond("auth_request_magic_link", FromPtr(email));
        public IntPtr amba_auth_verify_magic_link(IntPtr token) =>
            Respond("auth_verify_magic_link", FromPtr(token));
        public IntPtr amba_auth_link_account(IntPtr provider, IntPtr credential) =>
            Respond("auth_link_account", FromPtr(provider), FromPtr(credential));

        // ── users / sessions / sync / leagues (4.0) ──
        public IntPtr amba_users_get(IntPtr userId) => Respond("users_get", FromPtr(userId));
        public IntPtr amba_users_update(IntPtr userId, IntPtr patchJson) =>
            Respond("users_update", FromPtr(userId), FromPtr(patchJson));

        public IntPtr amba_sessions_list() => Respond("sessions_list");
        public IntPtr amba_sessions_revoke(IntPtr sessionId) =>
            Respond("sessions_revoke", FromPtr(sessionId));

        public IntPtr amba_sync_push_changes(IntPtr changesJson) =>
            Respond("sync_push_changes", FromPtr(changesJson));
        public IntPtr amba_sync_pull_changes(IntPtr sinceJson) =>
            Respond("sync_pull_changes", FromPtr(sinceJson));

        public IntPtr amba_leagues_me() => Respond("leagues_me");
        public IntPtr amba_leagues_cohort() => Respond("leagues_cohort");

        public IntPtr amba_collections_find(IntPtr collection, IntPtr optionsJson) =>
            Respond("collections_find", FromPtr(collection), FromPtr(optionsJson));
        public IntPtr amba_collections_find_one(IntPtr collection, IntPtr id) =>
            Respond("collections_find_one", FromPtr(collection), FromPtr(id));
        public IntPtr amba_collections_insert(IntPtr collection, IntPtr rowJson) =>
            Respond("collections_insert", FromPtr(collection), FromPtr(rowJson));
        public IntPtr amba_collections_update(IntPtr collection, IntPtr id, IntPtr setJson) =>
            Respond("collections_update", FromPtr(collection), FromPtr(id), FromPtr(setJson));
        public IntPtr amba_collections_delete(IntPtr collection, IntPtr id) =>
            Respond("collections_delete", FromPtr(collection), FromPtr(id));
        public IntPtr amba_collections_find_nearest(IntPtr collection, IntPtr optionsJson) =>
            Respond("collections_find_nearest", FromPtr(collection), FromPtr(optionsJson));
        public IntPtr amba_collections_count(IntPtr collection, IntPtr filterJson) =>
            Respond("collections_count", FromPtr(collection), FromPtr(filterJson));

        public IntPtr amba_storage_presign(IntPtr bucket, IntPtr filename, IntPtr mimeType, ulong sizeBytes, int retentionDays) =>
            Respond("storage_presign", FromPtr(bucket), FromPtr(filename), FromPtr(mimeType),
                sizeBytes.ToString(), retentionDays.ToString());
        public IntPtr amba_storage_commit(IntPtr uploadId, IntPtr assetId) =>
            Respond("storage_commit", FromPtr(uploadId), FromPtr(assetId));
        public IntPtr amba_storage_list(IntPtr prefix) => Respond("storage_list", FromPtr(prefix));
        public IntPtr amba_storage_delete(IntPtr assetId) => Respond("storage_delete", FromPtr(assetId));
        public IntPtr amba_storage_download(IntPtr assetId) => Respond("storage_download", FromPtr(assetId));

        public IntPtr amba_push_register(IntPtr token, IntPtr platform, IntPtr bundleId) =>
            Respond("push_register", FromPtr(token), FromPtr(platform), FromPtr(bundleId));
        public IntPtr amba_push_subscribe(IntPtr topic) =>
            Respond("push_subscribe", FromPtr(topic));
        public IntPtr amba_push_unregister(IntPtr token) =>
            Respond("push_unregister", FromPtr(token));
        public IntPtr amba_push_get_tokens() => Respond("push_get_tokens");
        public IntPtr amba_push_unsubscribe(IntPtr topic) =>
            Respond("push_unsubscribe", FromPtr(topic));

        public IntPtr amba_entitlements_list() => Respond("entitlements_list");
        public uint amba_entitlements_has(IntPtr name)
        {
            var n = FromPtr(name);
            Calls.Add(new Call("entitlements_has", new[] { n }));
            return _entitlements.TryGetValue(n, out var v) && v ? 1u : 0u;
        }

        public IntPtr amba_ai_anthropic_messages(IntPtr requestJson) =>
            Respond("ai_anthropic_messages", FromPtr(requestJson));

        public IntPtr amba_config_fetch() => Respond("config_fetch");
        public IntPtr amba_flags_fetch() => Respond("flags_fetch");

        // ── gamification ──
        public IntPtr amba_achievements_get_all() => Respond("achievements_get_all");
        public IntPtr amba_achievements_get_progress() => Respond("achievements_get_progress");

        public IntPtr amba_challenges_get_active() => Respond("challenges_get_active");
        public IntPtr amba_challenges_get(IntPtr id) => Respond("challenges_get", FromPtr(id));
        public IntPtr amba_challenges_get_progress(IntPtr id) => Respond("challenges_get_progress", FromPtr(id));
        public IntPtr amba_challenges_claim(IntPtr id) => Respond("challenges_claim", FromPtr(id));

        public IntPtr amba_currencies_get_balance() => Respond("currencies_get_balance");
        public IntPtr amba_currencies_get_transactions(IntPtr currencyKey) => Respond("currencies_get_transactions", FromPtr(currencyKey));

        public IntPtr amba_inventory_get_items() => Respond("inventory_get_items");
        public IntPtr amba_inventory_get_item(IntPtr id) => Respond("inventory_get_item", FromPtr(id));
        public IntPtr amba_inventory_purchase(IntPtr requestJson) => Respond("inventory_purchase", FromPtr(requestJson));
        public IntPtr amba_inventory_consume(IntPtr requestJson) => Respond("inventory_consume", FromPtr(requestJson));

        public IntPtr amba_leaderboards_get(IntPtr key) => Respond("leaderboards_get", FromPtr(key));
        public IntPtr amba_leaderboards_get_entries(IntPtr key, int limit) =>
            Respond("leaderboards_get_entries", FromPtr(key), limit.ToString());
        public IntPtr amba_leaderboards_get_my_rank(IntPtr key) => Respond("leaderboards_get_my_rank", FromPtr(key));

        public IntPtr amba_stores_list() => Respond("stores_list");
        public IntPtr amba_stores_get_purchase_options(IntPtr storeKey) => Respond("stores_get_purchase_options", FromPtr(storeKey));
        public IntPtr amba_stores_purchase(IntPtr storeKey, IntPtr purchaseOptionId, IntPtr receiptJson) =>
            Respond("stores_purchase", FromPtr(storeKey), FromPtr(purchaseOptionId), FromPtr(receiptJson));

        public IntPtr amba_xp_get_balance() => Respond("xp_get_balance");
        public IntPtr amba_xp_get_history(int limit) => Respond("xp_get_history", limit.ToString());
        public IntPtr amba_xp_claim(IntPtr grantKey) => Respond("xp_claim", FromPtr(grantKey));

        public IntPtr amba_streaks_get_all() => Respond("streaks_get_all");
        public IntPtr amba_streaks_qualify(IntPtr streakKey) => Respond("streaks_qualify", FromPtr(streakKey));

        // ── social ──
        public IntPtr amba_feeds_get_activity(IntPtr feed, IntPtr cursor) =>
            Respond("feeds_get_activity", FromPtr(feed), FromPtr(cursor));

        public IntPtr amba_friends_get_list() => Respond("friends_get_list");
        public IntPtr amba_friends_get_friends() => Respond("friends_get_friends");
        public IntPtr amba_friends_block_user(IntPtr userId) => Respond("friends_block_user", FromPtr(userId));
        public IntPtr amba_friends_unblock_user(IntPtr userId) => Respond("friends_unblock_user", FromPtr(userId));
        public IntPtr amba_friends_remove_block(IntPtr friendshipId) => Respond("friends_remove_block", FromPtr(friendshipId));
        public IntPtr amba_friends_send_request(IntPtr userId) => Respond("friends_send_request", FromPtr(userId));
        public IntPtr amba_friends_accept_request(IntPtr friendshipId) => Respond("friends_accept_request", FromPtr(friendshipId));
        public IntPtr amba_friends_decline_request(IntPtr friendshipId) => Respond("friends_decline_request", FromPtr(friendshipId));

        public IntPtr amba_groups_create(IntPtr paramsJson) => Respond("groups_create", FromPtr(paramsJson));
        public IntPtr amba_groups_get(IntPtr id) => Respond("groups_get", FromPtr(id));
        public IntPtr amba_groups_update(IntPtr id, IntPtr patchJson) => Respond("groups_update", FromPtr(id), FromPtr(patchJson));
        public IntPtr amba_groups_delete(IntPtr id) => Respond("groups_delete", FromPtr(id));
        public IntPtr amba_groups_get_members(IntPtr id) => Respond("groups_get_members", FromPtr(id));
        public IntPtr amba_groups_join(IntPtr id) => Respond("groups_join", FromPtr(id));
        public IntPtr amba_groups_leave(IntPtr id) => Respond("groups_leave", FromPtr(id));
        public IntPtr amba_groups_invite(IntPtr id, IntPtr userId) => Respond("groups_invite", FromPtr(id), FromPtr(userId));

        public IntPtr amba_messaging_get_conversations() => Respond("messaging_get_conversations");
        public IntPtr amba_messaging_create_conversation(IntPtr requestJson) =>
            Respond("messaging_create_conversation", FromPtr(requestJson));
        public IntPtr amba_messaging_list_messages(IntPtr conversationId, uint limit, uint offset) =>
            Respond("messaging_list_messages", FromPtr(conversationId), limit.ToString(), offset.ToString());
        public IntPtr amba_messaging_get_message(IntPtr conversationId, IntPtr messageId) =>
            Respond("messaging_get_message", FromPtr(conversationId), FromPtr(messageId));
        public IntPtr amba_messaging_mark_read(IntPtr conversationId) =>
            Respond("messaging_mark_read", FromPtr(conversationId));
        public IntPtr amba_messaging_send_message(IntPtr requestJson) => Respond("messaging_send_message", FromPtr(requestJson));

        public IntPtr amba_moderation_report_user(IntPtr requestJson) => Respond("moderation_report_user", FromPtr(requestJson));
        public IntPtr amba_moderation_report_content(IntPtr requestJson) => Respond("moderation_report_content", FromPtr(requestJson));
        public IntPtr amba_moderation_get_report_status(IntPtr id) => Respond("moderation_get_report_status", FromPtr(id));

        public IntPtr amba_reviews_list(IntPtr targetType, IntPtr targetId) => Respond("reviews_list", FromPtr(targetType), FromPtr(targetId));
        public IntPtr amba_reviews_create(IntPtr paramsJson) => Respond("reviews_create", FromPtr(paramsJson));
        public IntPtr amba_reviews_update(IntPtr id, IntPtr patchJson) => Respond("reviews_update", FromPtr(id), FromPtr(patchJson));
        public IntPtr amba_reviews_delete(IntPtr id) => Respond("reviews_delete", FromPtr(id));

        public IntPtr amba_roles_get_my_roles() => Respond("roles_get_my_roles");
        public uint amba_roles_has_permission(IntPtr permission)
        {
            var p = FromPtr(permission);
            Calls.Add(new Call("roles_has_permission", new[] { p }));
            return _permissions.TryGetValue(p, out var v) && v ? 1u : 0u;
        }

        public IntPtr amba_referrals_get_referral_code() => Respond("referrals_get_referral_code");
        public IntPtr amba_referrals_claim_referral(IntPtr code) => Respond("referrals_claim_referral", FromPtr(code));
        public IntPtr amba_referrals_create(IntPtr code, int maxUses) =>
            Respond("referrals_create", FromPtr(code), maxUses.ToString());

        // ── lifecycle ──
        public IntPtr amba_catalog_list() => Respond("catalog_list");
        public IntPtr amba_catalog_get(IntPtr itemId) => Respond("catalog_get", FromPtr(itemId));

        public IntPtr amba_content_get_today(IntPtr channel) => Respond("content_get_today", FromPtr(channel));
        public IntPtr amba_content_get_library(IntPtr channel, uint limit, IntPtr cursor) =>
            Respond("content_get_library", FromPtr(channel), limit.ToString(), FromPtr(cursor));
        public IntPtr amba_content_get_item(IntPtr id) => Respond("content_get_item", FromPtr(id));
        public IntPtr amba_content_update_item(IntPtr id, IntPtr stateJson) =>
            Respond("content_update_item", FromPtr(id), FromPtr(stateJson));
        public IntPtr amba_content_create_item(IntPtr channel, IntPtr itemJson) =>
            Respond("content_create_item", FromPtr(channel), FromPtr(itemJson));

        public IntPtr amba_deep_links_get(IntPtr shortCode) => Respond("deep_links_get", FromPtr(shortCode));
        public IntPtr amba_deep_links_create(IntPtr paramsJson) => Respond("deep_links_create", FromPtr(paramsJson));

        public IntPtr amba_onboarding_get_status() => Respond("onboarding_get_status");
        public IntPtr amba_onboarding_next_step(IntPtr payloadJson) => Respond("onboarding_next_step", FromPtr(payloadJson));
        public IntPtr amba_onboarding_skip_step() => Respond("onboarding_skip_step");
        public IntPtr amba_onboarding_complete() => Respond("onboarding_complete");

        // ── diagnostics ──
        public IntPtr amba_diagnostics_ping() => Respond("diagnostics_ping");
    }

    public readonly struct Call
    {
        public readonly string Name;
        public readonly string[] Args;

        public Call(string name, string[] args)
        {
            Name = name;
            Args = args ?? Array.Empty<string>();
        }
    }
}
