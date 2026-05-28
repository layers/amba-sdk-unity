// Amba.cs — Unity / C# SDK for amba.
//
// Uses P/Invoke to bridge to the Rust core's C-ABI (`core/src/ffi.rs`).
// Native libraries ship in `Plugins/<target>/` per platform:
//   - iOS:     Plugins/iOS/libamba_core.a (statically linked)
//   - Android: Plugins/Android/<abi>/libamba_core.so
//   - macOS:   Plugins/macOS/libamba_core.dylib
//   - Windows: Plugins/Windows/x86_64/amba_core.dll
//   - Linux:   Plugins/Linux/x86_64/libamba_core.so
//
// WebGL is NOT supported. P/Invoke is unavailable on WebGL builds
// (emscripten only exposes native symbols that were compiled in at
// link time; the Rust core is not compiled into the WebGL build).
// Attempting to use this SDK in a WebGL build triggers a compile-time
// error below.
//
// ── Architecture: Constructor DI ─────────────────────────────────────
//
// `INativeMethods` is the FFI surface — a public interface whose default
// implementation (`DefaultNativeMethods`) forwards every method to the
// matching `[DllImport]` stub in `NativeMethods`. Anything that needs
// to call the native library accepts an `INativeMethods` via constructor
// injection. There are no hidden globals, no `NativeBridge.Current`-
// style mutation hooks, no statics that mutate at runtime — only the
// customer-friendly singleton inside `Amba` that gets populated by
// `Amba.ConfigureAsync`.
//
// Tests construct an `AmbaClient` directly with a fake:
//
//   var bindings = new FakeNativeMethods();
//   var client = new AmbaClient(bindings);
//   await client.Events.TrackAsync("test");
//
// They never touch `Amba.*` static state. This is the same pattern
// Stripe / OkHttp / URLSession / AWS SDK use.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Grants the test assemblies access to internal helpers (`JsonUtil`,
// `NativeUtil`) that tests assert against directly. This is the ONLY
// remaining test-visibility hook — there's no internal mutable state
// to inject; tests construct `AmbaClient` with a fake binding instead.
[assembly: InternalsVisibleTo("Amba.Tests.Editor")]
[assembly: InternalsVisibleTo("Amba.Tests")]

namespace Layers.Amba
{
    // ═══════════════════════════════════════════════════════════════
    // AmbaClient — the injectable, testable entry point
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// An amba SDK client wired against a specific <see cref="INativeMethods"/>.
    ///
    /// Customers normally don't construct this directly — <see cref="Amba.ConfigureAsync"/>
    /// wires a singleton instance for the static convenience API. Tests
    /// and advanced consumers (custom transports, multi-tenant apps with
    /// distinct API keys) construct <see cref="AmbaClient"/> directly.
    /// </summary>
    public class AmbaClient
    {
        /// <summary>FFI surface. Public so consumers can implement their own.</summary>
        public INativeMethods Bindings { get; }

        public EventsNamespace Events { get; }
        public AuthNamespace Auth { get; }
        public UsersNamespace Users { get; }
        public SessionsNamespace Sessions { get; }
        public SyncNamespace Sync { get; }
        public CollectionsNamespace Collections { get; }
        public StorageNamespace Storage { get; }
        public PushNamespace Push { get; }
        public EntitlementsNamespace Entitlements { get; }
        public AiNamespace Ai { get; }
        public ConfigNamespace Config { get; }
        public FlagsNamespace Flags { get; }

        // ── gamification ──
        public AchievementsNamespace Achievements { get; }
        public ChallengesNamespace Challenges { get; }
        public CurrenciesNamespace Currencies { get; }
        public InventoryNamespace Inventory { get; }
        public LeaderboardsNamespace Leaderboards { get; }
        public LeaguesNamespace Leagues { get; }
        public StoresNamespace Stores { get; }
        public XpNamespace Xp { get; }
        public StreaksNamespace Streaks { get; }

        // ── social ──
        public FeedsNamespace Feeds { get; }
        public FriendsNamespace Friends { get; }
        public GroupsNamespace Groups { get; }
        public MessagingNamespace Messaging { get; }
        public ModerationNamespace Moderation { get; }
        public ReviewsNamespace Reviews { get; }
        public RolesNamespace Roles { get; }
        public ReferralsNamespace Referrals { get; }

        // ── lifecycle ──
        public CatalogNamespace Catalog { get; }
        public ContentNamespace Content { get; }
        public DeepLinksNamespace DeepLinks { get; }
        public OnboardingNamespace Onboarding { get; }

        // ── diagnostics ──
        public DiagnosticsNamespace Diagnostics { get; }

        /// <summary>
        /// Construct a client backed by <paramref name="bindings"/>. Every
        /// namespace below shares the same binding instance; isolation
        /// between clients is guaranteed because each gets its own
        /// namespaces wired against its own bindings.
        /// </summary>
        public AmbaClient(INativeMethods bindings)
        {
            Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            Events = new EventsNamespace(bindings);
            Auth = new AuthNamespace(bindings);
            Users = new UsersNamespace(bindings);
            Sessions = new SessionsNamespace(bindings);
            Sync = new SyncNamespace(bindings);
            Collections = new CollectionsNamespace(bindings);
            Storage = new StorageNamespace(bindings);
            Push = new PushNamespace(bindings);
            Entitlements = new EntitlementsNamespace(bindings);
            Ai = new AiNamespace(bindings);
            Config = new ConfigNamespace(bindings);
            Flags = new FlagsNamespace(bindings);

            Achievements = new AchievementsNamespace(bindings);
            Challenges = new ChallengesNamespace(bindings);
            Currencies = new CurrenciesNamespace(bindings);
            Inventory = new InventoryNamespace(bindings);
            Leaderboards = new LeaderboardsNamespace(bindings);
            Leagues = new LeaguesNamespace(bindings);
            Stores = new StoresNamespace(bindings);
            Xp = new XpNamespace(bindings);
            Streaks = new StreaksNamespace(bindings);

            Feeds = new FeedsNamespace(bindings);
            Friends = new FriendsNamespace(bindings);
            Groups = new GroupsNamespace(bindings);
            Messaging = new MessagingNamespace(bindings);
            Moderation = new ModerationNamespace(bindings);
            Reviews = new ReviewsNamespace(bindings);
            Roles = new RolesNamespace(bindings);
            Referrals = new ReferralsNamespace(bindings);

            Catalog = new CatalogNamespace(bindings);
            Content = new ContentNamespace(bindings);
            DeepLinks = new DeepLinksNamespace(bindings);
            Onboarding = new OnboardingNamespace(bindings);

            Diagnostics = new DiagnosticsNamespace(bindings);
        }

        /// <summary>Stable per-install anonymous identifier.</summary>
        public string AnonymousId
        {
            get
            {
                var raw = NativeUtil.CallReturnString(Bindings.amba_anonymous_id, Bindings);
                if (raw == null) throw new InvalidOperationException("amba not configured");
                JsonUtil.MaybeThrow(raw);
                return raw;
            }
        }

        /// <summary>Authenticated user id, if a session is live.</summary>
        public string AppUserId => NativeUtil.CallReturnString(Bindings.amba_app_user_id, Bindings);

        /// <summary>Whether a session token is currently held.</summary>
        public bool IsAuthenticated => Bindings.amba_is_authenticated() != 0;

        /// <summary>Toggle engine-side debug logging.</summary>
        public void SetDebug(bool enabled) => Bindings.amba_set_debug(enabled ? 1u : 0u);

        /// <summary>
        /// Initialize the underlying Rust core against this client's bindings.
        ///
        /// Production code reaches this via <see cref="Amba.ConfigureAsync"/>, which
        /// constructs real bindings (<see cref="DefaultNativeMethods"/>) and then
        /// calls into here. Tests can call this directly on a client wired with
        /// a fake binding to verify the config JSON shape without loading the
        /// native library.
        /// </summary>
        public Task InitializeAsync(string apiKey, string baseUrl = null, bool consentRequired = false, bool debug = false)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentException("apiKey must not be empty", nameof(apiKey));

            var config = new Dictionary<string, object>
            {
                ["api_key"] = apiKey,
                ["sdk_platform"] = "csharp",
                ["sdk_wrapper_version"] = $"amba-csharp/{Amba.Version}",
                ["consent_required"] = consentRequired,
                ["debug"] = debug,
            };
            if (!string.IsNullOrEmpty(baseUrl)) config["base_url"] = baseUrl;

            var result = NativeUtil.Invoke(p => Bindings.amba_init(p), JsonConvert.SerializeObject(config), Bindings);
            if (result != null) JsonUtil.ThrowFromRaw(result);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Release the Rust core's singleton — clears the local session,
        /// rotates the anonymous id, and frees the core slot so a
        /// subsequent <see cref="InitializeAsync"/> can wire a fresh
        /// tenant. Maps to the C-ABI <c>amba_reset</c> symbol (Phase A).
        /// </summary>
        /// <remarks>
        /// Use this for multi-tenant apps that swap API keys at runtime,
        /// or for sign-out flows that want a clean slate. The Rust core
        /// logs a debug breadcrumb when in-flight FFI calls still hold
        /// the core (those finish on the pre-reset state).
        ///
        /// On a successful reset, <see cref="AuthNamespace.OnAuthStateChange"/>
        /// subscribers are notified with a <c>null</c> session — reset is
        /// documented as a sign-out path (and multi-tenant swap) so UI/handlers
        /// must transition out of the "signed in" state. Symmetric with
        /// <see cref="AuthNamespace.SignOutAsync"/>.
        /// </remarks>
        public Task ResetAsync()
        {
            var raw = NativeUtil.CallReturnString(Bindings.amba_reset, Bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            // Native call succeeded — the local session is gone. Tell
            // OnAuthStateChange subscribers so UI doesn't stay stuck on a
            // logged-in view. Mirrors SignOutAsync's notify-on-success.
            Auth.NotifyAuthSubscribersForReset();
            return Task.CompletedTask;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Amba — static convenience surface
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Top-level amba SDK convenience API.
    ///
    /// This is a thin static facade over <see cref="AmbaClient"/>. It exists
    /// because the vast majority of mobile apps want exactly one SDK instance,
    /// configured at startup, accessed everywhere. Everything here resolves
    /// to a single <see cref="AmbaClient"/> created by <see cref="ConfigureAsync"/>.
    /// Apps that need multiple instances (multi-tenant, multi-environment)
    /// skip this and construct <see cref="AmbaClient"/> directly.
    /// </summary>
    public static class Amba
    {
        public const string Version = "4.0.2";

        private static AmbaClient _client;

        /// <summary>
        /// Initialize the SDK against the default native binding. Must be
        /// called once before any other <c>Amba.*</c> access.
        /// </summary>
        /// <remarks>
        /// Calling ConfigureAsync a second time throws — the C-ABI core's
        /// <c>amba_init</c> rejects re-init with "amba already initialized"
        /// so the second call surfaces an <see cref="AmbaException"/> with
        /// that message. To swap tenants in the same process, call
        /// <see cref="ResetAsync"/> first to release the Rust core's
        /// singleton, then re-call ConfigureAsync with the new key.
        /// </remarks>
        public static async Task ConfigureAsync(string apiKey, string baseUrl = null, bool consentRequired = false, bool debug = false)
        {
            // Validate before constructing real bindings so a misconfigured
            // app fails fast with a clean ArgumentException rather than
            // after touching the native library.
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentException("apiKey must not be empty", nameof(apiKey));

            var bindings = new DefaultNativeMethods();
            var client = new AmbaClient(bindings);
            await client.InitializeAsync(apiKey, baseUrl, consentRequired, debug);
            _client = client;
        }

        /// <summary>
        /// The currently-active <see cref="AmbaClient"/>. Throws
        /// <see cref="InvalidOperationException"/> if <see cref="ConfigureAsync"/>
        /// has not yet been called.
        /// </summary>
        public static AmbaClient Client => RequireClient();

        public static EventsNamespace Events => RequireClient().Events;
        public static AuthNamespace Auth => RequireClient().Auth;
        public static UsersNamespace Users => RequireClient().Users;
        public static SessionsNamespace Sessions => RequireClient().Sessions;
        public static SyncNamespace Sync => RequireClient().Sync;
        public static CollectionsNamespace Collections => RequireClient().Collections;
        public static StorageNamespace Storage => RequireClient().Storage;
        public static PushNamespace Push => RequireClient().Push;
        public static EntitlementsNamespace Entitlements => RequireClient().Entitlements;
        public static AiNamespace Ai => RequireClient().Ai;
        public static ConfigNamespace Config => RequireClient().Config;
        public static FlagsNamespace Flags => RequireClient().Flags;

        // ── gamification ──
        public static AchievementsNamespace Achievements => RequireClient().Achievements;
        public static ChallengesNamespace Challenges => RequireClient().Challenges;
        public static CurrenciesNamespace Currencies => RequireClient().Currencies;
        public static InventoryNamespace Inventory => RequireClient().Inventory;
        public static LeaderboardsNamespace Leaderboards => RequireClient().Leaderboards;
        public static LeaguesNamespace Leagues => RequireClient().Leagues;
        public static StoresNamespace Stores => RequireClient().Stores;
        public static XpNamespace Xp => RequireClient().Xp;
        public static StreaksNamespace Streaks => RequireClient().Streaks;

        // ── social ──
        public static FeedsNamespace Feeds => RequireClient().Feeds;
        public static FriendsNamespace Friends => RequireClient().Friends;
        public static GroupsNamespace Groups => RequireClient().Groups;
        public static MessagingNamespace Messaging => RequireClient().Messaging;
        public static ModerationNamespace Moderation => RequireClient().Moderation;
        public static ReviewsNamespace Reviews => RequireClient().Reviews;
        public static RolesNamespace Roles => RequireClient().Roles;
        public static ReferralsNamespace Referrals => RequireClient().Referrals;

        // ── lifecycle ──
        public static CatalogNamespace Catalog => RequireClient().Catalog;
        public static ContentNamespace Content => RequireClient().Content;
        public static DeepLinksNamespace DeepLinks => RequireClient().DeepLinks;
        public static OnboardingNamespace Onboarding => RequireClient().Onboarding;

        // ── diagnostics ──
        public static DiagnosticsNamespace Diagnostics => RequireClient().Diagnostics;

        public static string AnonymousId => RequireClient().AnonymousId;
        public static string AppUserId => RequireClient().AppUserId;
        public static bool IsAuthenticated => RequireClient().IsAuthenticated;
        public static void SetDebug(bool enabled) => RequireClient().SetDebug(enabled);

        /// <summary>
        /// Release the Rust core singleton and clear the static
        /// <see cref="Client"/> reference. After calling this,
        /// <see cref="ConfigureAsync"/> can be called again with a fresh
        /// API key (multi-tenant flow). Throws
        /// <see cref="InvalidOperationException"/> if no client is
        /// configured.
        ///
        /// The static <c>_client</c> reference is cleared in a <c>finally</c>
        /// block so that a failure inside the native <c>amba_reset</c> call
        /// does not leave the facade in a half-configured state — once the
        /// caller has asked for a reset, the next access must require a
        /// fresh <see cref="ConfigureAsync"/>, error or no error.
        ///
        /// On a successful reset, <see cref="AuthNamespace.OnAuthStateChange"/>
        /// subscribers are notified with a <c>null</c> session via the
        /// instance <see cref="AmbaClient.ResetAsync"/> method.
        /// </summary>
        public static async Task ResetAsync()
        {
            var c = RequireClient();
            try
            {
                await c.ResetAsync();
            }
            finally
            {
                _client = null;
            }
        }

        private static AmbaClient RequireClient() =>
            _client ?? throw new InvalidOperationException(
                "Amba.ConfigureAsync must be called before accessing Amba.*");
    }

    public class AmbaException : Exception
    {
        public AmbaException(string message) : base(message) {}
    }

    /// <summary>
    /// Typed error mirror of `AmbaApiError` in the TypeScript SDK. Carries
    /// a stable <see cref="Code"/> string (e.g. <c>"UNAUTHORIZED"</c>,
    /// <c>"RATE_LIMITED"</c>, …) and an opaque <see cref="Details"/> payload
    /// so callers can branch on category without parsing the message.
    ///
    /// Extends <see cref="AmbaException"/> for back-compat — every catch
    /// block written against `AmbaException` still works. New code should
    /// catch <see cref="AmbaApiError"/> and branch on <see cref="Code"/>.
    ///
    /// Code list (string-typed rather than enum so customers can stamp
    /// custom codes without coordinating with this assembly):
    ///   UNAUTHORIZED, FORBIDDEN, NOT_FOUND, CONFLICT, RATE_LIMITED,
    ///   VALIDATION_ERROR, NETWORK_ERROR, HTTP_ERROR, CIRCUIT_OPEN,
    ///   CONSENT_NOT_GRANTED, NOT_INITIALIZED, INVALID_CONFIG,
    ///   INVALID_ARGUMENT, PUSH_PERMISSION_DENIED,
    ///   PUSH_REGISTRATION_FAILED, UNKNOWN_ERROR.
    /// </summary>
    public class AmbaApiError : AmbaException
    {
        /// <summary>Stable error code. See class doc for the standard list.</summary>
        public readonly string Code;
        /// <summary>Optional opaque payload (raw HTTP body, validation field paths, …).</summary>
        public readonly object Details;

        public AmbaApiError(string code, string message, object details = null)
            : base(message)
        {
            Code = code ?? "UNKNOWN_ERROR";
            Details = details;
        }

        /// <summary>
        /// Best-effort: infer a stable code from a Rust `AmbaError` Display
        /// string. Keep in sync with the equivalent helper in the TS SDKs
        /// (`sdks/packages/*/src/error.ts::codeFromMessage`). Stale match
        /// lists fall back to <c>UNKNOWN_ERROR</c> — not a footgun, just a
        /// QoL regression.
        /// </summary>
        public static string CodeFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return "UNKNOWN_ERROR";
            if (message.StartsWith("Unauthorized")) return "UNAUTHORIZED";
            if (message.StartsWith("Forbidden")) return "FORBIDDEN";
            if (message.StartsWith("Not found")) return "NOT_FOUND";
            if (message.StartsWith("Conflict")) return "CONFLICT";
            if (message.StartsWith("Rate limited")) return "RATE_LIMITED";
            if (message.StartsWith("Validation error")) return "VALIDATION_ERROR";
            if (message.StartsWith("Network error")) return "NETWORK_ERROR";
            if (message.StartsWith("HTTP error")) return "HTTP_ERROR";
            if (message.StartsWith("Circuit breaker")) return "CIRCUIT_OPEN";
            if (message.StartsWith("Consent not granted")) return "CONSENT_NOT_GRANTED";
            if (message.Contains("not initialized")) return "NOT_INITIALIZED";
            if (message.StartsWith("Invalid configuration")) return "INVALID_CONFIG";
            if (message.StartsWith("Invalid argument")) return "INVALID_ARGUMENT";
            return "UNKNOWN_ERROR";
        }

        /// <summary>
        /// Coerce any caught exception into an <see cref="AmbaApiError"/>.
        /// Idempotent — passing an existing <see cref="AmbaApiError"/>
        /// returns it unchanged.
        /// </summary>
        public static AmbaApiError From(Exception err)
        {
            if (err is AmbaApiError api) return api;
            if (err == null) return new AmbaApiError("UNKNOWN_ERROR", "null");
            return new AmbaApiError(CodeFromMessage(err.Message), err.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Namespaces — each holds an INativeMethods and is constructable on its own
    // ═══════════════════════════════════════════════════════════════

    public class EventsNamespace
    {
        private readonly INativeMethods _bindings;
        public EventsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task TrackAsync(string eventName, Dictionary<string, object> properties = null)
        {
            string propsJson = properties != null ? JsonConvert.SerializeObject(properties) : null;
            var result = NativeUtil.InvokeBinary(_bindings.amba_events_track, eventName, propsJson, _bindings);
            if (result != null) JsonUtil.ThrowFromRaw(result);
            return Task.CompletedTask;
        }
    }

    public class AuthNamespace
    {
        private readonly INativeMethods _bindings;
        // In-SDK auth-state pub/sub. Mirrors the TS SDK shape so
        // `Amba.Auth.OnAuthStateChange` works identically across SDKs.
        // HashSet keyed by handler reference — Add/Remove are O(1), and the
        // returned IDisposable closes over the exact reference to remove.
        private readonly HashSet<Action<Session>> _authSubscribers = new();

        public AuthNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> SignInAnonymouslyAsync()
        {
            var raw = NativeUtil.CallReturnString(_bindings.amba_auth_sign_in_anonymously, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(token));
            return Task.FromResult(token);
        }

        public Task<JToken> SignInWithEmailAsync(string email, string password)
        {
            var raw = NativeUtil.InvokeBinary(_bindings.amba_auth_sign_in_with_email, email, password, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(token));
            return Task.FromResult(token);
        }

        public Task<JToken> SignUpWithEmailAsync(string email, string password)
        {
            var raw = NativeUtil.InvokeBinary(_bindings.amba_auth_sign_up_with_email, email, password, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(token));
            return Task.FromResult(token);
        }

        public Task<JToken> SignInWithAppleAsync(string identityToken)
        {
            var raw = NativeUtil.InvokeBinary(_bindings.amba_auth_sign_in_with_social, "apple", identityToken, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(token));
            return Task.FromResult(token);
        }

        public Task<JToken> SignInWithGoogleAsync(string idToken)
        {
            var raw = NativeUtil.InvokeBinary(_bindings.amba_auth_sign_in_with_social, "google", idToken, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(token));
            return Task.FromResult(token);
        }

        /// <summary>
        /// Request a one-time passcode for <paramref name="email"/>. The
        /// server emails a 6-digit code; the user types it in, then call
        /// <see cref="VerifyEmailOtpAsync"/> to exchange for a session.
        /// </summary>
        public Task RequestEmailOtpAsync(string email)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_auth_request_email_otp, email, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Exchange <paramref name="email"/> + <paramref name="code"/> for
        /// a session. Returns the same <see cref="JToken"/> shape as
        /// <see cref="SignInWithEmailAsync"/>.
        /// </summary>
        public Task<JToken> VerifyEmailOtpAsync(string email, string code)
        {
            var raw = NativeUtil.InvokeBinary(_bindings.amba_auth_verify_email_otp, email, code, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(token));
            return Task.FromResult(token);
        }

        /// <summary>
        /// Request a one-time passcode by SMS. <paramref name="phone"/>
        /// must be E.164 (starts with `+`, 8–15 total digits). The SDK
        /// rejects non-E.164 phones before the network call.
        /// </summary>
        public Task RequestSmsOtpAsync(string phone)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_auth_request_sms_otp, phone, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Exchange <paramref name="phone"/> + <paramref name="code"/>
        /// for a session. Same <see cref="JToken"/> shape as
        /// <see cref="VerifyEmailOtpAsync"/>.
        /// </summary>
        public Task<JToken> VerifySmsOtpAsync(string phone, string code)
        {
            var raw = NativeUtil.InvokeBinary(_bindings.amba_auth_verify_sms_otp, phone, code, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(token));
            return Task.FromResult(token);
        }

        public Task SignOutAsync(bool rotateAnonymousId = false)
        {
            var rawPtr = _bindings.amba_auth_sign_out(rotateAnonymousId ? 1u : 0u);
            var raw = NativeUtil.PtrToStringAndFree(rawPtr, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            NotifyAuthSubscribers(null);
            return Task.CompletedTask;
        }

        public Task<JToken> RefreshAsync()
        {
            var raw = NativeUtil.CallReturnString(_bindings.amba_auth_refresh, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(token));
            return Task.FromResult(token);
        }

        public Task<JToken> MeAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_auth_me, _bindings)));

        // ── 4.0 additions: magic link + account linking ──────────────

        /// <summary>
        /// Request a magic-link email to <paramref name="email"/>. The
        /// server emails a one-time link; the client extracts the token
        /// and calls <see cref="VerifyMagicLinkAsync"/> to exchange for a
        /// session. Maps to <c>POST /v1/client/auth/magic-link/request</c>.
        /// </summary>
        public Task RequestMagicLinkAsync(string email)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_auth_request_magic_link, email, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Exchange a magic-link <paramref name="token"/> for a session.
        /// Maps to <c>POST /v1/client/auth/magic-link/verify</c>.
        /// </summary>
        public Task<JToken> VerifyMagicLinkAsync(string token)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_auth_verify_magic_link, token, _bindings);
            var result = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(result));
            return Task.FromResult(result);
        }

        /// <summary>
        /// Link an external identity (<paramref name="provider"/> = "apple" /
        /// "google" / "email" / …) to the currently-signed-in user. Used
        /// to upgrade an anonymous session into an identified one without
        /// losing local state. Maps to <c>POST /v1/client/auth/link</c>.
        /// </summary>
        public Task<JToken> LinkAccountAsync(string provider, string credential)
        {
            var raw = NativeUtil.InvokeBinary(_bindings.amba_auth_link_account, provider, credential, _bindings);
            var result = JsonUtil.ExpectJson(raw);
            NotifyAuthSubscribers(SnapshotSession(result));
            return Task.FromResult(result);
        }

        // ── Phase-A parity helpers ────────────────────────────────────

        /// <summary>
        /// Snapshot the current session, or null if not authenticated.
        /// Mirrors `Amba.auth.getSession()` in the TypeScript SDK.
        /// </summary>
        /// <remarks>
        /// The session/refresh tokens stay inside the Rust core (the SDK
        /// handles the auto-refresh dance) — the snapshot's
        /// <c>SessionToken</c> / <c>RefreshToken</c> / <c>ExpiresAt</c>
        /// fields are empty strings on the consumer side, matching the
        /// TS shape exactly.
        /// </remarks>
        public Task<Session> GetSessionAsync()
        {
            if (_bindings.amba_is_authenticated() == 0)
                return Task.FromResult<Session>(null);
            var raw = NativeUtil.CallReturnString(_bindings.amba_auth_me, _bindings);
            if (raw == null) return Task.FromResult<Session>(null);
            JsonUtil.MaybeThrow(raw);
            var userToken = JToken.Parse(raw);
            return Task.FromResult(new Session
            {
                SessionToken = "",
                RefreshToken = "",
                User = userToken,
                ExpiresAt = "",
            });
        }

        /// <summary>
        /// Stable per-install anonymous identifier. Mirrors
        /// `Amba.auth.getAnonymousId()` in the TypeScript SDK. Returns the
        /// same value as <see cref="AmbaClient.AnonymousId"/>; surfaced
        /// here for cross-SDK parity.
        /// </summary>
        public string AnonymousId
        {
            get
            {
                var raw = NativeUtil.CallReturnString(_bindings.amba_anonymous_id, _bindings);
                if (raw == null) throw new InvalidOperationException("amba not configured");
                JsonUtil.MaybeThrow(raw);
                return raw;
            }
        }

        /// <summary>
        /// Subscribe to session changes. <paramref name="handler"/> is
        /// invoked synchronously after every signIn* / signUp* / refresh /
        /// signOut call. Returns an <see cref="IDisposable"/> — call
        /// <c>Dispose()</c> (or use <c>using</c>) to unsubscribe.
        /// </summary>
        /// <remarks>
        /// Handlers that throw are caught and logged via
        /// <see cref="Console.Error"/> so a single bad subscriber doesn't
        /// take down the rest of the pub/sub chain.
        /// </remarks>
        public IDisposable OnAuthStateChange(Action<Session> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _authSubscribers.Add(handler);
            return new AuthSubscription(_authSubscribers, handler);
        }

        private void NotifyAuthSubscribers(Session session)
        {
            // Snapshot before iterating so handlers can dispose themselves
            // (or each other) without mutating the live set.
            var snapshot = new List<Action<Session>>(_authSubscribers);
            foreach (var cb in snapshot)
            {
                try { cb(session); }
                catch (Exception err)
                {
                    Console.Error.WriteLine($"[amba] OnAuthStateChange handler threw: {err}");
                }
            }
        }

        /// <summary>
        /// Internal entry point so <see cref="AmbaClient.ResetAsync"/> can
        /// fan out the same "session cleared" notification that
        /// <see cref="SignOutAsync"/> emits. Kept internal because reset is
        /// the only legitimate non-auth caller — every other state change
        /// goes through the auth methods on this namespace.
        /// </summary>
        internal void NotifyAuthSubscribersForReset() => NotifyAuthSubscribers(null);

        private static Session SnapshotSession(JToken authResult)
        {
            if (authResult == null || authResult.Type != JTokenType.Object) return null;
            // Auth results from the Rust core carry a `user` envelope —
            // surface that as Session.User. Token strings stay empty on
            // the consumer side (SDK-managed inside the Rust core).
            var user = authResult["user"];
            if (user == null) return null;
            return new Session
            {
                SessionToken = "",
                RefreshToken = "",
                User = user,
                ExpiresAt = "",
            };
        }

        private sealed class AuthSubscription : IDisposable
        {
            private HashSet<Action<Session>> _set;
            private Action<Session> _handler;

            public AuthSubscription(HashSet<Action<Session>> set, Action<Session> handler)
            {
                _set = set;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_set != null && _handler != null)
                {
                    _set.Remove(_handler);
                    _set = null;
                    _handler = null;
                }
            }
        }
    }

    public class CollectionsNamespace
    {
        private readonly INativeMethods _bindings;
        public CollectionsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> FindAsync(string name, Dictionary<string, object> options = null)
        {
            var optionsJson = JsonConvert.SerializeObject(options ?? new Dictionary<string, object>());
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_collections_find, name, optionsJson, _bindings)));
        }

        public Task<JToken> FindOneAsync(string name, string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_collections_find_one, name, id, _bindings)));

        public Task<JToken> InsertAsync(string name, Dictionary<string, object> row)
        {
            var rowJson = JsonConvert.SerializeObject(row);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_collections_insert, name, rowJson, _bindings)));
        }

        public Task<JToken> UpdateAsync(string name, string id, Dictionary<string, object> set)
        {
            var setJson = JsonConvert.SerializeObject(set);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeTernary(_bindings.amba_collections_update, name, id, setJson, _bindings)));
        }

        public Task DeleteAsync(string name, string id)
        {
            var raw = NativeUtil.InvokeBinary(_bindings.amba_collections_delete, name, id, _bindings);
            if (raw != null) JsonUtil.MaybeThrow(raw);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Vector-search the collection. <paramref name="vector"/> is the
        /// query embedding, <paramref name="k"/> the top-k count.
        /// <paramref name="filter"/> is an optional Filter — see
        /// <see cref="NormalizeFilter"/> for the accepted shapes
        /// (recursive Filter tree, or shorthand <c>{column: value}</c>
        /// joined with AND).
        /// The Rust core expects an options JSON of shape
        /// <c>{"vector_field":"embedding","query_vector":[…],"k":N,"filter"?:…}</c>;
        /// the wrapper marshals <paramref name="vectorField"/> ("embedding"
        /// by default).
        /// </summary>
        public Task<JToken> FindNearestAsync(string name, IEnumerable<float> vector, int k, Dictionary<string, object> filter = null, string vectorField = "embedding")
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            var options = new Dictionary<string, object>
            {
                ["vector_field"] = vectorField,
                ["query_vector"] = new List<float>(vector),
                ["k"] = k,
            };
            var normalized = NormalizeFilter(filter);
            if (normalized != null) options["filter"] = normalized;
            var optionsJson = JsonConvert.SerializeObject(options);
            return Task.FromResult(JsonUtil.ExpectJson(
                NativeUtil.InvokeBinary(_bindings.amba_collections_find_nearest, name, optionsJson, _bindings)));
        }

        /// <summary>
        /// Count rows in <paramref name="name"/>. <paramref name="filter"/>
        /// is optional — pass null for an unfiltered count, a recursive
        /// Filter tree, or a shorthand <c>{column: value}</c> dictionary
        /// (each entry becomes a <c>{column, op:"eq", value}</c> condition,
        /// joined with AND). Returns the raw <c>{"data":{"count":N}}</c>
        /// envelope as a <see cref="JToken"/>.
        /// </summary>
        public Task<JToken> CountAsync(string name, Dictionary<string, object> filter = null)
        {
            var normalized = NormalizeFilter(filter);
            string filterJson = normalized != null ? JsonConvert.SerializeObject(normalized) : null;
            return Task.FromResult(JsonUtil.ExpectJson(
                NativeUtil.InvokeBinary(_bindings.amba_collections_count, name, filterJson, _bindings)));
        }

        // ── filter normalization ─────────────────────────────────────────
        //
        // The Rust core (`sdks/core/src/collections.rs::Filter`) deserializes
        // an `#[serde(untagged)]` enum:
        //
        //   - Condition: { "column": "...", "op": "...", "value": ... }
        //   - And:       { "and": [ Filter, ... ] }
        //   - Or:        { "or":  [ Filter, ... ] }
        //   - Not:       { "not": Filter }
        //
        // Customers calling `CountAsync("posts", { ["author_id"] = "u_1" })`
        // expect that to mean "WHERE author_id = 'u_1'" — but a flat
        // `{ "author_id": "u_1" }` is none of the Filter variants, so the
        // FFI rejects it with "invalid filter shape". We translate the
        // shorthand into the canonical tree shape on the way out.
        //
        // Pass-through (no rewrite) when the dict is already a recognized
        // Filter shape — exactly one key, named `and` / `or` / `not`, or
        // the two/three-key `{column, op[, value]}` Condition. Anything else
        // is treated as shorthand and folded into an AND of `eq` conditions.
        //
        // Extra keys alongside a recognized variant are a hard error rather
        // than a silent pass-through. The Rust core uses
        // `#[serde(untagged)]` on `Filter`, which silently drops fields
        // unknown to the matched variant — so shipping
        // `{column, op, value, author_id}` to FFI would succeed without ever
        // applying the `author_id` constraint and return wrong rows /
        // counts. Throw instead so the customer learns at the call site.
        internal static object NormalizeFilter(Dictionary<string, object> filter)
        {
            if (filter == null || filter.Count == 0) return null;

            if (filter.Count == 1)
            {
                foreach (var kv in filter)
                {
                    if (kv.Key == "and" || kv.Key == "or" || kv.Key == "not") return filter;
                }
            }

            // Reject mixed dicts that contain a structural Filter key
            // (`and` / `or` / `not`) alongside other keys — Rust's untagged
            // deserializer would silently drop the structural variant or
            // the extras, depending on order.
            bool hasStructural = filter.ContainsKey("and") || filter.ContainsKey("or") || filter.ContainsKey("not");
            if (hasStructural && filter.Count > 1)
            {
                throw new ArgumentException(
                    "Filter dictionary mixes a structural key ('and' / 'or' / 'not') with other keys; wrap the extras in their own Filter node instead.",
                    nameof(filter));
            }

            // Condition shape: requires both `column` and `op`. `value` is
            // optional on the Rust side (`is_null` / `is_not_null` carry no
            // value), so don't require it. Any extra keys are a hard error
            // — see the comment above re: silent field-drop.
            //
            // The presence of EITHER `column` or `op` triggers the Condition
            // path: a half-shaped dict (e.g. `{column:"x", author_id:"y"}`
            // missing `op`, or `{op:"eq", value:1}` missing `column`) would
            // otherwise silently fall through to the shorthand path and
            // ship spurious `{column:"column", op:"eq", value:"x"}`
            // conditions to the FFI — wrong rows, no error. Reject instead.
            bool hasColumn = filter.ContainsKey("column");
            bool hasOp = filter.ContainsKey("op");
            if (hasColumn || hasOp)
            {
                if (!(hasColumn && hasOp))
                {
                    throw new ArgumentException(
                        hasColumn
                            ? "Filter dictionary has 'column' but is missing 'op'; Condition shape requires both. Use shorthand `{ \"col\": value }` for equality, or supply an explicit `op`."
                            : "Filter dictionary has 'op' but is missing 'column'; Condition shape requires both.",
                        nameof(filter));
                }
                foreach (var kv in filter)
                {
                    if (kv.Key != "column" && kv.Key != "op" && kv.Key != "value")
                    {
                        throw new ArgumentException(
                            $"Filter Condition dictionary has unexpected key '{kv.Key}' alongside 'column'/'op'; only 'value' is allowed. Move extra constraints into a separate Condition wrapped in an 'and'.",
                            nameof(filter));
                    }
                }
                return filter;
            }

            // Shorthand: every entry becomes `{column, op:"eq", value}`.
            var conditions = new List<Dictionary<string, object>>(filter.Count);
            foreach (var kv in filter)
            {
                conditions.Add(new Dictionary<string, object>
                {
                    ["column"] = kv.Key,
                    ["op"] = "eq",
                    ["value"] = kv.Value,
                });
            }
            if (conditions.Count == 1) return conditions[0];
            return new Dictionary<string, object> { ["and"] = conditions };
        }
    }

    public class StorageNamespace
    {
        private readonly INativeMethods _bindings;
        public StorageNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> PresignAsync(string bucket, string filename, string mimeType, ulong sizeBytes, int retentionDays = -1)
        {
            IntPtr b = NativeUtil.MarshalUtf8(bucket), f = NativeUtil.MarshalUtf8(filename), m = NativeUtil.MarshalUtf8(mimeType);
            try
            {
                var ptr = _bindings.amba_storage_presign(b, f, m, sizeBytes, retentionDays);
                return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.PtrToStringAndFree(ptr, _bindings)));
            }
            finally
            {
                Marshal.FreeHGlobal(b);
                Marshal.FreeHGlobal(f);
                Marshal.FreeHGlobal(m);
            }
        }

        public Task<JToken> CommitAsync(string uploadId, string assetId) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_storage_commit, uploadId, assetId, _bindings)));

        /// <summary>List media assets, optionally filtered by <paramref name="prefix"/>. Null prefix returns all.</summary>
        public Task<JToken> ListAsync(string prefix = null) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_storage_list, prefix, _bindings)));

        /// <summary>Delete the asset identified by <paramref name="assetId"/>.</summary>
        public Task DeleteAsync(string assetId)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_storage_delete, assetId, _bindings);
            if (raw != null) JsonUtil.MaybeThrow(raw);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Download the bytes of an asset. The Rust core returns a JSON
        /// envelope of shape <c>{"data":"&lt;base64&gt;"}</c> — the wrapper
        /// decodes the base64 payload and returns raw bytes.
        /// </summary>
        /// <remarks>
        /// A corrupt or non-base64 <c>data</c> field is surfaced as an
        /// <see cref="AmbaApiError"/> (code <c>UNKNOWN_ERROR</c>) rather
        /// than the raw <see cref="FormatException"/> — keeps the storage
        /// failure surface uniform with the rest of the namespace, which
        /// routes every failure through <see cref="JsonUtil"/>.
        /// </remarks>
        public Task<byte[]> DownloadAsync(string assetId)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_storage_download, assetId, _bindings);
            var token = JsonUtil.ExpectJson(raw);
            if (token.Type != JTokenType.Object)
                throw new AmbaApiError("UNKNOWN_ERROR", $"unexpected download envelope: {raw}");
            var data = token["data"];
            if (data == null || data.Type != JTokenType.String)
                throw new AmbaApiError("UNKNOWN_ERROR", $"download envelope missing data: {raw}");
            try
            {
                return Task.FromResult(Convert.FromBase64String((string)data));
            }
            catch (FormatException err)
            {
                throw new AmbaApiError(
                    "UNKNOWN_ERROR",
                    $"download envelope data is not valid base64: {err.Message}");
            }
        }
    }

    public class PushNamespace
    {
        private readonly INativeMethods _bindings;
        public PushNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> RegisterAsync(string token, string platform, string bundleId = null) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeTernary(_bindings.amba_push_register, token, platform, bundleId, _bindings)));

        public Task SubscribeAsync(string topic)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_push_subscribe, topic, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        public Task UnregisterAsync(string token)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_push_unregister, token, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        public Task<JToken> GetTokensAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_push_get_tokens, _bindings)));

        public Task UnsubscribeAsync(string topic)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_push_unsubscribe, topic, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }
    }

    public class EntitlementsNamespace
    {
        private readonly INativeMethods _bindings;
        public EntitlementsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ListAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_entitlements_list, _bindings)));

        public Task<bool> HasAsync(string name)
        {
            IntPtr p = NativeUtil.MarshalUtf8(name);
            try { return Task.FromResult(_bindings.amba_entitlements_has(p) != 0); }
            finally { Marshal.FreeHGlobal(p); }
        }
    }

    public class AiNamespace
    {
        public AnthropicNamespace Anthropic { get; }
        public AiNamespace(INativeMethods bindings) { Anthropic = new AnthropicNamespace(bindings); }
    }

    public class AnthropicNamespace
    {
        public AnthropicMessagesNamespace Messages { get; }
        public AnthropicNamespace(INativeMethods bindings) { Messages = new AnthropicMessagesNamespace(bindings); }
    }

    public class AnthropicMessagesNamespace
    {
        private readonly INativeMethods _bindings;
        public AnthropicMessagesNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> CreateAsync(Dictionary<string, object> request)
        {
            var json = JsonConvert.SerializeObject(request);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_ai_anthropic_messages, json, _bindings)));
        }
    }

    public class ConfigNamespace
    {
        private readonly INativeMethods _bindings;
        public ConfigNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> FetchAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_config_fetch, _bindings)));
    }

    public class FlagsNamespace
    {
        private readonly INativeMethods _bindings;
        public FlagsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> FetchAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_flags_fetch, _bindings)));

        /// <summary>
        /// Single-flag lookup (SDK 4.0). Wraps
        /// <c>GET /v1/client/flags/{key}</c>. Returns <c>null</c> for
        /// unknown or disabled keys; throws on other failures.
        /// </summary>
        public Task<JToken?> GetAsync(string key)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_flags_get, key, _bindings);
            var env = JsonUtil.ExpectJson(raw);
            var data = env["data"];
            if (data == null || data.Type == JTokenType.Null) return Task.FromResult<JToken?>(null);
            return Task.FromResult<JToken?>(data);
        }
    }

    // ── gamification ─────────────────────────────────────────────────

    public class AchievementsNamespace
    {
        private readonly INativeMethods _bindings;
        public AchievementsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> AllAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_achievements_get_all, _bindings)));

        public Task<JToken> ProgressAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_achievements_get_progress, _bindings)));
    }

    public class ChallengesNamespace
    {
        private readonly INativeMethods _bindings;
        public ChallengesNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ActiveAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_challenges_get_active, _bindings)));

        public Task<JToken> GetAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_challenges_get, id, _bindings)));

        public Task<JToken> ProgressAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_challenges_get_progress, id, _bindings)));

        public Task<JToken> ClaimAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_challenges_claim, id, _bindings)));
    }

    public class CurrenciesNamespace
    {
        private readonly INativeMethods _bindings;
        public CurrenciesNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> BalanceAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_currencies_get_balance, _bindings)));

        public Task<JToken> TransactionsAsync(string currencyKey) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_currencies_get_transactions, currencyKey, _bindings)));
    }

    public class InventoryNamespace
    {
        private readonly INativeMethods _bindings;
        public InventoryNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ItemsAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_inventory_get_items, _bindings)));

        public Task<JToken> ItemAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_inventory_get_item, id, _bindings)));

        public Task<JToken> PurchaseAsync(Dictionary<string, object> request)
        {
            var json = JsonConvert.SerializeObject(request);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_inventory_purchase, json, _bindings)));
        }

        public Task<JToken> ConsumeAsync(Dictionary<string, object> request)
        {
            var json = JsonConvert.SerializeObject(request);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_inventory_consume, json, _bindings)));
        }
    }

    public class LeaderboardsNamespace
    {
        private readonly INativeMethods _bindings;
        public LeaderboardsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> GetAsync(string key) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_leaderboards_get, key, _bindings)));

        /// <summary>List leaderboard entries. <paramref name="limit"/> ≤ 0 → "no limit".</summary>
        public Task<JToken> EntriesAsync(string key, int limit = -1) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeStringInt(_bindings.amba_leaderboards_get_entries, key, limit, _bindings)));

        public Task<JToken> MyRankAsync(string key) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_leaderboards_get_my_rank, key, _bindings)));
    }

    public class StoresNamespace
    {
        private readonly INativeMethods _bindings;
        public StoresNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ListAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_stores_list, _bindings)));

        public Task<JToken> PurchaseOptionsAsync(string storeKey) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_stores_get_purchase_options, storeKey, _bindings)));

        public Task<JToken> PurchaseAsync(string storeKey, string purchaseOptionId, Dictionary<string, object> receipt)
        {
            var json = JsonConvert.SerializeObject(receipt ?? new Dictionary<string, object>());
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeTernary(_bindings.amba_stores_purchase, storeKey, purchaseOptionId, json, _bindings)));
        }
    }

    public class XpNamespace
    {
        private readonly INativeMethods _bindings;
        public XpNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> BalanceAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_xp_get_balance, _bindings)));

        /// <summary>List XP transactions. <paramref name="limit"/> ≤ 0 → "no limit".</summary>
        public Task<JToken> HistoryAsync(int limit = -1)
        {
            // No string arg — just a single int. Skip MarshalUtf8 helper.
            var ptr = _bindings.amba_xp_get_history(limit);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.PtrToStringAndFree(ptr, _bindings)));
        }

        public Task<JToken> ClaimAsync(string grantKey) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_xp_claim, grantKey, _bindings)));
    }

    public class StreaksNamespace
    {
        private readonly INativeMethods _bindings;
        public StreaksNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> AllAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_streaks_get_all, _bindings)));

        public Task<JToken> QualifyAsync(string streakKey) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_streaks_qualify, streakKey, _bindings)));
    }

    // ── social ───────────────────────────────────────────────────────

    public class FeedsNamespace
    {
        private readonly INativeMethods _bindings;
        public FeedsNamespace(INativeMethods bindings) { _bindings = bindings; }

        /// <summary>Get activity feed. Null <paramref name="feed"/> defaults to "timeline".</summary>
        public Task<JToken> ActivityAsync(string feed = null, string cursor = null) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_feeds_get_activity, feed, cursor, _bindings)));
    }

    public class FriendsNamespace
    {
        private readonly INativeMethods _bindings;
        public FriendsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ListAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_friends_get_list, _bindings)));

        public Task<JToken> FriendsAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_friends_get_friends, _bindings)));

        public Task<JToken> BlockUserAsync(string userId) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_friends_block_user, userId, _bindings)));

        public Task UnblockUserAsync(string userId)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_friends_unblock_user, userId, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        public Task RemoveBlockAsync(string friendshipId)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_friends_remove_block, friendshipId, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Unfriend by the other user's id (SDK 4.0). Wraps
        /// <c>DELETE /v1/client/friends/by-user/{userId}</c>. Server
        /// preserves blocked rows; use <see cref="UnblockUserAsync"/>
        /// to clear those instead.
        /// </summary>
        public Task RemoveFriendAsync(string userId)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_friends_remove_friend, userId, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        /// <summary>Send a friend request to <paramref name="userId"/>. Returns the new Friendship row.</summary>
        public Task<JToken> SendRequestAsync(string userId) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_friends_send_request, userId, _bindings)));

        /// <summary>Accept the friend request identified by <paramref name="friendshipId"/>.</summary>
        public Task<JToken> AcceptRequestAsync(string friendshipId) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_friends_accept_request, friendshipId, _bindings)));

        /// <summary>Decline the friend request identified by <paramref name="friendshipId"/>.</summary>
        public Task DeclineRequestAsync(string friendshipId)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_friends_decline_request, friendshipId, _bindings);
            if (raw != null) JsonUtil.MaybeThrow(raw);
            return Task.CompletedTask;
        }
    }

    public class GroupsNamespace
    {
        private readonly INativeMethods _bindings;
        public GroupsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> CreateAsync(Dictionary<string, object> params_)
        {
            var json = JsonConvert.SerializeObject(params_);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_groups_create, json, _bindings)));
        }

        public Task<JToken> GetAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_groups_get, id, _bindings)));

        public Task<JToken> UpdateAsync(string id, Dictionary<string, object> patch)
        {
            var json = JsonConvert.SerializeObject(patch);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_groups_update, id, json, _bindings)));
        }

        public Task DeleteAsync(string id)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_groups_delete, id, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        public Task<JToken> MembersAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_groups_get_members, id, _bindings)));

        public Task<JToken> JoinAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_groups_join, id, _bindings)));

        public Task LeaveAsync(string id)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_groups_leave, id, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }

        public Task<JToken> InviteAsync(string id, string userId) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_groups_invite, id, userId, _bindings)));
    }

    public class MessagingNamespace
    {
        private readonly INativeMethods _bindings;
        public MessagingNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ConversationsAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_messaging_get_conversations, _bindings)));

        /// <summary>
        /// Create a new conversation. <paramref name="participants"/> is the
        /// list of user ids to include; <paramref name="metadata"/> can hold
        /// optional <c>type</c> ("direct" / "group") and <c>name</c> keys
        /// (server's <c>CreateConversationInput</c> shape).
        /// </summary>
        /// <remarks>
        /// <paramref name="metadata"/> may not contain a <c>participant_ids</c>
        /// key — the participant list is owned by the
        /// <paramref name="participants"/> argument. Passing both is a
        /// programming error and throws <see cref="ArgumentException"/>
        /// rather than silently letting metadata clobber the typed argument.
        /// </remarks>
        public Task<JToken> CreateConversationAsync(IEnumerable<string> participants, Dictionary<string, object> metadata = null)
        {
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            if (metadata != null && metadata.ContainsKey("participant_ids"))
            {
                throw new ArgumentException(
                    "metadata must not contain a 'participant_ids' key — pass participants via the 'participants' argument instead.",
                    nameof(metadata));
            }
            var body = new Dictionary<string, object>
            {
                ["participant_ids"] = new List<string>(participants),
            };
            if (metadata != null)
            {
                foreach (var kv in metadata) body[kv.Key] = kv.Value;
            }
            var json = JsonConvert.SerializeObject(body);
            return Task.FromResult(JsonUtil.ExpectJson(
                NativeUtil.InvokeUnary(_bindings.amba_messaging_create_conversation, json, _bindings)));
        }

        /// <summary>
        /// List messages in <paramref name="conversationId"/>. Pass <c>null</c>
        /// for <paramref name="limit"/> / <paramref name="offset"/> to defer
        /// to server defaults. The FFI uses the <c>u32::MAX</c> sentinel to
        /// mean "not provided" so the call shape stays ABI-stable.
        /// </summary>
        public Task<JToken> ListMessagesAsync(string conversationId, uint? limit = null, uint? offset = null)
        {
            uint l = limit ?? uint.MaxValue;
            uint o = offset ?? uint.MaxValue;
            IntPtr c = NativeUtil.MarshalUtf8(conversationId);
            try
            {
                var ptr = _bindings.amba_messaging_list_messages(c, l, o);
                return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.PtrToStringAndFree(ptr, _bindings)));
            }
            finally { Marshal.FreeHGlobal(c); }
        }

        /// <summary>
        /// Fetch a single message by id. The Rust core paginates
        /// <c>list_messages</c> internally and filters by id (the REST API
        /// has no by-id GET route). Returns the literal <c>null</c> JSON
        /// when the message isn't found in the first 5,000 entries.
        /// </summary>
        /// <remarks>
        /// Before SDK 4.0 this called a non-existent <c>amba_messaging_get_message</c>
        /// symbol — first call crashed with <c>EntryPointNotFoundException</c>.
        /// Phase A implemented the symbol with a <c>(conversation_id, message_id)</c>
        /// signature; the wrapper now passes both args.
        /// </remarks>
        public Task<JToken> GetMessageAsync(string conversationId, string messageId) =>
            Task.FromResult(JsonUtil.ExpectJson(
                NativeUtil.InvokeBinary(_bindings.amba_messaging_get_message, conversationId, messageId, _bindings)));

        public Task<JToken> SendMessageAsync(Dictionary<string, object> request)
        {
            var json = JsonConvert.SerializeObject(request);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_messaging_send_message, json, _bindings)));
        }

        /// <summary>Mark all messages in <paramref name="conversationId"/> as read for the current user.</summary>
        public Task MarkReadAsync(string conversationId)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_messaging_mark_read, conversationId, _bindings);
            if (raw != null) JsonUtil.MaybeThrow(raw);
            return Task.CompletedTask;
        }
    }

    public class ModerationNamespace
    {
        private readonly INativeMethods _bindings;
        public ModerationNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ReportUserAsync(Dictionary<string, object> request)
        {
            var json = JsonConvert.SerializeObject(request);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_moderation_report_user, json, _bindings)));
        }

        public Task<JToken> ReportContentAsync(Dictionary<string, object> request)
        {
            var json = JsonConvert.SerializeObject(request);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_moderation_report_content, json, _bindings)));
        }

        public Task<JToken> ReportStatusAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_moderation_get_report_status, id, _bindings)));
    }

    public class ReviewsNamespace
    {
        private readonly INativeMethods _bindings;
        public ReviewsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ListAsync(string targetType, string targetId) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_reviews_list, targetType, targetId, _bindings)));

        public Task<JToken> CreateAsync(Dictionary<string, object> params_)
        {
            var json = JsonConvert.SerializeObject(params_);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_reviews_create, json, _bindings)));
        }

        public Task<JToken> UpdateAsync(string id, Dictionary<string, object> patch)
        {
            var json = JsonConvert.SerializeObject(patch);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_reviews_update, id, json, _bindings)));
        }

        public Task DeleteAsync(string id)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_reviews_delete, id, _bindings);
            if (raw != null) JsonUtil.ThrowFromRaw(raw);
            return Task.CompletedTask;
        }
    }

    public class RolesNamespace
    {
        private readonly INativeMethods _bindings;
        public RolesNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> MyRolesAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_roles_get_my_roles, _bindings)));

        public Task<bool> HasPermissionAsync(string permission)
        {
            IntPtr p = NativeUtil.MarshalUtf8(permission);
            try { return Task.FromResult(_bindings.amba_roles_has_permission(p) != 0); }
            finally { Marshal.FreeHGlobal(p); }
        }
    }

    public class ReferralsNamespace
    {
        private readonly INativeMethods _bindings;
        public ReferralsNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ReferralCodeAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_referrals_get_referral_code, _bindings)));

        public Task<JToken> ClaimReferralAsync(string code) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_referrals_claim_referral, code, _bindings)));

        /// <summary>Create a referral code. Null <paramref name="code"/> = server-generated; <paramref name="maxUses"/> ≤ 0 = uncapped.</summary>
        public Task<JToken> CreateAsync(string code = null, int maxUses = -1) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeStringInt(_bindings.amba_referrals_create, code, maxUses, _bindings)));
    }

    // ── lifecycle ────────────────────────────────────────────────────

    public class CatalogNamespace
    {
        private readonly INativeMethods _bindings;
        public CatalogNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> ListAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_catalog_list, _bindings)));

        /// <summary>Fetch a single catalog item by <paramref name="id"/>. NEW in 4.0.</summary>
        public Task<JToken> GetAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_catalog_get, id, _bindings)));
    }

    public class ContentNamespace
    {
        private readonly INativeMethods _bindings;
        public ContentNamespace(INativeMethods bindings) { _bindings = bindings; }

        /// <summary>
        /// Get today's content for <paramref name="channel"/>. Null
        /// <paramref name="channel"/> defaults to <c>"default"</c> server-side
        /// (Phase A: the Rust core treats a null pointer as the default
        /// channel, so single-channel callers can omit the argument).
        /// </summary>
        public Task<JToken> TodayAsync(string channel = null) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_content_get_today, channel, _bindings)));

        /// <summary>
        /// Get the library page for <paramref name="channel"/>.
        /// <paramref name="limit"/> = null (or ≤ 0) means "no limit";
        /// <paramref name="cursor"/> = null means "first page".
        /// </summary>
        /// <remarks>
        /// Phase-A C-ABI change: <c>amba_content_get_library</c> now takes
        /// <c>(channel, limit: u32, cursor)</c>. We pass <c>(uint)(limit ?? 0)</c>
        /// and the Rust side maps 0 → <c>None</c>.
        /// </remarks>
        public Task<JToken> LibraryAsync(string channel = null, int? limit = null, string cursor = null)
        {
            uint limitU = (uint)(limit.HasValue && limit.Value > 0 ? limit.Value : 0);
            return Task.FromResult(JsonUtil.ExpectJson(
                NativeUtil.InvokeStringUintString(_bindings.amba_content_get_library, channel, limitU, cursor, _bindings)));
        }

        public Task<JToken> ItemAsync(string id) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_content_get_item, id, _bindings)));

        public Task<JToken> UpdateItemAsync(string id, Dictionary<string, object> state)
        {
            var json = JsonConvert.SerializeObject(state);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_content_update_item, id, json, _bindings)));
        }

        public Task<JToken> CreateItemAsync(string channel, Dictionary<string, object> item)
        {
            var json = JsonConvert.SerializeObject(item);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeBinary(_bindings.amba_content_create_item, channel, json, _bindings)));
        }
    }

    public class DeepLinksNamespace
    {
        private readonly INativeMethods _bindings;
        public DeepLinksNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> GetAsync(string shortCode) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_deep_links_get, shortCode, _bindings)));

        public Task<JToken> CreateAsync(Dictionary<string, object> params_)
        {
            var json = JsonConvert.SerializeObject(params_);
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_deep_links_create, json, _bindings)));
        }
    }

    public class OnboardingNamespace
    {
        private readonly INativeMethods _bindings;
        public OnboardingNamespace(INativeMethods bindings) { _bindings = bindings; }

        public Task<JToken> StatusAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_onboarding_get_status, _bindings)));

        public Task<JToken> NextStepAsync(Dictionary<string, object> payload)
        {
            var json = JsonConvert.SerializeObject(payload ?? new Dictionary<string, object>());
            return Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_onboarding_next_step, json, _bindings)));
        }

        public Task<JToken> SkipStepAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_onboarding_skip_step, _bindings)));

        public Task<JToken> CompleteAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_onboarding_complete, _bindings)));
    }

    // ═══════════════════════════════════════════════════════════════
    // 4.0 additions: users / sessions / sync / leagues
    // ═══════════════════════════════════════════════════════════════

    public class UsersNamespace
    {
        private readonly INativeMethods _bindings;
        public UsersNamespace(INativeMethods bindings) { _bindings = bindings; }

        /// <summary>
        /// Fetch a user record. <paramref name="userId"/> defaults to
        /// <c>null</c> which resolves to the current authenticated user
        /// (<c>GET /v1/client/users/me</c>).
        /// </summary>
        public Task<JToken> GetAsync(string userId = null) =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.InvokeUnary(_bindings.amba_users_get, userId, _bindings)));

        /// <summary>
        /// Patch the current user's profile. <paramref name="patch"/> is a
        /// shallow dictionary of writable fields → new values.
        /// <paramref name="userId"/> is reserved for future admin-edit
        /// flows; defaults to current user.
        /// </summary>
        public Task<JToken> UpdateAsync(Dictionary<string, object> patch, string userId = null)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            var json = JsonConvert.SerializeObject(patch);
            return Task.FromResult(JsonUtil.ExpectJson(
                NativeUtil.InvokeBinary(_bindings.amba_users_update, userId, json, _bindings)));
        }
    }

    public class SessionsNamespace
    {
        private readonly INativeMethods _bindings;
        public SessionsNamespace(INativeMethods bindings) { _bindings = bindings; }

        /// <summary>List active sessions for the current user.</summary>
        public Task<JToken> ListAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_sessions_list, _bindings)));

        /// <summary>Revoke <paramref name="sessionId"/> — invalidates that session immediately.</summary>
        public Task RevokeAsync(string sessionId)
        {
            var raw = NativeUtil.InvokeUnary(_bindings.amba_sessions_revoke, sessionId, _bindings);
            if (raw != null) JsonUtil.MaybeThrow(raw);
            return Task.CompletedTask;
        }
    }

    public class SyncNamespace
    {
        private readonly INativeMethods _bindings;
        public SyncNamespace(INativeMethods bindings) { _bindings = bindings; }

        /// <summary>
        /// Push an offline-recorded batch of <paramref name="changes"/>
        /// for server-wins conflict resolution. Returns
        /// <c>{ applied, conflicts }</c>.
        /// </summary>
        public Task<JToken> PushChangesAsync(IEnumerable<Dictionary<string, object>> changes)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            var json = JsonConvert.SerializeObject(changes);
            return Task.FromResult(JsonUtil.ExpectJson(
                NativeUtil.InvokeUnary(_bindings.amba_sync_push_changes, json, _bindings)));
        }

        /// <summary>
        /// Pull server-side changes for <paramref name="entityType"/>
        /// (e.g. "collections.posts"), optionally since
        /// <paramref name="checkpointToken"/>. Returns the changes plus a
        /// new cursor.
        /// </summary>
        public Task<JToken> PullChangesAsync(string entityType, string checkpointToken = null)
        {
            if (string.IsNullOrEmpty(entityType))
                throw new ArgumentException("entityType is required", nameof(entityType));
            var payload = new Dictionary<string, object> { ["entity_type"] = entityType };
            if (checkpointToken != null) payload["checkpoint_token"] = checkpointToken;
            var json = JsonConvert.SerializeObject(payload);
            return Task.FromResult(JsonUtil.ExpectJson(
                NativeUtil.InvokeUnary(_bindings.amba_sync_pull_changes, json, _bindings)));
        }
    }

    public class LeaguesNamespace
    {
        private readonly INativeMethods _bindings;
        public LeaguesNamespace(INativeMethods bindings) { _bindings = bindings; }

        /// <summary>Get the current user's league membership.</summary>
        public Task<JToken> MeAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_leagues_me, _bindings)));

        /// <summary>Get the current user's cohort (peers in the same tier).</summary>
        public Task<JToken> CohortAsync() =>
            Task.FromResult(JsonUtil.ExpectJson(NativeUtil.CallReturnString(_bindings.amba_leagues_cohort, _bindings)));
    }

    // ═══════════════════════════════════════════════════════════════
    // Diagnostics — wire-verify primitive
    //
    // Customers (or installer agents) call `Amba.Diagnostics.Ping()`
    // once after configuration to confirm the SDK is talking to the
    // expected project with the expected key in the expected
    // environment. Every field on `PingResult` is server-decided, so
    // comparing `server_project_id` / `key_fingerprint` against what
    // the customer thinks they configured catches "wrong .env" silent
    // failures on the spot.
    //
    // Wire schema authoritative in `core/src/diagnostics.rs::PingResult`
    // and the route handler at `apps/api/src/routes/client/diagnostics.ts`.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Server-echoed diagnostics envelope. Every field is decided by
    /// the SERVER — none is trusted from the request. Mirrors the
    /// Rust core's <c>PingResult</c> struct.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a successful round-trip <see cref="Ok"/> is <c>true</c> and
    /// <see cref="Error"/> is <c>null</c>. On a server-side internal
    /// failure (e.g. control DB read error) the route returns 200 with
    /// <see cref="Ok"/> = <c>false</c> and <see cref="Error"/> populated
    /// with a stable code like <c>"DIAGNOSTICS_INTERNAL_ERROR"</c>.
    /// Network / auth failures (4xx, 5xx, no connectivity) bubble out
    /// as <see cref="AmbaApiError"/> from <see cref="DiagnosticsNamespace.PingAsync"/>
    /// and never produce a <see cref="PingResult"/>.
    /// </para>
    /// <para>
    /// Struct (not class) per the multi-SDK shape convention — small,
    /// value-typed, no inheritance hooks needed.
    /// </para>
    /// </remarks>
    public struct PingResult
    {
        /// <summary><c>true</c> on a successful diagnostic; <c>false</c> when
        /// the server reached the route but couldn't resolve the lookup.</summary>
        [JsonProperty("ok")] public bool Ok;

        /// <summary>The project id the server resolved from the API key.
        /// Compare against the project id the customer thinks they
        /// configured. Empty string (or <c>null</c>) when <see cref="Ok"/>
        /// is <c>false</c> and the lookup failed.</summary>
        [JsonProperty("server_project_id")] public string ServerProjectId;

        /// <summary><c>"production"</c> / <c>"staging"</c> / <c>"sandbox"</c>,
        /// derived server-side from the API key's environment + developer
        /// tier. <c>null</c> when the lookup failed.</summary>
        [JsonProperty("environment")] public string Environment;

        /// <summary>Last 4 hex chars of the sha256 of the API key the
        /// server actually saw on this request. Match against the same
        /// suffix in the developer console to confirm the right secret
        /// is loaded.</summary>
        [JsonProperty("key_fingerprint")] public string KeyFingerprint;

        /// <summary>Server-measured handler latency in milliseconds (does
        /// NOT include client-side network time).</summary>
        [JsonProperty("latency_ms")] public long LatencyMs;

        /// <summary><c>null</c> on success. On a server-side failure the
        /// route returns 200 with <see cref="Ok"/> = <c>false</c> and a
        /// stable code here — typically <c>"DIAGNOSTICS_INTERNAL_ERROR"</c>.</summary>
        [JsonProperty("error")] public string Error;
    }

    /// <summary>
    /// Diagnostics namespace — wire-verify primitive. Call
    /// <see cref="PingAsync"/> once after <see cref="Amba.ConfigureAsync"/>
    /// to confirm the SDK is talking to the expected project.
    /// </summary>
    public class DiagnosticsNamespace
    {
        private readonly INativeMethods _bindings;
        public DiagnosticsNamespace(INativeMethods bindings) { _bindings = bindings; }

        // The customer-facing alias matches the brief / public docs —
        // CamelCase, no `Async` suffix. Forwards to the canonical
        // `PingAsync` method.
        public Task<PingResult> Ping() => PingAsync();

        /// <summary>
        /// Issue a wire-verify ping. Returns a <see cref="PingResult"/>
        /// even on server-side internal failures (those surface as
        /// <c>Ok=false</c> + populated <c>Error</c>). Throws
        /// <see cref="AmbaApiError"/> for transport / auth / 5xx
        /// failures.
        /// </summary>
        /// <remarks>
        /// Emits a single <c>UnityEngine.Debug.Log</c> on success and
        /// <c>UnityEngine.Debug.LogError</c> on failure, both prefixed
        /// with <c>[Amba SDK]</c> so Unity console filtering works.
        /// The log call is compiled out under non-Unity test contexts.
        /// </remarks>
        public Task<PingResult> PingAsync()
        {
            try
            {
                var raw = NativeUtil.CallReturnString(_bindings.amba_diagnostics_ping, _bindings);
                if (raw == null)
                {
                    DiagnosticsLog.Error("ping: native returned null");
                    throw new AmbaApiError("UNKNOWN_ERROR", "diagnostics.ping returned null");
                }

                // Two wire shapes to disambiguate before decoding:
                //
                //  1. Transport/auth/5xx — Rust's FFI layer emits an
                //     error envelope `{"error":"<msg>"[,"code":...]}` via
                //     `err_json(&e.to_string())`. Must surface as
                //     AmbaApiError so callers branch on `.Code`.
                //
                //  2. PingResult success/server-side-failure envelope —
                //     `{"ok":..., "server_project_id":..., "key_fingerprint":...}`.
                //     Even on `ok=false` this still has `key_fingerprint`
                //     populated. The wrapper surfaces this as a structured
                //     `PingResult`, never as a throw.
                //
                // Discriminator: a PingResult ALWAYS has `key_fingerprint`
                // (always populated server-side; see diagnostics.ts).
                // A bare error envelope has only `error` (and maybe `code`,
                // `details`). Parsing as JToken first to inspect keys
                // costs one allocation but avoids the ambiguity of
                // `Newtonsoft.JsonConvert.DeserializeObject<PingResult>`
                // happily mapping an error envelope into a default
                // PingResult with Error=<msg>.
                JToken token;
                try
                {
                    token = JToken.Parse(raw);
                }
                catch (JsonException e)
                {
                    DiagnosticsLog.Error($"ping: invalid JSON — {e.Message}");
                    throw new AmbaApiError("UNKNOWN_ERROR", $"invalid JSON: {raw}");
                }

                // Error-envelope path: object with `error` string and NO
                // `key_fingerprint`. `JsonUtil.MaybeThrow` does the
                // structured throw (code + details inference).
                if (token.Type == JTokenType.Object
                    && token["key_fingerprint"] == null
                    && token["error"] is JValue err
                    && err.Type == JTokenType.String)
                {
                    JsonUtil.MaybeThrow(raw);
                    // Defensive — MaybeThrow should have thrown above.
                    throw new AmbaApiError("UNKNOWN_ERROR", (string)err);
                }

                PingResult result;
                try
                {
                    result = token.ToObject<PingResult>();
                }
                catch (JsonException e)
                {
                    DiagnosticsLog.Error($"ping: decode failed — {e.Message}");
                    throw new AmbaApiError("UNKNOWN_ERROR", $"decode: {e.Message}");
                }

                if (result.Ok)
                {
                    DiagnosticsLog.Info(
                        $"ping ok project={result.ServerProjectId} env={result.Environment} " +
                        $"key={result.KeyFingerprint} latency_ms={result.LatencyMs}");
                }
                else
                {
                    DiagnosticsLog.Error(
                        $"ping not-ok error={result.Error ?? "unknown"} " +
                        $"project={result.ServerProjectId} key={result.KeyFingerprint}");
                }
                return Task.FromResult(result);
            }
            catch (AmbaApiError api)
            {
                // Don't swallow — log first so Unity devs see the failure
                // in their Console, then re-throw so caller can branch.
                DiagnosticsLog.Error($"ping failed code={api.Code} message={api.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Platform-idiomatic logging shim for diagnostics. Under Unity
    /// (UNITY_5_3_OR_NEWER) forwards to <c>UnityEngine.Debug</c> so
    /// messages show up in the Editor Console. Under pure-managed
    /// test contexts (where Unity is unavailable, e.g. `dotnet test`)
    /// writes to <c>System.Console.Error</c> so the messages still
    /// surface in CI logs.
    ///
    /// Internal so customers don't depend on the surface — the public
    /// log story is "we log via UnityEngine.Debug, prefixed [Amba SDK]".
    /// </summary>
    internal static class DiagnosticsLog
    {
        private const string Prefix = "[Amba SDK]";

        public static void Info(string message)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.Log($"{Prefix} {message}");
#else
            System.Console.Error.WriteLine($"{Prefix} {message}");
#endif
        }

        public static void Error(string message)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogError($"{Prefix} {message}");
#else
            System.Console.Error.WriteLine($"{Prefix} ERROR {message}");
#endif
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Typed data models — opt-in decoding of common payloads
    //
    // Most namespace methods return `JToken` (raw JSON) so callers can
    // shape-shift without round-tripping through generated types. These
    // typed models exist for the handful of payloads where the field set
    // is stable enough to deserve a strongly-typed surface — mirrors the
    // TypeScript SDK's `Streak` / `XpBalance` / `Session` shapes.
    //
    // Decode with `token.ToObject<Streak>()` — Newtonsoft is already a
    // dependency, and `[JsonProperty]` aliases keep the C# property
    // names idiomatic (PascalCase) while accepting the wire JSON
    // (snake_case).
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Daily-engagement streak. Mirrors `Streak` in the TypeScript SDK.</summary>
    public class Streak
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("key")] public string Key;
        [JsonProperty("name")] public string Name;
        [JsonProperty("current_length")] public uint CurrentLength;
        [JsonProperty("longest_length")] public uint LongestLength;
        [JsonProperty("last_qualified_on")] public string LastQualifiedOn;
        [JsonProperty("updated_at")] public string UpdatedAt;
        /// <summary>
        /// Server-computed lifecycle state: <c>"active"</c>, <c>"qualified_today"</c>,
        /// <c>"broken"</c>, or <c>"frozen"</c>. Empty string when decoded against
        /// an older server that didn't emit it.
        /// </summary>
        [JsonProperty("status")] public string Status;
        /// <summary>
        /// Unused streak-freeze count (shields). Auto-granted by the server
        /// every <c>freezes_per_n_events</c> qualifying events.
        /// </summary>
        [JsonProperty("freezes_remaining")] public uint FreezesRemaining;
    }

    /// <summary>XP balance + level snapshot. Mirrors `XpBalance` in the TypeScript SDK.</summary>
    public class XpBalance
    {
        [JsonProperty("user_id")] public string UserId;
        [JsonProperty("total_xp")] public long TotalXp;
        [JsonProperty("current_level")] public uint CurrentLevel;
        [JsonProperty("xp_into_level")] public long XpIntoLevel;
        [JsonProperty("xp_to_next_level")] public long XpToNextLevel;
        [JsonProperty("updated_at")] public string UpdatedAt;
        /// <summary>
        /// XP earned in the current rolling window. Window defined by the
        /// project's XP rules. Older servers without the field decode as 0.
        /// </summary>
        [JsonProperty("xp_this_period")] public long XpThisPeriod;
    }

    /// <summary>
    /// Authenticated session snapshot. Mirrors `Session` in the TypeScript
    /// SDK. The Unity SDK keeps session/refresh tokens inside the Rust
    /// core; the strings on this snapshot are empty on the consumer side.
    /// </summary>
    public class Session
    {
        [JsonProperty("session_token")] public string SessionToken;
        [JsonProperty("refresh_token")] public string RefreshToken;
        [JsonProperty("user")] public JToken User;
        [JsonProperty("expires_at")] public string ExpiresAt;
    }

    // ═══════════════════════════════════════════════════════════════
    // FFI surface — public so tests / custom transports can implement it
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Mirror of the Rust core's C-ABI, 1:1 with the symbols in
    /// <c>core/src/ffi.rs</c>. The default implementation is
    /// <see cref="DefaultNativeMethods"/> which P/Invokes into
    /// <c>libamba_core</c>. Tests / advanced consumers implement this
    /// directly and pass it to <see cref="AmbaClient"/>.
    /// </summary>
    public interface INativeMethods
    {
        IntPtr amba_init(IntPtr configJson);
        IntPtr amba_reset();
        IntPtr amba_anonymous_id();
        IntPtr amba_app_user_id();
        uint amba_is_authenticated();
        void amba_string_free(IntPtr ptr);
        void amba_set_debug(uint enabled);

        IntPtr amba_events_track(IntPtr eventName, IntPtr propertiesJson);

        IntPtr amba_auth_sign_in_anonymously();
        IntPtr amba_auth_sign_in_with_email(IntPtr email, IntPtr password);
        IntPtr amba_auth_sign_up_with_email(IntPtr email, IntPtr password);
        IntPtr amba_auth_sign_in_with_social(IntPtr provider, IntPtr idToken);
        IntPtr amba_auth_request_email_otp(IntPtr email);
        IntPtr amba_auth_verify_email_otp(IntPtr email, IntPtr code);
        IntPtr amba_auth_request_sms_otp(IntPtr phone);
        IntPtr amba_auth_verify_sms_otp(IntPtr phone, IntPtr code);
        IntPtr amba_auth_sign_out(uint rotateAnonymousId);
        IntPtr amba_auth_refresh();
        IntPtr amba_auth_me();
        IntPtr amba_auth_request_magic_link(IntPtr email);
        IntPtr amba_auth_verify_magic_link(IntPtr token);
        IntPtr amba_auth_link_account(IntPtr provider, IntPtr credential);

        // ── users / sessions / sync / leagues (4.0) ──
        IntPtr amba_users_get(IntPtr userId);
        IntPtr amba_users_update(IntPtr userId, IntPtr patchJson);

        IntPtr amba_sessions_list();
        IntPtr amba_sessions_revoke(IntPtr sessionId);

        IntPtr amba_sync_push_changes(IntPtr changesJson);
        IntPtr amba_sync_pull_changes(IntPtr sinceJson);

        IntPtr amba_leagues_me();
        IntPtr amba_leagues_cohort();

        IntPtr amba_collections_find(IntPtr collection, IntPtr optionsJson);
        IntPtr amba_collections_find_one(IntPtr collection, IntPtr id);
        IntPtr amba_collections_insert(IntPtr collection, IntPtr rowJson);
        IntPtr amba_collections_update(IntPtr collection, IntPtr id, IntPtr setJson);
        IntPtr amba_collections_delete(IntPtr collection, IntPtr id);
        IntPtr amba_collections_find_nearest(IntPtr collection, IntPtr optionsJson);
        IntPtr amba_collections_count(IntPtr collection, IntPtr filterJson);

        IntPtr amba_storage_presign(IntPtr bucket, IntPtr filename, IntPtr mimeType, ulong sizeBytes, int retentionDays);
        IntPtr amba_storage_commit(IntPtr uploadId, IntPtr assetId);
        IntPtr amba_storage_list(IntPtr prefix);
        IntPtr amba_storage_delete(IntPtr assetId);
        IntPtr amba_storage_download(IntPtr assetId);

        IntPtr amba_push_register(IntPtr token, IntPtr platform, IntPtr bundleId);
        IntPtr amba_push_subscribe(IntPtr topic);
        IntPtr amba_push_unregister(IntPtr token);
        IntPtr amba_push_get_tokens();
        IntPtr amba_push_unsubscribe(IntPtr topic);

        IntPtr amba_entitlements_list();
        uint amba_entitlements_has(IntPtr name);

        IntPtr amba_ai_anthropic_messages(IntPtr requestJson);

        IntPtr amba_config_fetch();
        IntPtr amba_flags_fetch();
        IntPtr amba_flags_get(IntPtr key);

        // ── gamification ──
        IntPtr amba_achievements_get_all();
        IntPtr amba_achievements_get_progress();

        IntPtr amba_challenges_get_active();
        IntPtr amba_challenges_get(IntPtr id);
        IntPtr amba_challenges_get_progress(IntPtr id);
        IntPtr amba_challenges_claim(IntPtr id);

        IntPtr amba_currencies_get_balance();
        IntPtr amba_currencies_get_transactions(IntPtr currencyKey);

        IntPtr amba_inventory_get_items();
        IntPtr amba_inventory_get_item(IntPtr id);
        IntPtr amba_inventory_purchase(IntPtr requestJson);
        IntPtr amba_inventory_consume(IntPtr requestJson);

        IntPtr amba_leaderboards_get(IntPtr key);
        IntPtr amba_leaderboards_get_entries(IntPtr key, int limit);
        IntPtr amba_leaderboards_get_my_rank(IntPtr key);

        IntPtr amba_stores_list();
        IntPtr amba_stores_get_purchase_options(IntPtr storeKey);
        IntPtr amba_stores_purchase(IntPtr storeKey, IntPtr purchaseOptionId, IntPtr receiptJson);

        IntPtr amba_xp_get_balance();
        IntPtr amba_xp_get_history(int limit);
        IntPtr amba_xp_claim(IntPtr grantKey);

        IntPtr amba_streaks_get_all();
        IntPtr amba_streaks_qualify(IntPtr streakKey);

        // ── social ──
        IntPtr amba_feeds_get_activity(IntPtr feed, IntPtr cursor);

        IntPtr amba_friends_get_list();
        IntPtr amba_friends_get_friends();
        IntPtr amba_friends_block_user(IntPtr userId);
        IntPtr amba_friends_unblock_user(IntPtr userId);
        IntPtr amba_friends_remove_block(IntPtr friendshipId);
        IntPtr amba_friends_remove_friend(IntPtr userId);
        IntPtr amba_friends_send_request(IntPtr userId);
        IntPtr amba_friends_accept_request(IntPtr friendshipId);
        IntPtr amba_friends_decline_request(IntPtr friendshipId);

        IntPtr amba_groups_create(IntPtr paramsJson);
        IntPtr amba_groups_get(IntPtr id);
        IntPtr amba_groups_update(IntPtr id, IntPtr patchJson);
        IntPtr amba_groups_delete(IntPtr id);
        IntPtr amba_groups_get_members(IntPtr id);
        IntPtr amba_groups_join(IntPtr id);
        IntPtr amba_groups_leave(IntPtr id);
        IntPtr amba_groups_invite(IntPtr id, IntPtr userId);

        IntPtr amba_messaging_get_conversations();
        IntPtr amba_messaging_create_conversation(IntPtr requestJson);
        IntPtr amba_messaging_list_messages(IntPtr conversationId, uint limit, uint offset);
        /// <summary>
        /// Phase A signature: <c>(conversation_id, message_id)</c>. The Rust
        /// core paginates <c>list_messages</c> internally and filters by id.
        /// Returns the message envelope or the literal <c>null</c> when not
        /// found.
        /// </summary>
        IntPtr amba_messaging_get_message(IntPtr conversationId, IntPtr messageId);
        IntPtr amba_messaging_mark_read(IntPtr conversationId);
        IntPtr amba_messaging_send_message(IntPtr requestJson);

        IntPtr amba_moderation_report_user(IntPtr requestJson);
        IntPtr amba_moderation_report_content(IntPtr requestJson);
        IntPtr amba_moderation_get_report_status(IntPtr id);

        IntPtr amba_reviews_list(IntPtr targetType, IntPtr targetId);
        IntPtr amba_reviews_create(IntPtr paramsJson);
        IntPtr amba_reviews_update(IntPtr id, IntPtr patchJson);
        IntPtr amba_reviews_delete(IntPtr id);

        IntPtr amba_roles_get_my_roles();
        uint amba_roles_has_permission(IntPtr permission);

        IntPtr amba_referrals_get_referral_code();
        IntPtr amba_referrals_claim_referral(IntPtr code);
        IntPtr amba_referrals_create(IntPtr code, int maxUses);

        // ── lifecycle ──
        IntPtr amba_catalog_list();
        IntPtr amba_catalog_get(IntPtr itemId);

        IntPtr amba_content_get_today(IntPtr channel);
        /// <summary>
        /// Phase A: signature is `(channel, limit: u32, cursor)`. `limit == 0`
        /// means "no limit" — null cursor means "first page".
        /// </summary>
        IntPtr amba_content_get_library(IntPtr channel, uint limit, IntPtr cursor);
        IntPtr amba_content_get_item(IntPtr id);
        IntPtr amba_content_update_item(IntPtr id, IntPtr stateJson);
        IntPtr amba_content_create_item(IntPtr channel, IntPtr itemJson);

        IntPtr amba_deep_links_get(IntPtr shortCode);
        IntPtr amba_deep_links_create(IntPtr paramsJson);

        IntPtr amba_onboarding_get_status();
        IntPtr amba_onboarding_next_step(IntPtr payloadJson);
        IntPtr amba_onboarding_skip_step();
        IntPtr amba_onboarding_complete();

        // ── diagnostics ──
        IntPtr amba_diagnostics_ping();
    }

    /// <summary>
    /// Production <see cref="INativeMethods"/> — forwards every call to the
    /// matching <c>[DllImport]</c> stub in <see cref="NativeMethods"/>.
    /// Used by <see cref="Amba.ConfigureAsync"/>.
    /// </summary>
    public sealed class DefaultNativeMethods : INativeMethods
    {
        public IntPtr amba_init(IntPtr configJson) => NativeMethods.amba_init(configJson);
        public IntPtr amba_reset() => NativeMethods.amba_reset();
        public IntPtr amba_anonymous_id() => NativeMethods.amba_anonymous_id();
        public IntPtr amba_app_user_id() => NativeMethods.amba_app_user_id();
        public uint amba_is_authenticated() => NativeMethods.amba_is_authenticated();
        public void amba_string_free(IntPtr ptr) => NativeMethods.amba_string_free(ptr);
        public void amba_set_debug(uint enabled) => NativeMethods.amba_set_debug(enabled);

        public IntPtr amba_events_track(IntPtr eventName, IntPtr propertiesJson) =>
            NativeMethods.amba_events_track(eventName, propertiesJson);

        public IntPtr amba_auth_sign_in_anonymously() => NativeMethods.amba_auth_sign_in_anonymously();
        public IntPtr amba_auth_sign_in_with_email(IntPtr email, IntPtr password) =>
            NativeMethods.amba_auth_sign_in_with_email(email, password);
        public IntPtr amba_auth_sign_up_with_email(IntPtr email, IntPtr password) =>
            NativeMethods.amba_auth_sign_up_with_email(email, password);
        public IntPtr amba_auth_sign_in_with_social(IntPtr provider, IntPtr idToken) =>
            NativeMethods.amba_auth_sign_in_with_social(provider, idToken);
        public IntPtr amba_auth_request_email_otp(IntPtr email) =>
            NativeMethods.amba_auth_request_email_otp(email);
        public IntPtr amba_auth_verify_email_otp(IntPtr email, IntPtr code) =>
            NativeMethods.amba_auth_verify_email_otp(email, code);
        public IntPtr amba_auth_request_sms_otp(IntPtr phone) =>
            NativeMethods.amba_auth_request_sms_otp(phone);
        public IntPtr amba_auth_verify_sms_otp(IntPtr phone, IntPtr code) =>
            NativeMethods.amba_auth_verify_sms_otp(phone, code);
        public IntPtr amba_auth_sign_out(uint rotateAnonymousId) =>
            NativeMethods.amba_auth_sign_out(rotateAnonymousId);
        public IntPtr amba_auth_refresh() => NativeMethods.amba_auth_refresh();
        public IntPtr amba_auth_me() => NativeMethods.amba_auth_me();
        public IntPtr amba_auth_request_magic_link(IntPtr email) =>
            NativeMethods.amba_auth_request_magic_link(email);
        public IntPtr amba_auth_verify_magic_link(IntPtr token) =>
            NativeMethods.amba_auth_verify_magic_link(token);
        public IntPtr amba_auth_link_account(IntPtr provider, IntPtr credential) =>
            NativeMethods.amba_auth_link_account(provider, credential);

        // ── users / sessions / sync / leagues (4.0) ──
        public IntPtr amba_users_get(IntPtr userId) => NativeMethods.amba_users_get(userId);
        public IntPtr amba_users_update(IntPtr userId, IntPtr patchJson) =>
            NativeMethods.amba_users_update(userId, patchJson);

        public IntPtr amba_sessions_list() => NativeMethods.amba_sessions_list();
        public IntPtr amba_sessions_revoke(IntPtr sessionId) =>
            NativeMethods.amba_sessions_revoke(sessionId);

        public IntPtr amba_sync_push_changes(IntPtr changesJson) =>
            NativeMethods.amba_sync_push_changes(changesJson);
        public IntPtr amba_sync_pull_changes(IntPtr sinceJson) =>
            NativeMethods.amba_sync_pull_changes(sinceJson);

        public IntPtr amba_leagues_me() => NativeMethods.amba_leagues_me();
        public IntPtr amba_leagues_cohort() => NativeMethods.amba_leagues_cohort();

        public IntPtr amba_collections_find(IntPtr collection, IntPtr optionsJson) =>
            NativeMethods.amba_collections_find(collection, optionsJson);
        public IntPtr amba_collections_find_one(IntPtr collection, IntPtr id) =>
            NativeMethods.amba_collections_find_one(collection, id);
        public IntPtr amba_collections_insert(IntPtr collection, IntPtr rowJson) =>
            NativeMethods.amba_collections_insert(collection, rowJson);
        public IntPtr amba_collections_update(IntPtr collection, IntPtr id, IntPtr setJson) =>
            NativeMethods.amba_collections_update(collection, id, setJson);
        public IntPtr amba_collections_delete(IntPtr collection, IntPtr id) =>
            NativeMethods.amba_collections_delete(collection, id);
        public IntPtr amba_collections_find_nearest(IntPtr collection, IntPtr optionsJson) =>
            NativeMethods.amba_collections_find_nearest(collection, optionsJson);
        public IntPtr amba_collections_count(IntPtr collection, IntPtr filterJson) =>
            NativeMethods.amba_collections_count(collection, filterJson);

        public IntPtr amba_storage_presign(IntPtr bucket, IntPtr filename, IntPtr mimeType, ulong sizeBytes, int retentionDays) =>
            NativeMethods.amba_storage_presign(bucket, filename, mimeType, sizeBytes, retentionDays);
        public IntPtr amba_storage_commit(IntPtr uploadId, IntPtr assetId) =>
            NativeMethods.amba_storage_commit(uploadId, assetId);
        public IntPtr amba_storage_list(IntPtr prefix) => NativeMethods.amba_storage_list(prefix);
        public IntPtr amba_storage_delete(IntPtr assetId) => NativeMethods.amba_storage_delete(assetId);
        public IntPtr amba_storage_download(IntPtr assetId) => NativeMethods.amba_storage_download(assetId);

        public IntPtr amba_push_register(IntPtr token, IntPtr platform, IntPtr bundleId) =>
            NativeMethods.amba_push_register(token, platform, bundleId);
        public IntPtr amba_push_subscribe(IntPtr topic) =>
            NativeMethods.amba_push_subscribe(topic);
        public IntPtr amba_push_unregister(IntPtr token) =>
            NativeMethods.amba_push_unregister(token);
        public IntPtr amba_push_get_tokens() => NativeMethods.amba_push_get_tokens();
        public IntPtr amba_push_unsubscribe(IntPtr topic) =>
            NativeMethods.amba_push_unsubscribe(topic);

        public IntPtr amba_entitlements_list() => NativeMethods.amba_entitlements_list();
        public uint amba_entitlements_has(IntPtr name) => NativeMethods.amba_entitlements_has(name);

        public IntPtr amba_ai_anthropic_messages(IntPtr requestJson) =>
            NativeMethods.amba_ai_anthropic_messages(requestJson);

        public IntPtr amba_config_fetch() => NativeMethods.amba_config_fetch();
        public IntPtr amba_flags_fetch() => NativeMethods.amba_flags_fetch();
        public IntPtr amba_flags_get(IntPtr key) => NativeMethods.amba_flags_get(key);

        // ── gamification ──
        public IntPtr amba_achievements_get_all() => NativeMethods.amba_achievements_get_all();
        public IntPtr amba_achievements_get_progress() => NativeMethods.amba_achievements_get_progress();

        public IntPtr amba_challenges_get_active() => NativeMethods.amba_challenges_get_active();
        public IntPtr amba_challenges_get(IntPtr id) => NativeMethods.amba_challenges_get(id);
        public IntPtr amba_challenges_get_progress(IntPtr id) => NativeMethods.amba_challenges_get_progress(id);
        public IntPtr amba_challenges_claim(IntPtr id) => NativeMethods.amba_challenges_claim(id);

        public IntPtr amba_currencies_get_balance() => NativeMethods.amba_currencies_get_balance();
        public IntPtr amba_currencies_get_transactions(IntPtr currencyKey) => NativeMethods.amba_currencies_get_transactions(currencyKey);

        public IntPtr amba_inventory_get_items() => NativeMethods.amba_inventory_get_items();
        public IntPtr amba_inventory_get_item(IntPtr id) => NativeMethods.amba_inventory_get_item(id);
        public IntPtr amba_inventory_purchase(IntPtr requestJson) => NativeMethods.amba_inventory_purchase(requestJson);
        public IntPtr amba_inventory_consume(IntPtr requestJson) => NativeMethods.amba_inventory_consume(requestJson);

        public IntPtr amba_leaderboards_get(IntPtr key) => NativeMethods.amba_leaderboards_get(key);
        public IntPtr amba_leaderboards_get_entries(IntPtr key, int limit) => NativeMethods.amba_leaderboards_get_entries(key, limit);
        public IntPtr amba_leaderboards_get_my_rank(IntPtr key) => NativeMethods.amba_leaderboards_get_my_rank(key);

        public IntPtr amba_stores_list() => NativeMethods.amba_stores_list();
        public IntPtr amba_stores_get_purchase_options(IntPtr storeKey) => NativeMethods.amba_stores_get_purchase_options(storeKey);
        public IntPtr amba_stores_purchase(IntPtr storeKey, IntPtr purchaseOptionId, IntPtr receiptJson) =>
            NativeMethods.amba_stores_purchase(storeKey, purchaseOptionId, receiptJson);

        public IntPtr amba_xp_get_balance() => NativeMethods.amba_xp_get_balance();
        public IntPtr amba_xp_get_history(int limit) => NativeMethods.amba_xp_get_history(limit);
        public IntPtr amba_xp_claim(IntPtr grantKey) => NativeMethods.amba_xp_claim(grantKey);

        public IntPtr amba_streaks_get_all() => NativeMethods.amba_streaks_get_all();
        public IntPtr amba_streaks_qualify(IntPtr streakKey) => NativeMethods.amba_streaks_qualify(streakKey);

        // ── social ──
        public IntPtr amba_feeds_get_activity(IntPtr feed, IntPtr cursor) =>
            NativeMethods.amba_feeds_get_activity(feed, cursor);

        public IntPtr amba_friends_get_list() => NativeMethods.amba_friends_get_list();
        public IntPtr amba_friends_get_friends() => NativeMethods.amba_friends_get_friends();
        public IntPtr amba_friends_block_user(IntPtr userId) => NativeMethods.amba_friends_block_user(userId);
        public IntPtr amba_friends_unblock_user(IntPtr userId) => NativeMethods.amba_friends_unblock_user(userId);
        public IntPtr amba_friends_remove_block(IntPtr friendshipId) => NativeMethods.amba_friends_remove_block(friendshipId);
        public IntPtr amba_friends_remove_friend(IntPtr userId) => NativeMethods.amba_friends_remove_friend(userId);
        public IntPtr amba_friends_send_request(IntPtr userId) => NativeMethods.amba_friends_send_request(userId);
        public IntPtr amba_friends_accept_request(IntPtr friendshipId) => NativeMethods.amba_friends_accept_request(friendshipId);
        public IntPtr amba_friends_decline_request(IntPtr friendshipId) => NativeMethods.amba_friends_decline_request(friendshipId);

        public IntPtr amba_groups_create(IntPtr paramsJson) => NativeMethods.amba_groups_create(paramsJson);
        public IntPtr amba_groups_get(IntPtr id) => NativeMethods.amba_groups_get(id);
        public IntPtr amba_groups_update(IntPtr id, IntPtr patchJson) => NativeMethods.amba_groups_update(id, patchJson);
        public IntPtr amba_groups_delete(IntPtr id) => NativeMethods.amba_groups_delete(id);
        public IntPtr amba_groups_get_members(IntPtr id) => NativeMethods.amba_groups_get_members(id);
        public IntPtr amba_groups_join(IntPtr id) => NativeMethods.amba_groups_join(id);
        public IntPtr amba_groups_leave(IntPtr id) => NativeMethods.amba_groups_leave(id);
        public IntPtr amba_groups_invite(IntPtr id, IntPtr userId) => NativeMethods.amba_groups_invite(id, userId);

        public IntPtr amba_messaging_get_conversations() => NativeMethods.amba_messaging_get_conversations();
        public IntPtr amba_messaging_create_conversation(IntPtr requestJson) =>
            NativeMethods.amba_messaging_create_conversation(requestJson);
        public IntPtr amba_messaging_list_messages(IntPtr conversationId, uint limit, uint offset) =>
            NativeMethods.amba_messaging_list_messages(conversationId, limit, offset);
        public IntPtr amba_messaging_get_message(IntPtr conversationId, IntPtr messageId) =>
            NativeMethods.amba_messaging_get_message(conversationId, messageId);
        public IntPtr amba_messaging_mark_read(IntPtr conversationId) =>
            NativeMethods.amba_messaging_mark_read(conversationId);
        public IntPtr amba_messaging_send_message(IntPtr requestJson) => NativeMethods.amba_messaging_send_message(requestJson);

        public IntPtr amba_moderation_report_user(IntPtr requestJson) => NativeMethods.amba_moderation_report_user(requestJson);
        public IntPtr amba_moderation_report_content(IntPtr requestJson) => NativeMethods.amba_moderation_report_content(requestJson);
        public IntPtr amba_moderation_get_report_status(IntPtr id) => NativeMethods.amba_moderation_get_report_status(id);

        public IntPtr amba_reviews_list(IntPtr targetType, IntPtr targetId) => NativeMethods.amba_reviews_list(targetType, targetId);
        public IntPtr amba_reviews_create(IntPtr paramsJson) => NativeMethods.amba_reviews_create(paramsJson);
        public IntPtr amba_reviews_update(IntPtr id, IntPtr patchJson) => NativeMethods.amba_reviews_update(id, patchJson);
        public IntPtr amba_reviews_delete(IntPtr id) => NativeMethods.amba_reviews_delete(id);

        public IntPtr amba_roles_get_my_roles() => NativeMethods.amba_roles_get_my_roles();
        public uint amba_roles_has_permission(IntPtr permission) => NativeMethods.amba_roles_has_permission(permission);

        public IntPtr amba_referrals_get_referral_code() => NativeMethods.amba_referrals_get_referral_code();
        public IntPtr amba_referrals_claim_referral(IntPtr code) => NativeMethods.amba_referrals_claim_referral(code);
        public IntPtr amba_referrals_create(IntPtr code, int maxUses) => NativeMethods.amba_referrals_create(code, maxUses);

        // ── lifecycle ──
        public IntPtr amba_catalog_list() => NativeMethods.amba_catalog_list();
        public IntPtr amba_catalog_get(IntPtr itemId) => NativeMethods.amba_catalog_get(itemId);

        public IntPtr amba_content_get_today(IntPtr channel) => NativeMethods.amba_content_get_today(channel);
        public IntPtr amba_content_get_library(IntPtr channel, uint limit, IntPtr cursor) =>
            NativeMethods.amba_content_get_library(channel, limit, cursor);
        public IntPtr amba_content_get_item(IntPtr id) => NativeMethods.amba_content_get_item(id);
        public IntPtr amba_content_update_item(IntPtr id, IntPtr stateJson) => NativeMethods.amba_content_update_item(id, stateJson);
        public IntPtr amba_content_create_item(IntPtr channel, IntPtr itemJson) => NativeMethods.amba_content_create_item(channel, itemJson);

        public IntPtr amba_deep_links_get(IntPtr shortCode) => NativeMethods.amba_deep_links_get(shortCode);
        public IntPtr amba_deep_links_create(IntPtr paramsJson) => NativeMethods.amba_deep_links_create(paramsJson);

        public IntPtr amba_onboarding_get_status() => NativeMethods.amba_onboarding_get_status();
        public IntPtr amba_onboarding_next_step(IntPtr payloadJson) => NativeMethods.amba_onboarding_next_step(payloadJson);
        public IntPtr amba_onboarding_skip_step() => NativeMethods.amba_onboarding_skip_step();
        public IntPtr amba_onboarding_complete() => NativeMethods.amba_onboarding_complete();

        // ── diagnostics ──
        public IntPtr amba_diagnostics_ping() => NativeMethods.amba_diagnostics_ping();
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers — pure marshal/parse, no hidden state
    // ═══════════════════════════════════════════════════════════════

    internal static class NativeUtil
    {
        public static IntPtr MarshalUtf8(string s)
        {
            if (s == null) return IntPtr.Zero;
            byte[] utf8 = Encoding.UTF8.GetBytes(s + "\0");
            IntPtr p = Marshal.AllocHGlobal(utf8.Length);
            Marshal.Copy(utf8, 0, p, utf8.Length);
            return p;
        }

        public static string PtrToStringAndFree(IntPtr ptr, INativeMethods bindings)
        {
            if (ptr == IntPtr.Zero) return null;
            string result = Marshal.PtrToStringUTF8(ptr);
            bindings.amba_string_free(ptr);
            return result;
        }

        public static string Invoke(Func<IntPtr, IntPtr> fn, string arg, INativeMethods bindings)
        {
            IntPtr p = MarshalUtf8(arg);
            try { return PtrToStringAndFree(fn(p), bindings); }
            finally { Marshal.FreeHGlobal(p); }
        }

        public static string InvokeUnary(Func<IntPtr, IntPtr> fn, string arg, INativeMethods bindings) =>
            Invoke(fn, arg, bindings);

        public static string InvokeBinary(Func<IntPtr, IntPtr, IntPtr> fn, string a, string b, INativeMethods bindings)
        {
            IntPtr pa = MarshalUtf8(a), pb = MarshalUtf8(b);
            try { return PtrToStringAndFree(fn(pa, pb), bindings); }
            finally
            {
                Marshal.FreeHGlobal(pa);
                Marshal.FreeHGlobal(pb);
            }
        }

        public static string InvokeTernary(Func<IntPtr, IntPtr, IntPtr, IntPtr> fn, string a, string b, string c, INativeMethods bindings)
        {
            IntPtr pa = MarshalUtf8(a), pb = MarshalUtf8(b), pc = MarshalUtf8(c);
            try { return PtrToStringAndFree(fn(pa, pb, pc), bindings); }
            finally
            {
                Marshal.FreeHGlobal(pa);
                Marshal.FreeHGlobal(pb);
                Marshal.FreeHGlobal(pc);
            }
        }

        /// Invoke a C-ABI fn that takes (string, i32). Negative `n` means
        /// "no limit / no value" — Rust treats it as `None`.
        public static string InvokeStringInt(Func<IntPtr, int, IntPtr> fn, string a, int n, INativeMethods bindings)
        {
            IntPtr pa = MarshalUtf8(a);
            try { return PtrToStringAndFree(fn(pa, n), bindings); }
            finally { Marshal.FreeHGlobal(pa); }
        }

        /// Invoke a C-ABI fn that takes (string, u32, string). `limit == 0`
        /// is Rust's "no limit" sentinel on this ABI; null cursor → first
        /// page; null channel → "default" server-side.
        public static string InvokeStringUintString(Func<IntPtr, uint, IntPtr, IntPtr> fn, string a, uint n, string b, INativeMethods bindings)
        {
            IntPtr pa = MarshalUtf8(a), pb = MarshalUtf8(b);
            try { return PtrToStringAndFree(fn(pa, n, pb), bindings); }
            finally
            {
                Marshal.FreeHGlobal(pa);
                Marshal.FreeHGlobal(pb);
            }
        }

        public static string CallReturnString(Func<IntPtr> fn, INativeMethods bindings) =>
            PtrToStringAndFree(fn(), bindings);
    }

    internal static class JsonUtil
    {
        public static JToken ExpectJson(string raw)
        {
            if (raw == null) throw new InvalidOperationException("null response");
            MaybeThrow(raw);
            return JToken.Parse(raw);
        }

        /// <summary>
        /// Inspect <paramref name="raw"/> for an error envelope (a JSON
        /// object with an `error` string) and throw an
        /// <see cref="AmbaApiError"/> with the inferred code if found.
        /// </summary>
        /// <remarks>
        /// Throws <see cref="AmbaApiError"/>, which extends
        /// <see cref="AmbaException"/> — catch blocks written against
        /// the old type still work; new code can catch <see cref="AmbaApiError"/>
        /// and branch on <see cref="AmbaApiError.Code"/>.
        /// </remarks>
        public static void MaybeThrow(string raw)
        {
            try
            {
                var token = JToken.Parse(raw);
                if (token.Type == JTokenType.Object
                    && token["error"] is JValue err
                    && err.Type == JTokenType.String)
                {
                    var msg = (string)err;
                    // Pull `code` and `details` off the envelope when the
                    // server provides them (e.g. structured 4xx replies);
                    // otherwise infer from the message prefix.
                    string code = null;
                    object details = null;
                    if (token["code"] is JValue codeVal && codeVal.Type == JTokenType.String)
                        code = (string)codeVal;
                    if (token["details"] != null)
                        details = token["details"];
                    throw new AmbaApiError(code ?? AmbaApiError.CodeFromMessage(msg), msg, details);
                }
            }
            catch (JsonReaderException) { /* not JSON; treat as opaque */ }
        }

        public static string DecodeError(string raw)
        {
            try
            {
                var token = JToken.Parse(raw);
                if (token.Type == JTokenType.Object
                    && token["error"] is JValue err
                    && err.Type == JTokenType.String)
                {
                    return (string)err;
                }
            }
            catch (JsonReaderException) {}
            return raw;
        }

        /// <summary>
        /// Throw an <see cref="AmbaApiError"/> built from a Rust-side
        /// error envelope. If <paramref name="raw"/> isn't valid JSON or
        /// doesn't carry an `error` field, wrap the whole payload as the
        /// message and tag <c>UNKNOWN_ERROR</c>.
        /// </summary>
        /// <remarks>
        /// Use this at every void-FFI throw site instead of
        /// <c>throw new AmbaException(DecodeError(raw))</c> — the
        /// `AmbaApiError` carries a structured <see cref="AmbaApiError.Code"/>
        /// for callers that want to branch.
        /// </remarks>
        public static void ThrowFromRaw(string raw)
        {
            // MaybeThrow handles the well-formed `{"error":"..."}` envelope.
            // If it didn't throw (e.g. opaque non-JSON body), fall through
            // and wrap the raw text with UNKNOWN_ERROR.
            MaybeThrow(raw);
            throw new AmbaApiError("UNKNOWN_ERROR", raw);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DllImport stubs — only touched by DefaultNativeMethods at runtime
    // ═══════════════════════════════════════════════════════════════

#if UNITY_WEBGL && !UNITY_EDITOR
#error "com.layers.amba does not support WebGL builds — P/Invoke to native code is unavailable on WebGL. Remove the WebGL build target or exclude the amba package from WebGL builds."
#endif

    internal static class NativeMethods
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string LIB = "__Internal";
#else
        private const string LIB = "amba_core";
#endif

        [DllImport(LIB)] public static extern IntPtr amba_init(IntPtr configJson);
        [DllImport(LIB)] public static extern IntPtr amba_reset();
        [DllImport(LIB)] public static extern IntPtr amba_anonymous_id();
        [DllImport(LIB)] public static extern IntPtr amba_app_user_id();
        [DllImport(LIB)] public static extern uint amba_is_authenticated();
        [DllImport(LIB)] public static extern void amba_string_free(IntPtr ptr);
        [DllImport(LIB)] public static extern void amba_set_debug(uint enabled);

        [DllImport(LIB)] public static extern IntPtr amba_events_track(IntPtr eventName, IntPtr propertiesJson);

        [DllImport(LIB)] public static extern IntPtr amba_auth_sign_in_anonymously();
        [DllImport(LIB)] public static extern IntPtr amba_auth_sign_in_with_email(IntPtr email, IntPtr password);
        [DllImport(LIB)] public static extern IntPtr amba_auth_sign_up_with_email(IntPtr email, IntPtr password);
        [DllImport(LIB)] public static extern IntPtr amba_auth_sign_in_with_social(IntPtr provider, IntPtr idToken);
        [DllImport(LIB)] public static extern IntPtr amba_auth_request_email_otp(IntPtr email);
        [DllImport(LIB)] public static extern IntPtr amba_auth_request_sms_otp(IntPtr phone);
        [DllImport(LIB)] public static extern IntPtr amba_auth_verify_sms_otp(IntPtr phone, IntPtr code);
        [DllImport(LIB)] public static extern IntPtr amba_auth_verify_email_otp(IntPtr email, IntPtr code);
        [DllImport(LIB)] public static extern IntPtr amba_auth_sign_out(uint rotateAnonymousId);
        [DllImport(LIB)] public static extern IntPtr amba_auth_refresh();
        [DllImport(LIB)] public static extern IntPtr amba_auth_me();
        [DllImport(LIB)] public static extern IntPtr amba_auth_request_magic_link(IntPtr email);
        [DllImport(LIB)] public static extern IntPtr amba_auth_verify_magic_link(IntPtr token);
        [DllImport(LIB)] public static extern IntPtr amba_auth_link_account(IntPtr provider, IntPtr credential);

        // ── users / sessions / sync / leagues (4.0) ──
        [DllImport(LIB)] public static extern IntPtr amba_users_get(IntPtr userId);
        [DllImport(LIB)] public static extern IntPtr amba_users_update(IntPtr userId, IntPtr patchJson);

        [DllImport(LIB)] public static extern IntPtr amba_sessions_list();
        [DllImport(LIB)] public static extern IntPtr amba_sessions_revoke(IntPtr sessionId);

        [DllImport(LIB)] public static extern IntPtr amba_sync_push_changes(IntPtr changesJson);
        [DllImport(LIB)] public static extern IntPtr amba_sync_pull_changes(IntPtr sinceJson);

        [DllImport(LIB)] public static extern IntPtr amba_leagues_me();
        [DllImport(LIB)] public static extern IntPtr amba_leagues_cohort();

        [DllImport(LIB)] public static extern IntPtr amba_collections_find(IntPtr collection, IntPtr optionsJson);
        [DllImport(LIB)] public static extern IntPtr amba_collections_find_one(IntPtr collection, IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_collections_insert(IntPtr collection, IntPtr rowJson);
        [DllImport(LIB)] public static extern IntPtr amba_collections_update(IntPtr collection, IntPtr id, IntPtr setJson);
        [DllImport(LIB)] public static extern IntPtr amba_collections_delete(IntPtr collection, IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_collections_find_nearest(IntPtr collection, IntPtr optionsJson);
        [DllImport(LIB)] public static extern IntPtr amba_collections_count(IntPtr collection, IntPtr filterJson);

        [DllImport(LIB)] public static extern IntPtr amba_storage_presign(IntPtr bucket, IntPtr filename, IntPtr mimeType, ulong sizeBytes, int retentionDays);
        [DllImport(LIB)] public static extern IntPtr amba_storage_commit(IntPtr uploadId, IntPtr assetId);
        [DllImport(LIB)] public static extern IntPtr amba_storage_list(IntPtr prefix);
        [DllImport(LIB)] public static extern IntPtr amba_storage_delete(IntPtr assetId);
        [DllImport(LIB)] public static extern IntPtr amba_storage_download(IntPtr assetId);

        [DllImport(LIB)] public static extern IntPtr amba_push_register(IntPtr token, IntPtr platform, IntPtr bundleId);
        [DllImport(LIB)] public static extern IntPtr amba_push_subscribe(IntPtr topic);
        [DllImport(LIB)] public static extern IntPtr amba_push_unregister(IntPtr token);
        [DllImport(LIB)] public static extern IntPtr amba_push_get_tokens();
        [DllImport(LIB)] public static extern IntPtr amba_push_unsubscribe(IntPtr topic);

        [DllImport(LIB)] public static extern IntPtr amba_entitlements_list();
        [DllImport(LIB)] public static extern uint amba_entitlements_has(IntPtr name);

        [DllImport(LIB)] public static extern IntPtr amba_ai_anthropic_messages(IntPtr requestJson);

        [DllImport(LIB)] public static extern IntPtr amba_config_fetch();
        [DllImport(LIB)] public static extern IntPtr amba_flags_fetch();
        [DllImport(LIB)] public static extern IntPtr amba_flags_get(IntPtr key);

        // ── gamification ──
        [DllImport(LIB)] public static extern IntPtr amba_achievements_get_all();
        [DllImport(LIB)] public static extern IntPtr amba_achievements_get_progress();

        [DllImport(LIB)] public static extern IntPtr amba_challenges_get_active();
        [DllImport(LIB)] public static extern IntPtr amba_challenges_get(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_challenges_get_progress(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_challenges_claim(IntPtr id);

        [DllImport(LIB)] public static extern IntPtr amba_currencies_get_balance();
        [DllImport(LIB)] public static extern IntPtr amba_currencies_get_transactions(IntPtr currencyKey);

        [DllImport(LIB)] public static extern IntPtr amba_inventory_get_items();
        [DllImport(LIB)] public static extern IntPtr amba_inventory_get_item(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_inventory_purchase(IntPtr requestJson);
        [DllImport(LIB)] public static extern IntPtr amba_inventory_consume(IntPtr requestJson);

        [DllImport(LIB)] public static extern IntPtr amba_leaderboards_get(IntPtr key);
        [DllImport(LIB)] public static extern IntPtr amba_leaderboards_get_entries(IntPtr key, int limit);
        [DllImport(LIB)] public static extern IntPtr amba_leaderboards_get_my_rank(IntPtr key);

        [DllImport(LIB)] public static extern IntPtr amba_stores_list();
        [DllImport(LIB)] public static extern IntPtr amba_stores_get_purchase_options(IntPtr storeKey);
        [DllImport(LIB)] public static extern IntPtr amba_stores_purchase(IntPtr storeKey, IntPtr purchaseOptionId, IntPtr receiptJson);

        [DllImport(LIB)] public static extern IntPtr amba_xp_get_balance();
        [DllImport(LIB)] public static extern IntPtr amba_xp_get_history(int limit);
        [DllImport(LIB)] public static extern IntPtr amba_xp_claim(IntPtr grantKey);

        [DllImport(LIB)] public static extern IntPtr amba_streaks_get_all();
        [DllImport(LIB)] public static extern IntPtr amba_streaks_qualify(IntPtr streakKey);

        // ── social ──
        [DllImport(LIB)] public static extern IntPtr amba_feeds_get_activity(IntPtr feed, IntPtr cursor);

        [DllImport(LIB)] public static extern IntPtr amba_friends_get_list();
        [DllImport(LIB)] public static extern IntPtr amba_friends_get_friends();
        [DllImport(LIB)] public static extern IntPtr amba_friends_block_user(IntPtr userId);
        [DllImport(LIB)] public static extern IntPtr amba_friends_unblock_user(IntPtr userId);
        [DllImport(LIB)] public static extern IntPtr amba_friends_remove_block(IntPtr friendshipId);
        [DllImport(LIB)] public static extern IntPtr amba_friends_remove_friend(IntPtr userId);
        [DllImport(LIB)] public static extern IntPtr amba_friends_send_request(IntPtr userId);
        [DllImport(LIB)] public static extern IntPtr amba_friends_accept_request(IntPtr friendshipId);
        [DllImport(LIB)] public static extern IntPtr amba_friends_decline_request(IntPtr friendshipId);

        [DllImport(LIB)] public static extern IntPtr amba_groups_create(IntPtr paramsJson);
        [DllImport(LIB)] public static extern IntPtr amba_groups_get(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_groups_update(IntPtr id, IntPtr patchJson);
        [DllImport(LIB)] public static extern IntPtr amba_groups_delete(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_groups_get_members(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_groups_join(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_groups_leave(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_groups_invite(IntPtr id, IntPtr userId);

        [DllImport(LIB)] public static extern IntPtr amba_messaging_get_conversations();
        [DllImport(LIB)] public static extern IntPtr amba_messaging_create_conversation(IntPtr requestJson);
        [DllImport(LIB)] public static extern IntPtr amba_messaging_list_messages(IntPtr conversationId, uint limit, uint offset);
        [DllImport(LIB)] public static extern IntPtr amba_messaging_get_message(IntPtr conversationId, IntPtr messageId);
        [DllImport(LIB)] public static extern IntPtr amba_messaging_mark_read(IntPtr conversationId);
        [DllImport(LIB)] public static extern IntPtr amba_messaging_send_message(IntPtr requestJson);

        [DllImport(LIB)] public static extern IntPtr amba_moderation_report_user(IntPtr requestJson);
        [DllImport(LIB)] public static extern IntPtr amba_moderation_report_content(IntPtr requestJson);
        [DllImport(LIB)] public static extern IntPtr amba_moderation_get_report_status(IntPtr id);

        [DllImport(LIB)] public static extern IntPtr amba_reviews_list(IntPtr targetType, IntPtr targetId);
        [DllImport(LIB)] public static extern IntPtr amba_reviews_create(IntPtr paramsJson);
        [DllImport(LIB)] public static extern IntPtr amba_reviews_update(IntPtr id, IntPtr patchJson);
        [DllImport(LIB)] public static extern IntPtr amba_reviews_delete(IntPtr id);

        [DllImport(LIB)] public static extern IntPtr amba_roles_get_my_roles();
        [DllImport(LIB)] public static extern uint amba_roles_has_permission(IntPtr permission);

        [DllImport(LIB)] public static extern IntPtr amba_referrals_get_referral_code();
        [DllImport(LIB)] public static extern IntPtr amba_referrals_claim_referral(IntPtr code);
        [DllImport(LIB)] public static extern IntPtr amba_referrals_create(IntPtr code, int maxUses);

        // ── lifecycle ──
        [DllImport(LIB)] public static extern IntPtr amba_catalog_list();
        [DllImport(LIB)] public static extern IntPtr amba_catalog_get(IntPtr itemId);

        [DllImport(LIB)] public static extern IntPtr amba_content_get_today(IntPtr channel);
        [DllImport(LIB)] public static extern IntPtr amba_content_get_library(IntPtr channel, uint limit, IntPtr cursor);
        [DllImport(LIB)] public static extern IntPtr amba_content_get_item(IntPtr id);
        [DllImport(LIB)] public static extern IntPtr amba_content_update_item(IntPtr id, IntPtr stateJson);
        [DllImport(LIB)] public static extern IntPtr amba_content_create_item(IntPtr channel, IntPtr itemJson);

        [DllImport(LIB)] public static extern IntPtr amba_deep_links_get(IntPtr shortCode);
        [DllImport(LIB)] public static extern IntPtr amba_deep_links_create(IntPtr paramsJson);

        [DllImport(LIB)] public static extern IntPtr amba_onboarding_get_status();
        [DllImport(LIB)] public static extern IntPtr amba_onboarding_next_step(IntPtr payloadJson);
        [DllImport(LIB)] public static extern IntPtr amba_onboarding_skip_step();
        [DllImport(LIB)] public static extern IntPtr amba_onboarding_complete();

        // ── diagnostics ──
        [DllImport(LIB)] public static extern IntPtr amba_diagnostics_ping();
    }
}
