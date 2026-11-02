using System;
using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary;
using SharedLibrary.Logger;
using WorldspaceUI;

namespace GloomSpeed
{
    internal static class Guard
    {
        internal static bool Fields(Type patch, params (Type type, string name)[] fields)
        {
            foreach (var (type, name) in fields)
            {
                if (AccessTools.DeclaredField(type, name) == null)
                {
                    Plugin.Log.LogError($"{patch.Name} skipped: {type.Name}.{name} not found. The game may have updated.");
                    return false;
                }
            }
            return true;
        }
    }

    // ---------------------------------------------------------------- Fast Forward multiplier

    /// <summary>
    /// The game calls StartSpeedUp every frame in its animation wait states, and it compares the clock
    /// with SceneController.GameSpeedIncreaseAmount. So setting that value here is all that is needed.
    /// </summary>
    [HarmonyPatch(typeof(GlobalData), nameof(GlobalData.StartSpeedUp))]
    internal static class StartSpeedUpPatch
    {
        private static readonly AccessTools.FieldRef<GlobalData, bool> Toggle =
            AccessTools.FieldRefAccess<GlobalData, bool>("m_SpeedUpToggle");

        // Skips the patch if an update renames the field, instead of throwing on every call in game.
        private static bool Prepare()
        {
            return Guard.Fields(typeof(StartSpeedUpPatch), (typeof(GlobalData), "m_SpeedUpToggle"));
        }

        private static void Prefix(GlobalData __instance)
        {
            // Field, not property: the property setter calls StartSpeedUp again.
            Toggle(__instance) = Presets.GameToggle;

            var scene = SceneController.Instance;
            if (scene == null)
            {
                return;
            }

            var choreographer = Choreographer.s_Choreographer;
            bool enemyTurn = choreographer != null && choreographer.CurrentActor is CEnemyActor;
            float target = enemyTurn ? Presets.EnemyMultiplier : Presets.Multiplier;
            if (scene.GameSpeedIncreaseAmount != target)
            {
                scene.GameSpeedIncreaseAmount = target;
            }
        }
    }

    /// <summary>
    /// The preset owns the game's Fast Forward option. Anything else that flips it changes the preset,
    /// which then sets the option back through SetQuietly. The exception is the game's Skip Attack key,
    /// which also flips it. A key that skips attacks shouldn't change speed, so that press is ignored.
    /// </summary>
    [HarmonyPatch(typeof(GlobalData), "set_SpeedUpToggle")]
    internal static class SpeedToggleSetterPatch
    {
        private static bool quiet;

        internal static void SetQuietly(GlobalData global, bool value)
        {
            quiet = true;
            try
            {
                global.SpeedUpToggle = value;
            }
            finally
            {
                quiet = false;
            }
        }

        private static bool Prefix(bool value)
        {
            if (quiet)
            {
                return true;
            }
            if (global::InputManager.GetWasPressed(KeyAction.SKIP_ATTACK))
            {
                return false;
            }
            if (value != Presets.GameToggle)
            {
                Cfg.Preset.Value = Presets.FromGameToggle(value);
            }
            return false;
        }
    }

    /// <summary>WaitingForEndTurnSync calls StopSpeedUp every frame. Ignore that while enabled.</summary>
    [HarmonyPatch(typeof(GlobalData), nameof(GlobalData.StopSpeedUp))]
    internal static class StopSpeedUpPatch
    {
        private static bool Prefix()
        {
            return !(Presets.Active && Cfg.KeepSpeedAtEndOfTurn.Value && InEndTurnSync());
        }

        private static bool InEndTurnSync()
        {
            var choreographer = Choreographer.s_Choreographer;
            return choreographer != null
                && choreographer.m_WaitState != null
                && choreographer.m_WaitState.m_State == Choreographer.ChoreographerStateType.WaitingForEndTurnSync;
        }
    }

    /// <summary>
    /// Entering the sync: the EndTurn message stopped the speed-up just before this call, so restart it.
    /// Leaving the sync: stop it again, which is the state the game expects afterwards. This is here rather
    /// than on TurnEndSyncedAndReadyToProceed because that method is small enough for Mono to inline.
    /// </summary>
    [HarmonyPatch(typeof(Choreographer), nameof(Choreographer.SetChoreographerState))]
    internal static class EndTurnSyncSpeedPatch
    {
        private const Choreographer.ChoreographerStateType Sync = Choreographer.ChoreographerStateType.WaitingForEndTurnSync;

        private static void Prefix(Choreographer __instance, out bool __state)
        {
            __state = __instance.m_WaitState != null && __instance.m_WaitState.m_State == Sync;
        }

        private static void Postfix(Choreographer.ChoreographerStateType eState, bool __state)
        {
            if (!Presets.Active || !Cfg.KeepSpeedAtEndOfTurn.Value)
            {
                return;
            }
            if (eState == Sync)
            {
                SaveData.Instance.Global.StartSpeedUp();
            }
            else if (__state)
            {
                SaveData.Instance.Global.StopSpeedUp();
            }
        }
    }

    /// <summary>The game's Toggle Speed key is host-only online, although the setting is purely local.</summary>
    [HarmonyPatch(typeof(SpeedUpButton), "SwitchSpeed")]
    internal static class SpeedHotkeyPatch
    {
        private static bool Prefix()
        {
            if (!Cfg.AllowClientHotkey.Value)
            {
                return true;
            }
            var global = SaveData.Instance.Global;
            global.SpeedUpToggle = !global.SpeedUpToggle;
            return false;
        }
    }

    // ---------------------------------------------------------------- Delays Fast Forward ignores

    /// <summary>
    /// Takes the path ShowEnemyTurn already uses when the same enemy type acts twice in a row: invoke
    /// onClosed immediately and show nothing. That callback is what starts the enemy's next phase.
    /// </summary>
    [HarmonyPatch(typeof(PhaseBannerHandler), nameof(PhaseBannerHandler.ShowEnemyTurn))]
    internal static class EnemyBannerPatch
    {
        private static bool Prefix(Action onClosed)
        {
            if (!Presets.Active || !Cfg.SkipEnemyBanner.Value)
            {
                return true;
            }
            onClosed?.Invoke();
            return false;
        }
    }

    /// <summary>Turns off shipped tween debug logging and shortens the phase banner tweens.</summary>
    [HarmonyPatch(typeof(LeanTweenGUIAnimator), "Animate")]
    internal static class TweenAnimatePatch
    {
        private static readonly AccessTools.FieldRef<GUIAnimator, bool> EnableDebug =
            AccessTools.FieldRefAccess<GUIAnimator, bool>("enableDebug");

        private static readonly AccessTools.FieldRef<LeanTweenGUIAnimator, List<LTDescr>> InProcess =
            AccessTools.FieldRefAccess<LeanTweenGUIAnimator, List<LTDescr>>("animationsInProcess");

        private static readonly AccessTools.FieldRef<PhaseBannerHandler, GUIAnimator> ShowAnimation =
            AccessTools.FieldRefAccess<PhaseBannerHandler, GUIAnimator>("showAnimation");

        private static bool Prepare()
        {
            return Guard.Fields(typeof(TweenAnimatePatch),
                (typeof(GUIAnimator), "enableDebug"),
                (typeof(LeanTweenGUIAnimator), "animationsInProcess"),
                (typeof(PhaseBannerHandler), "showAnimation"));
        }

        private static void Prefix(LeanTweenGUIAnimator __instance)
        {
            if (Cfg.DisableTweenDebug.Value)
            {
                EnableDebug(__instance) = false;
            }
        }

        private static void Postfix(LeanTweenGUIAnimator __instance)
        {
            float scale = Presets.OtherBannerScale;
            if (scale >= 1f)
            {
                return;
            }
            var banner = Singleton<PhaseBannerHandler>.Instance;
            if (banner == null || !ReferenceEquals(ShowAnimation(banner), __instance))
            {
                return;
            }
            foreach (var tween in InProcess(__instance))
            {
                tween.time *= scale;
                tween.delay *= scale;
            }
        }
    }

    /// <summary>
    /// Steps with no animation wait a fixed 400ms of real time. Only the exact value 400 is changed,
    /// so the 10000ms safety timeouts for steps with a real animation stay as they are.
    /// </summary>
    [HarmonyPatch(typeof(Choreographer), nameof(Choreographer.SetChoreographerState))]
    internal static class NoAnimWaitPatch
    {
        private static void Prefix(ref int waitTickFrame)
        {
            if (Presets.Active && waitTickFrame == 400)
            {
                waitTickFrame = Cfg.NoAnimWaitMs.Value;
            }
        }
    }

    /// <summary>
    /// AttackModBar multiplies every reveal duration by its public testTimeScaler field (shipped as 1).
    /// RollingModDuration is scaled too, because the attack animation freezes for that long per rolling card.
    /// </summary>
    [HarmonyPatch(typeof(AttackModBar), "Awake")]
    internal static class AttackModBarAwakePatch
    {
        private static GlobalSettings.AttackModifierSettings scaledSettings;
        private static float baseRollingDuration;
        private static float baseTimeScaler = -1f;

        private static void Postfix(AttackModBar __instance)
        {
            if (baseTimeScaler < 0f)
            {
                baseTimeScaler = __instance.testTimeScaler;
            }
            __instance.testTimeScaler = baseTimeScaler * Presets.AttackModifierScale;
            ApplyRolling();
        }

        internal static void ApplyAll()
        {
            if (baseTimeScaler >= 0f)
            {
                foreach (var bar in UnityEngine.Object.FindObjectsOfType<AttackModBar>(true))
                {
                    bar.testTimeScaler = baseTimeScaler * Presets.AttackModifierScale;
                }
            }
            ApplyRolling();
        }

        private static void ApplyRolling()
        {
            var settings = GlobalSettings.Instance != null ? GlobalSettings.Instance.m_AttackModifierSettings : null;
            if (settings == null)
            {
                return;
            }
            if (!ReferenceEquals(settings, scaledSettings))
            {
                baseRollingDuration = settings.RollingModDuration;
                scaledSettings = settings;
            }
            settings.RollingModDuration = baseRollingDuration * Presets.AttackModifierScale;
        }
    }

    /// <summary>
    /// With pauseDuringTransition the game clock stops until the camera arrives, and the camera uses
    /// unscaled time, so Fast Forward cannot shorten it.
    /// </summary>
    [HarmonyPatch(typeof(CameraController), nameof(CameraController.SmartFocus))]
    internal static class SmartFocusPatch
    {
        private static void Prefix(ref bool pauseDuringTransition)
        {
            var choreographer = Choreographer.s_Choreographer;
            if (Presets.Active && Cfg.NoPauseDuringEnemyCamera.Value && choreographer != null && choreographer.CurrentActor is CEnemyActor)
            {
                pauseDuringTransition = false;
            }
        }
    }

    [HarmonyPatch(typeof(CameraController), "Awake")]
    internal static class CameraAwakePatch
    {
        private static float baseFocusSpeed;

        private static void Postfix(CameraController __instance)
        {
            baseFocusSpeed = __instance.m_FocusSpeed;
            __instance.m_FocusSpeed = baseFocusSpeed * Presets.CameraFocusScale;
        }

        internal static void Apply()
        {
            var camera = CameraController.s_CameraController;
            if (camera != null && baseFocusSpeed > 0f)
            {
                camera.m_FocusSpeed = baseFocusSpeed * Presets.CameraFocusScale;
            }
        }
    }

    // ---------------------------------------------------------------- Logging

    /// <summary>Every call runs a regex and queues a second copy of the line for the main thread.</summary>
    [HarmonyPatch(typeof(SharedLibrary.SimpleLog.SimpleLog), nameof(SharedLibrary.SimpleLog.SimpleLog.AddToSimpleLog))]
    internal static class SimpleLogPatch
    {
        private static bool Prefix()
        {
            return !Cfg.QuietRuleLog.Value;
        }
    }

    /// <summary>Info messages capture Environment.StackTrace on the rule thread, then discard it.</summary>
    [HarmonyPatch(typeof(DLLDebug), "SendMessage")]
    internal static class DllDebugPatch
    {
        private static bool Prefix(EDLLDebug logType)
        {
            bool routine = logType == EDLLDebug.Info || logType == EDLLDebug.SimpleLog;
            return !(Cfg.QuietRuleLog.Value && routine);
        }
    }
}
