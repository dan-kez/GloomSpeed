using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace GloomSpeed
{
    /// <summary>
    /// Presentation-only pacing changes for Gloomhaven, plus diagnostics. Nothing here sends a game
    /// network message, skips an acknowledgement, or touches ScenarioRuleLibrary rule code or RNG.
    /// </summary>
    [BepInPlugin(Guid, "GloomSpeed", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kez.gloomspeed";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;

        private bool _presenceUnavailable;

        private void Awake()
        {
            Log = Logger;
            Cfg.Bind(Config);

            var harmony = new Harmony(Guid);

            // Applied even with General.Enabled off, so an unmodified session can be timed.
            var (ackOk, ackFailed) = Apply(harmony, new[]
            {
                typeof(AckAbilityLocalPatch),
                typeof(AckTurnLocalPatch),
                typeof(AckRoundLocalPatch),
                typeof(AckAbilityClientPatch),
                typeof(AckTurnClientPatch),
                typeof(AckRoundClientPatch),
            });
            Log.LogInfo($"GloomSpeed diagnostic patches applied: {ackOk}, failed: {ackFailed}.");

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo("Disabled by config; no patches applied.");
                return;
            }

            var (ok, failed) = Apply(harmony, new[]
            {
                typeof(StartSpeedUpPatch),
                typeof(StopSpeedUpPatch),
                typeof(EndTurnSyncSpeedPatch),
                typeof(EnemyBannerPatch),
                typeof(TweenAnimatePatch),
                typeof(NoAnimWaitPatch),
                typeof(AttackModBarAwakePatch),
                typeof(SmartFocusPatch),
                typeof(CameraAwakePatch),
                typeof(SpeedHotkeyPatch),
                typeof(SimpleLogPatch),
                typeof(DllDebugPatch),
            });
            Log.LogInfo($"GloomSpeed patches applied: {ok}, failed: {failed}.");

            if (Cfg.QuietUnityLog.Value)
            {
                // Same logger the game's own debug panel toggles (Script.GUI.DebugMenu/CheatPanel).
                // Warnings and errors stay on, so desync reports still reach Player.log.
                UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning;
            }
        }

        // Patch each class separately: after a game update one missing target must disable only
        // its own feature, not the whole plugin.
        private static (int ok, int failed) Apply(Harmony harmony, Type[] types)
        {
            int ok = 0, failed = 0;
            foreach (var type in types)
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    Log.LogError($"Patch {type.Name} failed; that feature is off. {e.Message}");
                }
            }
            return (ok, failed);
        }

        private void Update()
        {
            if (_presenceUnavailable)
            {
                return;
            }
            try
            {
                PresenceStatus.Tick();
            }
            catch (Exception e)
            {
                // This build references the Windows Steamworks assembly. Where it can't be resolved,
                // as expected on macOS, only the status feature is lost.
                _presenceUnavailable = true;
                Log.LogWarning("Presence status unavailable on this platform: " + e.Message);
            }
        }

        private void OnGUI()
        {
            PresenceStatus.Draw();
        }

        private void OnApplicationQuit()
        {
            AckLog.Shutdown();
        }
    }

    internal static class Cfg
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Multiplier;
        internal static ConfigEntry<float> EnemyTurnMultiplier;
        internal static ConfigEntry<bool> AlwaysFastForward;
        internal static ConfigEntry<bool> KeepSpeedAtEndOfTurn;
        internal static ConfigEntry<bool> AllowClientHotkey;
        internal static ConfigEntry<bool> SkipEnemyBanner;
        internal static ConfigEntry<float> OtherBannerTimeScale;
        internal static ConfigEntry<int> NoAnimWaitMs;
        internal static ConfigEntry<float> AttackModifierTimeScale;
        internal static ConfigEntry<bool> NoPauseDuringEnemyCamera;
        internal static ConfigEntry<float> CameraFocusSpeedScale;
        internal static ConfigEntry<bool> DisableTweenDebug;
        internal static ConfigEntry<bool> QuietUnityLog;
        internal static ConfigEntry<bool> QuietRuleLog;
        internal static ConfigEntry<bool> AckTimingLog;
        internal static ConfigEntry<bool> PresenceStatus;
        internal static ConfigEntry<bool> PresenceOverlay;

        internal static void Bind(ConfigFile c)
        {
            Enabled = c.Bind("General", "Enabled", true, "Master switch. False applies no patches (restart required).");

            var range = new AcceptableValueRange<float>(1f, 16f);
            Multiplier = c.Bind("Speed", "Multiplier", 5f,
                new ConfigDescription("Fast Forward speed outside enemy turns. The game's own value is 3.", range));
            EnemyTurnMultiplier = c.Bind("Speed", "EnemyTurnMultiplier", 6f,
                new ConfigDescription("Fast Forward speed while the current actor is an enemy. Higher values are more likely to break visuals.", range));
            AlwaysFastForward = c.Bind("Speed", "AlwaysFastForward", true,
                "Treat the Fast Forward option as on. The host waits for the slowest client, so every player should enable this.");
            KeepSpeedAtEndOfTurn = c.Bind("Speed", "KeepSpeedAtEndOfTurn", true,
                "The game forces 1x while waiting for end-of-turn idle and damage-label fades. Keep Fast Forward running through that wait.");
            AllowClientHotkey = c.Bind("Speed", "AllowClientHotkey", true,
                "The Toggle Speed key only works for the host online. Let clients use it too (the setting is local anyway).");

            SkipEnemyBanner = c.Bind("Pacing", "SkipEnemyBanner", true,
                "Skip the 'enemy turn' banner, which holds the enemy turn for 1.3s of real time and ignores Fast Forward.");
            OtherBannerTimeScale = c.Bind("Pacing", "OtherBannerTimeScale", 0.5f,
                new ConfigDescription("Duration scale for the player-turn and round banners. 1 = unchanged.", new AcceptableValueRange<float>(0.1f, 1f)));
            NoAnimWaitMs = c.Bind("Pacing", "NoAnimWaitMs", 200,
                new ConfigDescription("Real-time wait for a step with no animation to play. The game's value is 400. Use 400 to disable.", new AcceptableValueRange<int>(0, 400)));
            AttackModifierTimeScale = c.Bind("Pacing", "AttackModifierTimeScale", 0.5f,
                new ConfigDescription("Duration scale for the attack modifier reveal, which runs in real time and ignores Fast Forward. 1 = unchanged.", new AcceptableValueRange<float>(0.1f, 1f)));
            NoPauseDuringEnemyCamera = c.Bind("Pacing", "NoPauseDuringEnemyCamera", true,
                "During enemy turns, do not stop the game clock while the camera pans to an off-screen target.");
            CameraFocusSpeedScale = c.Bind("Pacing", "CameraFocusSpeedScale", 1.5f,
                new ConfigDescription("Multiplier for the camera's focus speed. 1 = unchanged.", new AcceptableValueRange<float>(1f, 2f)));

            DisableTweenDebug = c.Bind("Logging", "DisableTweenDebug", true,
                "Stop the per-frame 'tween:name:' lines. The game ships these on for its banner.");
            QuietUnityLog = c.Bind("Logging", "QuietUnityLog", true,
                "Drop Info-level lines from Player.log; warnings and errors remain. Set false when reporting a bug.");
            QuietRuleLog = c.Bind("Logging", "QuietRuleLog", true,
                "Skip the rule library's Info and SimpleLog messages, including their stack trace capture on the rule thread. Errors and warnings remain.");

            AckTimingLog = c.Bind("Diagnostics", "AckTimingLog", true,
                "As host, record when each player reaches the end of each ability, turn and round, in BepInEx/GloomSpeed. Works with General.Enabled off.");
            PresenceStatus = c.Bind("Diagnostics", "PresenceStatus", true,
                "Share mod version and speed with Steam friends through Rich Presence, and read theirs. Logged to BepInEx/LogOutput.log.");
            PresenceOverlay = c.Bind("Diagnostics", "PresenceOverlay", true,
                "Show each player's mod status on screen during multiplayer.");
        }
    }
}
