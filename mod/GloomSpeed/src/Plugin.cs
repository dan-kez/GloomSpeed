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
        public const string Version = "0.4.0";
        public const string RepoUrl = "https://github.com/dan-kez/gloomspeed";

        internal static ManualLogSource Log;

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

            // A bug fix, not a pacing change, so it stays on with General.Enabled off.
            var (fixOk, fixFailed) = Apply(harmony, new[]
            {
                typeof(LoadingNoticeRememberPatch),
                typeof(LoadingNoticeRestorePatch),
            });
            Log.LogInfo($"GloomSpeed fix patches applied: {fixOk}, failed: {fixFailed}.");

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo("Disabled by config; no patches applied.");
                return;
            }

            var (ok, failed) = Apply(harmony, new[]
            {
                typeof(StartSpeedUpPatch),
                typeof(SpeedToggleSetterPatch),
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
                typeof(SettingsRowPatch),
                typeof(SkipIntroPatch),
                typeof(SkipEpicLoginPatch),
                typeof(HideDlcPromotionPatch),
                typeof(QuestToastsTogetherPatch),
                typeof(NarrationSpeedPatch),
                typeof(NarrationLinePatch),
                typeof(EventScreenPatch),
                typeof(EventUnavailablePatch),
                typeof(EventContinuePatch),
                typeof(EventCompletePatch),
                typeof(EventReplayPatch),
                typeof(EventHiddenPatch),
                typeof(EventPointerPatch),
                typeof(EventHostHoverPatch),
                typeof(EventHostUnhoverPatch),
                typeof(ResultShownPatch),
                typeof(ResultRetryPatch),
                typeof(ResultReturnPatch),
                typeof(ResultProxyRetryPatch),
                typeof(ResultProxyExitPatch),
                typeof(ContinueOncePatch),
                typeof(QuestMapClickPatch),
                typeof(QuestRowClickPatch),
                typeof(QuestDeselectPatch),
                typeof(CityHostSelectPatch),
                typeof(CityHostCancelPatch),
                typeof(MultiplayerMenuStatusPatch),
            });
            Cfg.Preset.SettingChanged += (_, _) => Presets.ApplyLive();
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

        private bool _sessionDead;

        private void Update()
        {
            PresenceStatus.Tick();
            TickSession();
        }

        private void TickSession()
        {
            if (_sessionDead || !Cfg.Enabled.Value)
            {
                return;
            }
            try
            {
                SessionStatus.Poll();
                NarrationAutoAdvance.Tick();
                QuestVotes.Tick();
                Votes.Tick();
                SharedContinue.Tick();
            }
            catch (Exception e)
            {
                _sessionDead = true;
                Log.LogError("Voting and shared Continue stopped: " + e);
            }
        }

        private void OnApplicationQuit()
        {
            AckLog.Shutdown();
        }
    }

    internal static class Cfg
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<SpeedPreset> Preset;
        internal static ConfigEntry<bool> ShareSpeed;
        internal static ConfigEntry<bool> SkipIntro;
        internal static ConfigEntry<bool> SkipEpicLogin;
        internal static ConfigEntry<bool> HideDlcPromotion;
        internal static ConfigEntry<bool> QuestToastsTogether;
        internal static ConfigEntry<bool> Voting;
        internal static ConfigEntry<float> NarrationSpeed;
        internal static ConfigEntry<bool> NarrationAutoAdvance;
        internal static ConfigEntry<bool> SharedContinue;
        internal static ConfigEntry<float> Multiplier;
        internal static ConfigEntry<float> EnemyTurnMultiplier;
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
        internal static ConfigEntry<bool> UiDebugLog;

        internal static void Bind(ConfigFile c)
        {
            Enabled = c.Bind("General", "Enabled", true, "Master switch. False applies no patches and removes the settings row (restart required).");
            Preset = c.Bind("General", "Preset", SpeedPreset.Fastest,
                "Normal, FastForward, Faster or Fastest. Also changed from Options > General in the game, which applies immediately. The Speed values below and the attack reveal scale tune Fastest; the other Pacing values apply to Faster too.");

            ShareSpeed = c.Bind("General", "ShareSpeed", true,
                "As host, set every modded player's Game Speed to yours for the session, so nobody holds the group up. Their saved choice is kept. Also changed from Options > General.");
            SkipIntro = c.Bind("Startup", "SkipIntro", false,
                "Skip the intro video and logos when the game starts. Also changed from Options > General. Takes effect on the next launch.");
            SkipEpicLogin = c.Bind("Startup", "SkipEpicLogin", false,
                "Don't open the Epic account login window when no Epic login is saved. Epic features, such as Epic friends and invites, stay unavailable. Also changed from Options > General. Takes effect on the next launch.");
            HideDlcPromotion = c.Bind("Startup", "HideDlcPromotion", false,
                "Hide the DLC promotion in the top right of the main menu. Also changed from Options > General.");

            var range = new AcceptableValueRange<float>(1f, 16f);
            Multiplier = c.Bind("Speed", "Multiplier", 5f,
                new ConfigDescription("Fast Forward speed outside enemy turns. The game's own value is 3.", range));
            EnemyTurnMultiplier = c.Bind("Speed", "EnemyTurnMultiplier", 6f,
                new ConfigDescription("Fast Forward speed while the current actor is an enemy. Higher values are more likely to break visuals.", range));
            KeepSpeedAtEndOfTurn = c.Bind("Speed", "KeepSpeedAtEndOfTurn", true,
                "The game forces 1x while waiting for end-of-turn idle and damage-label fades. Keep Fast Forward running through that wait.");
            AllowClientHotkey = c.Bind("Speed", "AllowClientHotkey", true,
                "The Toggle Speed key only works for the host online. Let clients use it too (the setting is local anyway).");

            NarrationSpeed = c.Bind("General", "NarrationSpeed", 1f,
                new ConfigDescription("How fast story narration plays, keeping the voice's pitch. Also changed from Options > General.",
                    new AcceptableValueList<float>(0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f)));
            NarrationAutoAdvance = c.Bind("General", "NarrationAutoAdvance", false,
                "Go to the next narrated line when the voice finishes, without a click. Also changed from Options > General.");
            Voting = c.Bind("General", "Voting", true,
                "In multiplayer, let players who aren't the host click an option to show which one they'd pick. The host still decides. Works when the host has this version or newer.");
            SharedContinue = c.Bind("General", "SharedContinue", true,
                "In multiplayer, let any player with the mod press Continue when the enemy cards are revealed. Works when the host has this version or newer.");
            QuestToastsTogether = c.Bind("Pacing", "QuestToastsTogether", true,
                "At Faster and Fastest, show all personal quest progress together after a scenario, rather than one at a time.");
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
                "Share mod version and Game Speed directly with the other players in a session, list them in the multiplayer menu, and warn if they differ. Sent over Steam P2P, separate from the game's network traffic. Nothing is added to your Steam status.");
            UiDebugLog = c.Bind("Diagnostics", "UiDebugLog", false,
                "Write the multiplayer menu's layout to BepInEx/LogOutput.log once per run, for fixing UI problems.");
            PresenceOverlay = c.Bind("Diagnostics", "PresenceOverlay", true,
                "During multiplayer, show a notification if another player's mod version or Game Speed differs, or their mod can't be detected.");
        }
    }
}
