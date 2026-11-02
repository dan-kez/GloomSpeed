using System;
using System.Collections;
using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using HarmonyLib;
using UnityEngine;
using Utilities;

namespace GloomSpeed
{
    /// <summary>
    /// Skips the video and logos, but keeps the steps the game runs after them. Bootstrap only subscribes
    /// to EventCompleted once the scene has loaded, and it stays on this screen until that event fires, so
    /// this waits for the subscriber. If none appears, it plays the normal intro rather than hanging.
    /// </summary>
    [HarmonyPatch(typeof(IntroPlayer), "ShowVideo")]
    internal static class SkipIntroPatch
    {
        private const float SubscriberTimeoutSeconds = 10f;

        private static readonly AccessTools.FieldRef<IntroPlayer, Action> Completed =
            AccessTools.FieldRefAccess<IntroPlayer, Action>("EventCompleted");

        private static readonly AccessTools.FieldRef<IntroPlayer, GameObject> Loading =
            AccessTools.FieldRefAccess<IntroPlayer, GameObject>("_loading");

        private static bool playNormally;

        private static bool Prepare()
        {
            return Guard.Fields(typeof(SkipIntroPatch),
                (typeof(IntroPlayer), "EventCompleted"),
                (typeof(IntroPlayer), "_loading"));
        }

        private static bool Prefix(IntroPlayer __instance)
        {
            if (playNormally || !Cfg.SkipIntro.Value)
            {
                return true;
            }
            __instance.StartCoroutine(Finish(__instance));
            return false;
        }

        private static IEnumerator Finish(IntroPlayer player)
        {
            float giveUpAt = Time.realtimeSinceStartup + SubscriberTimeoutSeconds;
            while (Completed(player) == null)
            {
                if (Time.realtimeSinceStartup > giveUpAt)
                {
                    Plugin.Log.LogWarning("Skip Intro Logos: nothing was waiting for the intro, so it will play normally.");
                    playNormally = true;
                    AccessTools.Method(typeof(IntroPlayer), "ShowVideo").Invoke(player, null);
                    yield break;
                }
                yield return null;
            }
            Loading(player).SetActive(true);
            Completed(player).Invoke();
            InputSystemUtilities.EnableMouseCursor();
            Plugin.Log.LogInfo("Skipped the intro logos.");
        }
    }

    /// <summary>
    /// With no saved Epic login, the game falls back to the account portal, which opens a browser. Stopping
    /// at that point leaves the same state as a player closing that window: not signed in to Epic. An
    /// existing saved login still works, because that attempt comes first.
    /// </summary>
    [HarmonyPatch(typeof(PlatformUserData), "OnAuthLoginComplete")]
    internal static class SkipEpicLoginPatch
    {
        private static readonly AccessTools.FieldRef<PlatformUserData, LoginCredentialType> LoginType =
            AccessTools.FieldRefAccess<PlatformUserData, LoginCredentialType>("m_CurrentLoginType");

        private static bool Prepare()
        {
            return Guard.Fields(typeof(SkipEpicLoginPatch), (typeof(PlatformUserData), "m_CurrentLoginType"));
        }

        private static bool Prefix(PlatformUserData __instance, LoginCallbackInfo loginCallbackInfo)
        {
            if (!Cfg.SkipEpicLogin.Value
                || loginCallbackInfo.ResultCode == Result.Success
                || LoginType(__instance) != LoginCredentialType.PersistentAuth)
            {
                return true;
            }
            __instance.CurrentEOSAuthStatus = PlatformUserData.EPlatformAuthStatus.NotAuthorised;
            Plugin.Log.LogInfo("Skipped the Epic account login window.");
            return false;
        }
    }
}
