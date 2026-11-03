using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Assets.Script.Misc;
using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using FFSNet;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;
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

        private static readonly AccessTools.FieldRef<IntroPlayer, VideoPlayer> Player =
            AccessTools.FieldRefAccess<IntroPlayer, VideoPlayer>("_player");

        private static readonly AccessTools.FieldRef<IntroPlayer, GameObject> Loading =
            AccessTools.FieldRefAccess<IntroPlayer, GameObject>("_loading");

        private static bool playNormally;

        private static bool Prepare()
        {
            return Guard.Fields(typeof(SkipIntroPatch),
                (typeof(IntroPlayer), "EventCompleted"),
                (typeof(IntroPlayer), "_player"),
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
            // The steps the game takes after its video and logos, in the same order.
            Player(player).gameObject.SetActive(false);
            Loading(player).SetActive(true);
            Completed(player).Invoke();
            InputSystemUtilities.EnableMouseCursor();
            Plugin.Log.LogInfo("Skipped the intro logos.");
        }
    }

    /// <summary>
    /// Stops the main menu showing its DLC promotion. The menu already hides it while Options is open and
    /// asks for it again on leaving, so a change made there applies as soon as Options closes.
    /// </summary>
    [HarmonyPatch(typeof(PromotionDLCManager), nameof(PromotionDLCManager.Show))]
    internal static class HideDlcPromotionPatch
    {
        private static bool Prefix()
        {
            return !Cfg.HideDlcPromotion.Value;
        }
    }

    /// <summary>
    /// After a scenario the game shows each personal quest's progress in turn, starting the next only when
    /// the last has fully hidden. Handing the queue a finished promise lets it start the next straight away,
    /// so they stack in the notification panel. The toast's own cleanup stays on the promise it was given.
    /// The last one reports when every toast has gone, so what follows waits as it did before. Completed
    /// quests open their own window, so one of those still waits for the toasts ahead of it.
    /// </summary>
    [HarmonyPatch(typeof(UIPersonalQuestResultManager), "ProcessPersonalQuestProgressed")]
    internal static class QuestToastsTogetherPatch
    {
        private static readonly FieldInfo Queue = AccessTools.Field(typeof(UIPersonalQuestResultManager), "queue");

        private static readonly FieldInfo IsCompleted =
            AccessTools.Field(AccessTools.Inner(typeof(UIPersonalQuestResultManager), "StackInfo"), "isCompleted");

        private static readonly List<ICallbackPromise> Showing = new List<ICallbackPromise>();

        private static bool Prepare()
        {
            if (Queue != null && IsCompleted != null)
            {
                return true;
            }
            Plugin.Log.LogWarning("Quest toasts will show one at a time: the game's queue was not recognised.");
            return false;
        }

        private static void Postfix(UIPersonalQuestResultManager __instance, ref ICallbackPromise __result)
        {
            if (!Cfg.QuestToastsTogether.Value || !Presets.Active)
            {
                return;
            }
            // The running entry stays first in the queue until its promise resolves.
            var queue = (IList)Queue.GetValue(__instance);
            if (queue.Count > 1 && !(bool)IsCompleted.GetValue(queue[1]))
            {
                Showing.Add(__result);
                __result = CallbackPromise.Resolved();
            }
            else if (Showing.Count > 0)
            {
                Showing.Add(__result);
                __result = WhenAll(Showing);
                Showing.Clear();
            }
        }

        private static ICallbackPromise WhenAll(List<ICallbackPromise> promises)
        {
            var all = new CallbackPromise();
            int remaining = promises.Count;
            foreach (var promise in promises.ToArray())
            {
                promise.Done(() =>
                {
                    remaining--;
                    if (remaining == 0)
                    {
                        all.Resolve();
                    }
                });
            }
            return all;
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

    /// <summary>
    /// Fixes a game bug that strands a client on "Waiting for other players...". A player that loads
    /// quickly announces it, and the slower player can receive that while it's still working through its
    /// queue. Then StartWaitingForPlayers clears the list, wiping the announcement, and it's never resent.
    /// Seen with no pacing patches active: received 14:27:21.788, cleared 14:27:22.936. So keep what
    /// another player announced moments before the clear. Sends nothing and only changes this player's list.
    /// </summary>
    [HarmonyPatch(typeof(PlayerRegistry), nameof(PlayerRegistry.NotifyLoadingFinished))]
    internal static class LoadingNoticeRememberPatch
    {
        internal static readonly Dictionary<int, float> LastNotice = new Dictionary<int, float>();

        private static void Prefix(NetworkPlayer player)
        {
            if (player != null && player != PlayerRegistry.MyPlayer)
            {
                LastNotice[player.PlayerID] = Time.realtimeSinceStartup;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerRegistry), nameof(PlayerRegistry.StartWaitingForPlayers))]
    internal static class LoadingNoticeRestorePatch
    {
        // Long enough for a slow client's queue, and shorter than the time between two real loads.
        private const float KeepSeconds = 20f;

        private static readonly AccessTools.FieldRef<List<NetworkPlayer>> Finished =
            AccessTools.StaticFieldRefAccess<List<NetworkPlayer>>(AccessTools.DeclaredField(typeof(PlayerRegistry), "PlayersFinishedLoading"));

        private static bool Prepare()
        {
            return Guard.Fields(typeof(LoadingNoticeRestorePatch), (typeof(PlayerRegistry), "PlayersFinishedLoading"));
        }

        private static void Postfix()
        {
            try
            {
                var finished = Finished();
                foreach (var notice in LoadingNoticeRememberPatch.LastNotice)
                {
                    if (Time.realtimeSinceStartup - notice.Value > KeepSeconds)
                    {
                        continue;
                    }
                    var player = PlayerRegistry.GetPlayer(notice.Key);
                    if (player != null && !finished.Contains(player))
                    {
                        finished.Add(player);
                        Plugin.Log.LogInfo($"Kept {player.Username}'s loading notice, which arrived before the wait began.");
                    }
                }
                LoadingNoticeRememberPatch.LastNotice.Clear();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not keep the loading notice: " + e);
            }
        }
    }
}
