using System;
using System.Collections.Generic;
using System.Linq;
using FFSNet;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.PhaseManager;
using MapRuleLibrary.YML.Events;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GloomSpeed
{
    /// <summary>
    /// Lets players who aren't the host mark which option they'd pick. It's advice only: votes travel over
    /// the mod's own channel, the host still makes the choice, and nothing here sends a game action. Votes
    /// are resent while a choice is open, so a player whose screen opens late still gets them.
    /// </summary>
    internal enum VotePlacement
    {
        InsideRight,
        Above,
        /// <summary>Tighter to the edge, for a narrow row.</summary>
        NarrowRow,
    }

    internal static class Votes
    {
        internal const string Prefix = "GSV1";
        private const string NoVote = "-";
        private const float ResendSeconds = 2f;
        private const float ExpireSeconds = 7f;
        private const string HolderName = "GloomSpeedVotes";

        private static readonly Dictionary<string, Transform> Rows = new Dictionary<string, Transform>();
        private static readonly Dictionary<ulong, (string Key, string Option, float Time)> Received =
            new Dictionary<ulong, (string, string, float)>();
        private static string _key;
        private static string _mine;
        private static float _nextSend;
        private static VotePlacement _placement;

        internal static bool Available => Cfg.Voting.Value && FFSNetwork.IsOnline && SessionStatus.HostSupportsVoting();

        internal static bool IsOpen(string prefix) => _key != null && _key.StartsWith(prefix, StringComparison.Ordinal);

        /// <summary>Opens a choice, identified the same way on every machine. Reopening it keeps nothing.</summary>
        internal static void Open(string key, Dictionary<string, Transform> rows, VotePlacement placement = VotePlacement.InsideRight)
        {
            if (key == _key)
            {
                return;
            }
            Close();
            if (!Available)
            {
                return;
            }
            _key = key;
            _placement = placement;
            foreach (var row in rows)
            {
                Rows[row.Key] = row.Value;
            }
            _nextSend = 0f;
            Draw();
        }

        /// <summary>Follows a list whose rows come and go while the same choice stays open.</summary>
        internal static void UpdateRows(Dictionary<string, Transform> rows)
        {
            if (rows.Count == Rows.Count && rows.All(row => Rows.TryGetValue(row.Key, out var known) && known == row.Value))
            {
                return;
            }
            foreach (var row in Rows.Values)
            {
                Clear(row);
            }
            Rows.Clear();
            foreach (var row in rows)
            {
                Rows[row.Key] = row.Value;
            }
            Draw();
        }

        internal static bool AnyOpen => _key != null;

        /// <summary>Makes an option the pick, without the second click taking it back.</summary>
        internal static void Set(string option)
        {
            if (_key != null && Rows.ContainsKey(option) && option != _mine)
            {
                _mine = option;
                _nextSend = 0f;
                Draw();
            }
        }

        /// <summary>Takes back the pick if it's this option.</summary>
        internal static void Unset(string option)
        {
            if (_key != null && _mine == option)
            {
                _mine = null;
                _nextSend = 0f;
                Draw();
            }
        }

        internal static void Close()
        {
            foreach (var row in Rows.Values)
            {
                Clear(row);
            }
            Rows.Clear();
            Received.Clear();
            VoteHover.Clear();
            _key = null;
            _mine = null;
        }

        /// <summary>Picks an option, or takes the vote back if it's already the pick.</summary>
        internal static void Cast(string option)
        {
            if (_key == null || !FFSNetwork.IsClient || !Rows.ContainsKey(option))
            {
                return;
            }
            _mine = option == _mine ? null : option;
            _nextSend = 0f;
            Draw();
        }

        internal static void Tick()
        {
            if (_key == null)
            {
                return;
            }
            if (!FFSNetwork.IsOnline)
            {
                Close();
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (_mine != null || _nextSend == 0f)
            {
                if (now >= _nextSend)
                {
                    _nextSend = now + ResendSeconds;
                    SessionStatus.SendToModdedPeers($"{Prefix};{_key};{_mine ?? NoVote}");
                }
            }
            VoteHover.Tick();
            var expired = Received.Where(vote => now - vote.Value.Time > ExpireSeconds).Select(vote => vote.Key).ToList();
            if (expired.Count > 0)
            {
                expired.ForEach(id => Received.Remove(id));
                Draw();
            }
        }

        internal static void Receive(ulong sender, NetworkPlayer player, string[] parts)
        {
            if (parts.Length != 3)
            {
                return;
            }
            bool changed = !Received.TryGetValue(sender, out var before) || before.Key != parts[1] || before.Option != parts[2];
            Received[sender] = (parts[1], parts[2], Time.realtimeSinceStartup);
            if (changed)
            {
                Draw();
            }
        }

        private static void Draw()
        {
            VoteHover.Clear();
            foreach (var row in Rows)
            {
                Clear(row.Value);
                var voters = new List<NetworkPlayer>();
                if (_mine == row.Key && PlayerRegistry.MyPlayer != null)
                {
                    voters.Add(PlayerRegistry.MyPlayer);
                }
                foreach (var vote in Received)
                {
                    if (vote.Value.Key == _key && vote.Value.Option == row.Key)
                    {
                        var player = PlayerRegistry.AllPlayers.FirstOrDefault(p => p.PlatformPlayerId == vote.Key.ToString());
                        if (player != null)
                        {
                            voters.Add(player);
                        }
                    }
                }
                if (voters.Count > 0)
                {
                    VoteBadges.Draw(row.Value, HolderName, voters, row.Key == QuestVotes.CityEvent ? VotePlacement.Above : _placement);
                }
            }
        }

        private static void Clear(Transform row)
        {
            if (row == null)
            {
                return;
            }
            var holder = row.Find(HolderName);
            if (holder != null)
            {
                // Destroy only takes effect at the end of the frame, so it must stop being found now.
                holder.name = HolderName + "Old";
                UnityEngine.Object.Destroy(holder.gameObject);
            }
        }
    }

    /// <summary>Draws each voter's character icons at an option.</summary>
    internal static class VoteBadges
    {
        private const float IconSize = 44f;
        private const float FontSize = 22f;

        internal static void Draw(Transform row, string name, List<NetworkPlayer> voters, VotePlacement placement)
        {
            var holder = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(LayoutElement),
                typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(row, false);
            switch (placement)
            {
                case VotePlacement.Above:
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                    rect.pivot = new Vector2(0.5f, 0f);
                    rect.anchoredPosition = new Vector2(0f, 8f);
                    break;
                case VotePlacement.NarrowRow:
                    rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(1f, 0.5f);
                    rect.anchoredPosition = new Vector2(-4f, 0f);
                    break;
                default:
                    rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(1f, 0.5f);
                    rect.anchoredPosition = new Vector2(-12f, 0f);
                    break;
            }
            holder.GetComponent<LayoutElement>().ignoreLayout = true;
            var group = holder.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var layout = holder.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = holder.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var font = row.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
            foreach (var voter in voters)
            {
                var characters = Characters(voter);
                foreach (var character in characters)
                {
                    VoteHover.Add(AddIcon(rect, UIInfoTools.Instance.GetCharacterMarker(character)),
                        VoteHover.Describe(character, voter));
                }
                // A player with no character yet has no icon to identify them.
                if (characters.Count == 0)
                {
                    AddName(rect, font, voter.Username);
                }
            }
        }

        private static List<CharacterYMLData> Characters(NetworkPlayer player)
        {
            return player.MyParticipatingControllables
                .Select(controllable => controllable.GetCharacter)
                .Where(id => id != "None")
                .Distinct()
                .Select(id => CharacterClassManager.Classes.FirstOrDefault(c => c.CharacterID == id)?.CharacterYML)
                .Where(yml => yml != null)
                .ToList();
        }

        private static RectTransform AddIcon(Transform parent, Sprite sprite)
        {
            var box = new GameObject("Icon", typeof(RectTransform), typeof(LayoutElement));
            box.transform.SetParent(parent, false);
            var size = box.GetComponent<LayoutElement>();
            size.preferredWidth = size.preferredHeight = IconSize;

            var icon = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)icon.transform;
            rect.SetParent(box.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = icon.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return (RectTransform)box.transform;
        }

        private static void AddName(Transform parent, TMP_FontAsset font, string text)
        {
            var label = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(parent, false);
            var tmp = label.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.text = text;
            tmp.fontSize = FontSize;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
        }
    }

    /// <summary>
    /// Names the player when the pointer is over a vote icon. The icons take no pointer events, so that they
    /// can never swallow a click meant for the option underneath, so this just compares positions instead.
    /// </summary>
    internal static class VoteHover
    {
        private static readonly List<(RectTransform Icon, string Text)> Icons = new List<(RectTransform, string)>();
        private const float TooltipWidth = 260f;
        private static RectTransform _shown;
        private static bool _showing;

        internal static void Add(RectTransform icon, string text)
        {
            Icons.Add((icon, text));
        }

        internal static void Clear()
        {
            Icons.Clear();
            Hide();
        }

        /// <summary>Character name then player name, following the game's rule for custom names on clients.</summary>
        internal static string Describe(CharacterYMLData yml, NetworkPlayer voter)
        {
            string name = GLOOM.LocalizationManager.GetTranslation(yml.LocKey);
            if (!FFSNetwork.IsClient || FFSNetwork.IsUGCEnabled)
            {
                var custom = AdventureState.MapState?.MapParty?.SelectedCharacters
                    ?.FirstOrDefault(character => character.CharacterID == yml.ID)?.CharacterName;
                if (!string.IsNullOrEmpty(custom))
                {
                    name = custom;
                }
            }
            return $"{name} ({voter.Username})";
        }

        internal static void Tick()
        {
            var mouse = Mouse.current;
            if (Icons.Count == 0 || mouse == null)
            {
                Hide();
                return;
            }
            Vector2 position = mouse.position.ReadValue();
            foreach (var (icon, text) in Icons)
            {
                if (icon == null || !icon.gameObject.activeInHierarchy)
                {
                    continue;
                }
                var canvas = icon.GetComponentInParent<Canvas>()?.rootCanvas;
                var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (RectTransformUtility.RectangleContainsScreenPoint(icon, position, camera))
                {
                    Show(icon, text);
                    return;
                }
            }
            Hide();
        }

        private static void Hide()
        {
            if (_shown != null || _showing)
            {
                _shown = null;
                _showing = false;
                UITooltip.Hide(0f);
            }
        }

        /// <summary>Uses the game's own tooltip, set up the way its text tooltips set it up.</summary>
        private static void Show(RectTransform icon, string text)
        {
            if (icon == _shown)
            {
                return;
            }
            _shown = icon;
            _showing = true;
            // Custom names go through the game's profanity filter, which can answer later.
            text.GetCensoredStringAsync(censored =>
            {
                if (icon == _shown)
                {
                    Display(icon, censored);
                }
            });
        }

        private static void Display(RectTransform icon, string text)
        {
            UITooltip.ResetContent();
            UITooltip.AddTitle(text);
            // Auto opens diagonally from the icon, so the icon stays in view.
            UITooltip.AnchorToRect(icon, UITooltip.Corner.Auto);
            UITooltip.ShowBackgroundImage(true);
            UITooltip.SetScreenBound(true, 10f);
            UITooltip.SetVerticalControls(true);
            UITooltip.SetWidth(TooltipWidth);
            UITooltip.Show(0f);
        }
    }

    /// <summary>The road and city event panel. Clients replay the host's choice through the same methods.</summary>
    internal static class EventVotes
    {
        private const string KeyPrefix = "event|";

        internal static readonly AccessTools.FieldRef<UIEventPanel, CRoadEvent> Data =
            AccessTools.FieldRefAccess<UIEventPanel, CRoadEvent>("eventData");
        internal static readonly AccessTools.FieldRef<UIEventPanel, CRoadEventScreen> Screen =
            AccessTools.FieldRefAccess<UIEventPanel, CRoadEventScreen>("currentScreen");
        internal static readonly AccessTools.FieldRef<UIEventPanel, List<EventButton>> Buttons =
            AccessTools.FieldRefAccess<UIEventPanel, List<EventButton>>("eventButtons");
        internal static readonly AccessTools.FieldRef<UIEventPanel, bool> Revealing =
            AccessTools.FieldRefAccess<UIEventPanel, bool>("isPlayingShowAnimation");

        internal static readonly HashSet<string> Unavailable = new HashSet<string>();
        internal static bool Replaying;

        internal static bool Open => Votes.IsOpen(KeyPrefix);

        internal static void Refresh(UIEventPanel panel)
        {
            var screen = Screen(panel);
            var data = Data(panel);
            if (data == null || screen == null || screen.End != 0 || screen.Options == null || screen.Options.Count < 2)
            {
                Votes.Close();
                return;
            }
            var buttons = Buttons(panel).Where(button => button != null && !string.IsNullOrEmpty(button.Name)).ToList();
            if (buttons.Count < 2)
            {
                Votes.Close();
                return;
            }
            // The first button's number tells apart two visits to a screen with the same name.
            string key = $"{KeyPrefix}{data.ID}|{screen.Name}|{buttons[0].ID}";
            Votes.Open(key, buttons.Where(button => !Unavailable.Contains(button.Name))
                .GroupBy(button => button.Name)
                .ToDictionary(group => group.Key, group => group.First().transform));
        }

        internal static void TryCast(EventButton button)
        {
            var panel = Singleton<UIEventPanel>.Instance;
            if (Open && panel != null && !Revealing(panel) && button != null)
            {
                Votes.Cast(button.Name);
            }
        }
    }

    [HarmonyPatch(typeof(UIEventPanel), "UpdateScreen")]
    internal static class EventScreenPatch
    {
        private static void Prefix()
        {
            EventVotes.Unavailable.Clear();
        }

        private static void Postfix(UIEventPanel __instance)
        {
            try
            {
                EventVotes.Refresh(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Event voting: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(EventButton), nameof(EventButton.SetUnvailableOption))]
    internal static class EventUnavailablePatch
    {
        private static void Postfix(string optionName)
        {
            EventVotes.Unavailable.Add(optionName);
        }
    }

    /// <summary>
    /// A client must never run a choice itself, because that rolls dice only it would see. Only the host's
    /// replayed choice gets through. The game's own lock can miss a controller's submit press.
    /// </summary>
    [HarmonyPatch(typeof(UIEventPanel), "ContinueEvent")]
    internal static class EventContinuePatch
    {
        private static bool Prefix(EventButton eventButton)
        {
            if (FFSNetwork.IsClient && !EventVotes.Replaying)
            {
                EventVotes.TryCast(eventButton);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(UIEventPanel), "CompleteEvent")]
    internal static class EventCompletePatch
    {
        private static bool Prefix()
        {
            return !FFSNetwork.IsClient || EventVotes.Replaying;
        }
    }

    [HarmonyPatch(typeof(UIEventPanel), nameof(UIEventPanel.ClientContinueRoadEvent))]
    internal static class EventReplayPatch
    {
        private static void Prefix()
        {
            EventVotes.Replaying = true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            EventVotes.Replaying = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(UIEventPanel), "OnHidden")]
    internal static class EventHiddenPatch
    {
        private static void Prefix()
        {
            if (EventVotes.Open)
            {
                Votes.Close();
            }
        }
    }

    /// <summary>A locked button ignores a click, but the click still reaches it, so it can count as a vote.</summary>
    [HarmonyPatch(typeof(ExtendedButton), nameof(ExtendedButton.OnPointerClick))]
    internal static class EventPointerPatch
    {
        private static void Postfix(ExtendedButton __instance, PointerEventData eventData)
        {
            if (!FFSNetwork.IsClient || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            if (EventVotes.Open)
            {
                EventVotes.TryCast(__instance.GetComponentInParent<EventButton>());
            }
            else if (Votes.IsOpen("quest|") && __instance.GetComponentInParent<UICityEncounterButton>() != null)
            {
                Votes.Cast(QuestVotes.CityEvent);
            }
        }
    }

    /// <summary>
    /// After a loss the host picks Retry or Return. Clients get the same two options back, and picking one
    /// only votes. The host's choice reaches them through the game's Proxy methods, not these.
    /// </summary>
    internal static class ResultVotes
    {
        private const string Key = "result|lose";
        internal const string Retry = "retry";
        internal const string Return = "return";

        internal static readonly AccessTools.FieldRef<UIResultsManager, UIResultsOption> RetryOption =
            AccessTools.FieldRefAccess<UIResultsManager, UIResultsOption>("retryOption");
        internal static readonly AccessTools.FieldRef<UIResultsManager, UIResultsOption> ReturnOption =
            AccessTools.FieldRefAccess<UIResultsManager, UIResultsOption>("returnToMapOption");
        internal static readonly AccessTools.FieldRef<UIResultsManager, UIResultsManager.ERetryOptionType> RetryType =
            AccessTools.FieldRefAccess<UIResultsManager, UIResultsManager.ERetryOptionType>("m_Retry");

        internal static bool Open => Votes.IsOpen(Key);

        internal static void Begin(UIResultsManager manager)
        {
            if (!Votes.Available)
            {
                return;
            }
            var retry = RetryOption(manager);
            var back = ReturnOption(manager);
            Votes.Open(Key, new Dictionary<string, Transform> { { Retry, retry.transform }, { Return, back.transform } }, VotePlacement.Above);
            if (FFSNetwork.IsClient && Open)
            {
                retry.SetActive(true);
                back.SetActive(true);
            }
        }
    }

    [HarmonyPatch(typeof(UINewAdventureResultsManager), "SetResult")]
    internal static class ResultShownPatch
    {
        private static void Postfix(UINewAdventureResultsManager __instance, EResult result)
        {
            try
            {
                if (FFSNetwork.IsOnline && result == EResult.Lose
                    && ResultVotes.RetryType(__instance) == UIResultsManager.ERetryOptionType.Additional)
                {
                    ResultVotes.Begin(__instance);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Result voting: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(UINewAdventureResultsManager), "Retry")]
    internal static class ResultRetryPatch
    {
        private static bool Prefix()
        {
            if (!ResultVotes.Open)
            {
                return true;
            }
            if (FFSNetwork.IsClient)
            {
                Votes.Cast(ResultVotes.Retry);
                return false;
            }
            Votes.Close();
            return true;
        }
    }

    [HarmonyPatch(typeof(UINewAdventureResultsManager), "ReturnToMap")]
    internal static class ResultReturnPatch
    {
        private static bool Prefix()
        {
            if (!ResultVotes.Open)
            {
                return true;
            }
            if (FFSNetwork.IsClient)
            {
                Votes.Cast(ResultVotes.Return);
                return false;
            }
            Votes.Close();
            return true;
        }
    }

    [HarmonyPatch(typeof(UINewAdventureResultsManager), nameof(UINewAdventureResultsManager.ProxyRetryScenario))]
    internal static class ResultProxyRetryPatch
    {
        private static void Prefix(UINewAdventureResultsManager __instance)
        {
            ResultDecided.Close(__instance);
        }
    }

    [HarmonyPatch(typeof(UINewAdventureResultsManager), nameof(UINewAdventureResultsManager.ProxyExitScenario))]
    internal static class ResultProxyExitPatch
    {
        private static void Prefix(UINewAdventureResultsManager __instance)
        {
            ResultDecided.Close(__instance);
        }
    }

    internal static class ResultDecided
    {
        internal static void Close(UIResultsManager manager)
        {
            if (!ResultVotes.Open)
            {
                return;
            }
            Votes.Close();
            ResultVotes.RetryOption(manager).SetActive(false);
            ResultVotes.ReturnOption(manager).SetActive(false);
        }
    }

    /// <summary>
    /// Choosing where to go. Clients can already select a location, and that stays local, so selecting one
    /// is the vote and deselecting takes it back. The host's selection shows too, so everyone can see what's
    /// being considered. It shows beside that quest in the quest list, which every player has. A player can
    /// instead recommend the city event, by clicking its button on a client, where it's otherwise locked.
    /// </summary>
    internal static class QuestVotes
    {
        private const string KeyPrefix = "quest|";
        internal const string CityEvent = "city-event";
        private const float RefreshSeconds = 1f;

        private static float _nextRefresh;

        internal static void Tick()
        {
            if (Time.realtimeSinceStartup < _nextRefresh)
            {
                return;
            }
            _nextRefresh = Time.realtimeSinceStartup + RefreshSeconds;
            bool open = Votes.IsOpen(KeyPrefix);
            var state = AdventureState.MapState;
            bool selecting = FFSNetwork.IsOnline && Singleton<MapChoreographer>.Instance != null && state != null
                && (state.CurrentMapPhaseType == EMapPhaseType.InHQ || state.CurrentMapPhaseType == EMapPhaseType.AtLinkedScenario);
            if (!selecting)
            {
                if (open)
                {
                    Votes.Close();
                }
                return;
            }
            if (Votes.AnyOpen && !open)
            {
                return;
            }
            var rows = UnityEngine.Object.FindObjectsOfType<UIQuestLogSlot>()
                .Where(slot => slot.Quest != null)
                .GroupBy(slot => slot.Quest.ID)
                .ToDictionary(group => group.Key, group => group.First().transform);
            var city = UnityEngine.Object.FindObjectOfType<UICityEncounterButton>();
            if (city != null && state.CanDrawCityEvent)
            {
                rows[CityEvent] = city.transform;
            }
            if (open)
            {
                Votes.UpdateRows(rows);
            }
            else
            {
                Votes.Open(KeyPrefix + state.CurrentMapPhaseType, rows, VotePlacement.NarrowRow);
            }
        }
    }

    /// <summary>
    /// The game also selects locations by itself, such as when the map loads, so only a real click counts:
    /// on the location, or on its row in the quest list.
    /// </summary>
    [HarmonyPatch(typeof(MapLocation), nameof(MapLocation.OnPointerClick))]
    internal static class QuestMapClickPatch
    {
        private static void Postfix(MapLocation __instance)
        {
            if (__instance.IsSelected && __instance.LocationQuest != null && Votes.IsOpen("quest|"))
            {
                Votes.Set(__instance.LocationQuest.ID);
            }
        }
    }

    [HarmonyPatch(typeof(MapChoreographer), nameof(MapChoreographer.OnMapLocationSelect))]
    internal static class QuestDeselectPatch
    {
        private static void Postfix(MapLocation mapLocation, bool active)
        {
            if (!active && mapLocation?.LocationQuest != null)
            {
                Votes.Unset(mapLocation.LocationQuest.ID);
            }
        }
    }

    /// <summary>The host's click really starts the city event's ready-up, so it's shown from there.</summary>
    [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.OnSelectedCityEvent))]
    internal static class CityHostSelectPatch
    {
        private static void Postfix()
        {
            if (FFSNetwork.IsHost && Votes.IsOpen("quest|"))
            {
                Votes.Set(QuestVotes.CityEvent);
            }
        }
    }

    [HarmonyPatch(typeof(UIMapMultiplayerController), "CancelPreviewCityEvent")]
    internal static class CityHostCancelPatch
    {
        private static void Postfix()
        {
            if (FFSNetwork.IsHost)
            {
                Votes.Unset(QuestVotes.CityEvent);
            }
        }
    }

    [HarmonyPatch(typeof(UIQuestLogSlot), "Select")]
    internal static class QuestRowClickPatch
    {
        private static void Postfix(UIQuestLogSlot __instance)
        {
            if (__instance.Quest != null && Votes.IsOpen("quest|"))
            {
                Votes.Set(__instance.Quest.ID);
            }
        }
    }

    /// <summary>
    /// Lets any player press Continue when the enemy cards are revealed. Only the host's press counts in
    /// the game's protocol, so a client asks and the host's own game presses. To the game and to players
    /// without the mod, that looks exactly like the host clicking. Nothing is decided at this step.
    /// </summary>
    internal static class SharedContinue
    {
        internal const string Prefix = "GSC1";
        private const float MinimumRevealSeconds = 1.5f;
        private const float RequestSeconds = 6f;

        private static GameObject _canvas;
        private static Button _button;
        private static int _requestRound = -1;
        private static float _requestTime;
        private static float _revealSince = -1f;
        private static int _sentRound = -1;
        private const float TipHideSeconds = 0.5f;
        private static float _nextTipHide;

        private static int Round => ScenarioManager.CurrentScenarioState?.RoundNumber ?? -1;

        private static bool InReveal()
        {
            var choreographer = Choreographer.s_Choreographer;
            return choreographer != null
                && PhaseManager.CurrentPhase != null
                && PhaseManager.CurrentPhase.Type == CPhase.PhaseType.MonsterClassesSelectAbilityCards
                && ActionProcessor.CurrentPhase == ActionPhaseType.EnemyCardReveal
                && choreographer.LastMessage != null
                && choreographer.LastMessage.m_Type == CMessageData.MessageType.MonsterClassesToSelectAbilityCards;
        }

        internal static void Tick()
        {
            bool reveal = Cfg.SharedContinue.Value && FFSNetwork.IsOnline && InReveal();
            if (!reveal)
            {
                _revealSince = -1f;
                Show(false);
                return;
            }
            if (_revealSince < 0f)
            {
                _revealSince = Time.realtimeSinceStartup;
            }
            if (FFSNetwork.IsClient)
            {
                Show(SessionStatus.HostSupportsVoting() && _sentRound != Round);
            }
            else if (FFSNetwork.IsHost)
            {
                TryPress();
            }
        }

        internal static void Receive(NetworkPlayer player, string[] parts)
        {
            if (FFSNetwork.IsHost && Cfg.SharedContinue.Value && parts.Length == 2 && player.IsClient && player.IsParticipant
                && int.TryParse(parts[1], out int round))
            {
                _requestRound = round;
                _requestTime = Time.realtimeSinceStartup;
                Plugin.Log.LogInfo($"{player.Username} pressed Continue for round {round}.");
            }
        }

        private static void TryPress()
        {
            float now = Time.realtimeSinceStartup;
            if (_requestRound != Round || now - _requestTime > RequestSeconds || now - _revealSince < MinimumRevealSeconds)
            {
                return;
            }
            var choreographer = Choreographer.s_Choreographer;
            var ready = choreographer.readyButton;
            if (FFSNetwork.HasDesynchronized || ScenarioRuleClient.IsProcessingOrMessagesQueued
                || choreographer.m_MessageQueue.Count > 0 || Singleton<UIConfirmationBoxManager>.Instance.IsRequested
                || ready.ActionsQueue.Count == 0 || !ready.ButtonComponent.enabled)
            {
                return;
            }
            _requestRound = -1;
            ready.OnClickInternal();
            Plugin.Log.LogInfo("Continued for another player.");
        }

        private static void Show(bool show)
        {
            if (!show)
            {
                if (_canvas != null)
                {
                    _canvas.SetActive(false);
                }
                return;
            }
            if (_canvas == null)
            {
                Build();
            }
            // The game can show its wait for the host tip after this appears, so keep clearing it.
            if (Time.realtimeSinceStartup >= _nextTipHide)
            {
                _nextTipHide = Time.realtimeSinceStartup + TipHideSeconds;
                InitiativeTrack.Instance?.helpBox?.Hide();
            }
            if (!_canvas.activeSelf)
            {
                _canvas.SetActive(true);
                _button.interactable = true;
            }
        }

        private static void Build()
        {
            _canvas = new GameObject("GloomSpeedContinue", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var body = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)body.transform;
            rect.SetParent(_canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 75f);
            rect.sizeDelta = new Vector2(324f, 60f);
            body.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            _button = body.GetComponent<Button>();
            _button.transition = Selectable.Transition.None;
            _button.onClick.AddListener(Press);
            AddLook(rect);
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
        }

        /// <summary>Copies how the host's Continue button looks. The copy only draws, and clicks hit the parent.</summary>
        private static void AddLook(RectTransform parent)
        {
            var holder = new GameObject("GloomSpeedHolder");
            holder.SetActive(false);
            try
            {
                var look = UnityEngine.Object.Instantiate(Choreographer.s_Choreographer.readyButton.gameObject, holder.transform);
                MultiplayerMenuStatusPatch.StripToVisuals(look, new List<Transform>());
                var rect = (RectTransform)look.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                // The original is faded out on clients, by a group on the button itself.
                var group = look.GetComponent<CanvasGroup>();
                if (group != null)
                {
                    group.alpha = 1f;
                }
                foreach (var text in look.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    text.text = GLOOM.LocalizationManager.GetTranslation("GUI_CONTINUE");
                    text.color = Color.white;
                }
                look.SetActive(true);
            }
            finally
            {
                UnityEngine.Object.Destroy(holder);
            }
        }

        private static void Press()
        {
            _sentRound = Round;
            _button.interactable = false;
            SessionStatus.SendToHost($"{Prefix};{Round}");
            Plugin.Log.LogInfo($"Asked the host to continue round {Round}.");
        }
    }

    /// <summary>
    /// Stops a controller press on the host from pressing Continue a second time. The target is small, but
    /// the game only calls it through a delegate, so it can't be inlined past this patch.
    /// </summary>
    [HarmonyPatch(typeof(Script.GUI.SMNavigation.States.ScenarioStates.EnemyActionScenarioState), "ReadyButtonClick")]
    internal static class ContinueOncePatch
    {
        private static bool Prefix()
        {
            return !FFSNetwork.IsOnline || ActionProcessor.CurrentPhase == ActionPhaseType.EnemyCardReveal;
        }
    }
}
