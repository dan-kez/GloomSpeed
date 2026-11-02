using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using FFSNet;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomSpeed
{
    /// <summary>
    /// Host-side record of when each player reaches the end of an ability, turn or round. The host
    /// waits for all of them (Choreographer.WaitForEndOf*Complete), so the spread shows who holds the
    /// group up. Observes only: it never changes an acknowledgement and sends nothing.
    /// </summary>
    internal static class AckLog
    {
        private const int SummaryEvery = 25;

        private sealed class Window
        {
            internal readonly string Kind;
            internal readonly int Seq;
            internal readonly Dictionary<int, long> Arrivals = new Dictionary<int, long>();
            internal readonly Dictionary<int, string> Names = new Dictionary<int, string>();

            internal Window(string kind, int seq)
            {
                Kind = kind;
                Seq = seq;
            }
        }

        private sealed class PlayerStats
        {
            internal string Name;
            internal readonly List<double> Offsets = new List<double>();
            internal int TimesLast;
            internal double HeldSeconds;
        }

        private static readonly Dictionary<string, Window> Open = new Dictionary<string, Window>();
        private static readonly Dictionary<int, PlayerStats> Stats = new Dictionary<int, PlayerStats>();
        private static readonly Dictionary<string, double> HostWait = new Dictionary<string, double>();
        private static StreamWriter _csv;
        private static string _summaryPath;
        private static int _seq;
        private static int _written;
        private static bool _dead;

        internal static void Arrive(string kind, int playerId, string name)
        {
            if (_dead || !Cfg.AckTimingLog.Value || !FFSNetwork.IsOnline || !FFSNetwork.IsHost)
            {
                return;
            }
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (Open.TryGetValue(kind, out var window) && window.Arrivals.ContainsKey(playerId))
                {
                    // A second arrival means the last sync never finished, e.g. a player dropped.
                    Close(window, complete: false);
                    window = null;
                }
                if (window == null)
                {
                    window = new Window(kind, ++_seq);
                    Open[kind] = window;
                }
                window.Arrivals[playerId] = now;
                window.Names[playerId] = name;

                bool hostIn = window.Arrivals.ContainsKey(PlayerRegistry.HostPlayerID);
                if (hostIn && PlayerRegistry.Participants.All(p => window.Arrivals.ContainsKey(p.PlayerID)))
                {
                    Close(window, complete: true);
                }
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        private static void Close(Window window, bool complete)
        {
            Open.Remove(window.Kind);
            OpenFiles();

            var ordered = window.Arrivals.OrderBy(a => a.Value).ToList();
            long first = ordered[0].Value;
            long last = ordered[ordered.Count - 1].Value;
            double held = ordered.Count > 1 ? Seconds(last - ordered[ordered.Count - 2].Value) : 0;
            double hostWait = window.Arrivals.TryGetValue(PlayerRegistry.HostPlayerID, out long host)
                ? Seconds(last - host)
                : 0;
            string utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            foreach (var arrival in ordered)
            {
                double offset = Seconds(arrival.Value - first);
                bool isLast = arrival.Value == last && ordered.Count > 1;
                string name = window.Names[arrival.Key];
                _csv.WriteLine(string.Join(",", utc, window.Kind, F(window.Seq), F(arrival.Key), Quote(name), F(offset),
                    isLast ? "1" : "0", F(isLast ? held : 0), F(hostWait), complete ? "1" : "0"));

                if (complete)
                {
                    if (!Stats.TryGetValue(arrival.Key, out var stats))
                    {
                        stats = new PlayerStats();
                        Stats[arrival.Key] = stats;
                    }
                    stats.Name = name;
                    stats.Offsets.Add(offset);
                    if (isLast)
                    {
                        stats.TimesLast++;
                        stats.HeldSeconds += held;
                    }
                }
            }
            _csv.Flush();

            if (complete)
            {
                HostWait.TryGetValue(window.Kind, out double total);
                HostWait[window.Kind] = total + hostWait;
                // The game has crashed on exit before, so don't rely on OnApplicationQuit alone.
                if (++_written % SummaryEvery == 0)
                {
                    WriteSummary();
                }
            }
        }

        internal static void Shutdown()
        {
            if (_dead || _csv == null)
            {
                return;
            }
            try
            {
                foreach (var window in Open.Values.ToList())
                {
                    Close(window, complete: false);
                }
                WriteSummary();
                Plugin.Log.LogInfo($"Acknowledgement timing written to {_summaryPath}");
                _csv.Dispose();
                _csv = null;
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        private static void OpenFiles()
        {
            if (_csv != null)
            {
                return;
            }
            string dir = Path.Combine(Paths.BepInExRootPath, "GloomSpeed");
            Directory.CreateDirectory(dir);
            string stem = "acks-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            _summaryPath = Path.Combine(dir, stem + "-summary.txt");
            _csv = new StreamWriter(Path.Combine(dir, stem + ".csv"));
            _csv.WriteLine("utc,kind,sync,player_id,player,offset_s,last,held_s,host_wait_s,complete");
        }

        private static void WriteSummary()
        {
            var text = new StringBuilder();
            text.AppendLine($"Completed syncs: {_written}");
            text.AppendLine("Offset = seconds after the first player reached this point. Held = time the last player alone made everyone wait.");
            text.AppendLine();
            text.AppendLine("player                 syncs  median    p90  worst  last  held_total");
            foreach (var pair in Stats.OrderByDescending(s => s.Value.HeldSeconds))
            {
                var stats = pair.Value;
                var sorted = stats.Offsets.OrderBy(o => o).ToList();
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-22} {1,5} {2,6:F2}s {3,5:F2}s {4,5:F2}s {5,5} {6,10:F1}s",
                    stats.Name, sorted.Count, Percentile(sorted, 0.5), Percentile(sorted, 0.9), sorted[sorted.Count - 1],
                    stats.TimesLast, stats.HeldSeconds));
            }
            text.AppendLine();
            text.AppendLine("Host's own wait after reaching each point:");
            foreach (var pair in HostWait.OrderBy(h => h.Key))
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-8} {1,8:F1}s", pair.Key, pair.Value));
            }
            File.WriteAllText(_summaryPath, text.ToString());
        }

        private static double Percentile(List<double> sorted, double p)
        {
            return sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
        }

        private static double Seconds(long ticks)
        {
            return (double)ticks / Stopwatch.Frequency;
        }

        private static string F(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        private static void Stop(Exception e)
        {
            _dead = true;
            Plugin.Log.LogError("Acknowledgement timing stopped: " + e);
        }
    }

    internal static class AckHost
    {
        internal static void Reached(string kind)
        {
            if (FFSNetwork.IsOnline && FFSNetwork.IsHost)
            {
                AckLog.Arrive(kind, PlayerRegistry.HostPlayerID, PlayerRegistry.MyPlayer?.Username);
            }
        }

        internal static void Client(string kind, GameAction action)
        {
            if (FFSNetwork.IsOnline && FFSNetwork.IsHost)
            {
                AckLog.Arrive(kind, action.PlayerID, PlayerRegistry.GetPlayer(action.PlayerID)?.Username);
            }
        }
    }

    [HarmonyPatch(typeof(Choreographer), nameof(Choreographer.EndOfAbilityReachedLocally))]
    internal static class AckAbilityLocalPatch
    {
        private static void Postfix() => AckHost.Reached("ability");
    }

    [HarmonyPatch(typeof(Choreographer), nameof(Choreographer.EndOfTurnReachedLocally))]
    internal static class AckTurnLocalPatch
    {
        private static void Postfix() => AckHost.Reached("turn");
    }

    [HarmonyPatch(typeof(Choreographer), "EndOfRoundReachedLocally")]
    internal static class AckRoundLocalPatch
    {
        private static void Postfix() => AckHost.Reached("round");
    }

    [HarmonyPatch(typeof(Choreographer), nameof(Choreographer.ServerACKClientReachingEndOfAbility))]
    internal static class AckAbilityClientPatch
    {
        private static void Prefix(GameAction action) => AckHost.Client("ability", action);
    }

    [HarmonyPatch(typeof(Choreographer), nameof(Choreographer.ServerACKClientReachingEndOfTurn))]
    internal static class AckTurnClientPatch
    {
        private static void Prefix(GameAction action) => AckHost.Client("turn", action);
    }

    [HarmonyPatch(typeof(Choreographer), nameof(Choreographer.ServerACKClientReachingEndOfRound))]
    internal static class AckRoundClientPatch
    {
        private static void Prefix(GameAction action) => AckHost.Client("round", action);
    }

    /// <summary>
    /// Finds Steam's library at runtime, because Windows ships Facepunch.Steamworks.Win64 and macOS ships
    /// Facepunch.Steamworks.Posix. Both have the same API, so one plugin DLL can work with either.
    /// </summary>
    internal static class SteamApi
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        private static readonly Assembly Library = AppDomain.CurrentDomain.GetAssemblies()
            .First(assembly => assembly.GetName().Name.StartsWith("Facepunch.Steamworks", StringComparison.Ordinal));

        private static readonly PropertyInfo ClientValid =
            Library.GetType("Steamworks.SteamClient", true).GetProperty("IsValid", PublicStatic);

        private static readonly Type SteamIdType = Library.GetType("Steamworks.SteamId", true);
        private static readonly FieldInfo SteamIdValue = SteamIdType.GetField("Value");

        private static readonly Type NetworkingType = Library.GetType("Steamworks.SteamNetworking", true);

        private static readonly MethodInfo SendP2P = NetworkingType.GetMethods(PublicStatic).First(method =>
            method.Name == "SendP2PPacket" && method.GetParameters()[1].ParameterType == typeof(byte[]));

        private static readonly object Reliable = Enum.Parse(SendP2P.GetParameters()[4].ParameterType, "Reliable");

        private static readonly MethodInfo ReadP2P = NetworkingType.GetMethods(PublicStatic).First(method =>
            method.Name == "ReadP2PPacket" && method.GetParameters().Length == 1);

        private static readonly MethodInfo CloseP2P = NetworkingType.GetMethod("CloseP2PSessionWithUser", PublicStatic);

        internal static bool IsValid => (bool)ClientValid.GetValue(null);

        private static object ToSteamId(ulong steamId)
        {
            object id = Activator.CreateInstance(SteamIdType);
            SteamIdValue.SetValue(id, steamId);
            return id;
        }

        internal static void SendPacket(ulong steamId, byte[] data, int channel)
        {
            SendP2P.Invoke(null, new[] { ToSteamId(steamId), data, -1, channel, Reliable });
        }

        internal static bool TryReadPacket(int channel, out ulong sender, out byte[] data)
        {
            sender = 0;
            data = null;
            object packet = ReadP2P.Invoke(null, new object[] { channel });
            if (packet == null)
            {
                return false;
            }
            var type = packet.GetType();
            sender = (ulong)SteamIdValue.GetValue(type.GetField("SteamId").GetValue(packet));
            data = (byte[])type.GetField("Data").GetValue(packet);
            return true;
        }

        internal static void CloseSession(ulong steamId)
        {
            CloseP2P.Invoke(null, new[] { ToSteamId(steamId) });
        }
    }

    /// <summary>
    /// Compares each player's mod version and Game Speed, sent directly between players in a session by
    /// SessionStatus, outside the game's network traffic. No value doesn't prove there's no mod: it can
    /// arrive late. The multiplayer menu always lists everyone;
    /// the on-screen box only appears when something doesn't line up.
    /// </summary>
    internal static class PresenceStatus
    {
        // Only reads what has already arrived, so polling this often costs little.
        private const float PollSeconds = 1f;
        private const string CantDetermine = "Can't determine";

        private static readonly Dictionary<int, string> Known = new Dictionary<int, string>();
        private static string _lastStatus;
        private static string _warning = string.Empty;
        private static bool _mismatch;
        private static float _nextPoll;
        private const string WarningId = "GloomSpeedWarning";
        private static string _shownWarning = string.Empty;
        private static bool _dead;

        /// <summary>One entry per player in the session, for the multiplayer menu.</summary>
        internal static List<(NetworkPlayer Player, string Status, bool Problem)> MenuRows { get; }
            = new List<(NetworkPlayer, string, bool)>();

        internal static void Tick()
        {
            if (_dead || !Cfg.PresenceStatus.Value || Time.realtimeSinceStartup < _nextPoll)
            {
                return;
            }
            _nextPoll = Time.realtimeSinceStartup + PollSeconds;
            try
            {
                if (SteamApi.IsValid)
                {
                    LogStatus();
                    SessionStatus.Exchange(Current());
                }
                Refresh();
            }
            catch (Exception e)
            {
                _dead = true;
                _warning = string.Empty;
                MenuRows.Clear();
                MultiplayerMenuStatusPatch.Refresh();
                ShowWarning(string.Empty);
                Plugin.Log.LogError("Presence status stopped: " + e);
            }
        }

        /// <summary>Lets the multiplayer menu show the current state as it opens.</summary>
        internal static void PollSoon()
        {
            _nextPoll = 0f;
            Tick();
        }

        /// <summary>Shows the warning in the game's own notification panel, only when its text changes.</summary>
        private static void ShowWarning(string text)
        {
            if (text == _shownWarning)
            {
                return;
            }
            var manager = Singleton<UINotificationManager>.Instance;
            if (manager == null)
            {
                return;
            }
            manager.HideNotification(WarningId, instant: true);
            if (text.Length > 0 && Cfg.PresenceOverlay.Value)
            {
                manager.ShowPermanentNotification(WarningId, "GUI_MULTIPLAYER", text);
            }
            _shownWarning = text;
        }

        private static string Current()
        {
            bool normal = Presets.Current == SpeedPreset.Normal;
            return string.Join(";", Plugin.Version, Cfg.Enabled.Value ? Presets.CurrentLabel : "Disabled",
                (normal ? 1f : Presets.Multiplier).ToString("0.##", CultureInfo.InvariantCulture),
                (normal ? 1f : Presets.EnemyMultiplier).ToString("0.##", CultureInfo.InvariantCulture));
        }

        private static void LogStatus()
        {
            string value = Current();
            if (value != _lastStatus)
            {
                _lastStatus = value;
                Plugin.Log.LogInfo("My status: " + value);
            }
        }

        private static void Refresh()
        {
            if (!FFSNetwork.IsOnline)
            {
                _warning = string.Empty;
                ShowWarning(string.Empty);
                MenuRows.Clear();
                Known.Clear();
                MultiplayerMenuStatusPatch.Refresh();
                return;
            }

            string[] mine = Current().Split(';');
            var problems = new List<string>();
            _mismatch = false;
            MenuRows.Clear();
            foreach (var player in PlayerRegistry.AllPlayers.OrderBy(p => p.PlayerID))
            {
                bool isMe = player == PlayerRegistry.MyPlayer;
                int before = problems.Count;
                string state = isMe ? Format(mine) : Describe(player, mine, problems);
                string line = player.Username + ": " + state;
                MenuRows.Add((player, state, problems.Count > before));
                if (!Known.TryGetValue(player.PlayerID, out string previous) || previous != state)
                {
                    Known[player.PlayerID] = state;
                    Plugin.Log.LogInfo("Presence: " + line);
                }
            }

            _warning = problems.Count == 0
                ? string.Empty
                : (_mismatch ? "GloomSpeed settings don't match:\n" : "Can't confirm everyone's GloomSpeed settings:\n") + string.Join("\n", problems);
            MultiplayerMenuStatusPatch.Refresh();
            ShowWarning(_warning);
        }

        private static string Describe(NetworkPlayer player, string[] mine, List<string> problems)
        {
            if (!ulong.TryParse(player.PlatformPlayerId, out ulong id))
            {
                problems.Add(player.Username + ": can't check, not on Steam");
                return CantDetermine;
            }
            string value = SessionStatus.Get(id);
            if (value == null)
            {
                problems.Add(player.Username + ": mod not detected, or its status hasn't arrived yet");
                return CantDetermine;
            }
            string[] theirs = value.Split(';');
            if (theirs.Length != 4)
            {
                problems.Add(player.Username + ": unreadable status");
                return "unreadable status";
            }
            if (theirs[0] != mine[0])
            {
                _mismatch = true;
                problems.Add($"{player.Username}: mod v{theirs[0]}, you have v{mine[0]}");
            }
            if (theirs[1] != mine[1])
            {
                _mismatch = true;
                problems.Add($"{player.Username}: {theirs[1]}, you're on {mine[1]}");
            }
            return Format(theirs);
        }

        private static string Format(string[] parts)
        {
            return $"v{parts[0]}, {parts[1]}";
        }
    }

    /// <summary>
    /// Adds a GloomSpeed section below Roles in the in-game multiplayer menu, by copying the Roles header
    /// and character rows. The copies are made under an inactive parent and stripped there, so none of the
    /// game's logic in them ever runs. Only their drawing and layout parts reach the real menu.
    /// </summary>
    [HarmonyPatch(typeof(UIMultiplayerEscSubmenu), "OnShown")]
    internal static class MultiplayerMenuStatusPatch
    {
        private const string RolesHeadingKey = "GUI_MULTIPLAYER_PARTY";
        private const string ProblemColor = "#E8846B";

        private sealed class Row
        {
            internal GameObject Root;
            internal TextMeshProUGUI Status;
            internal MultiplayerUserState Owner;
        }

        private static readonly AccessTools.FieldRef<UIMultiplayerMerchantRoster, List<UIMultiplayerMerchantSlot>> RosterSlots =
            AccessTools.FieldRefAccess<UIMultiplayerMerchantRoster, List<UIMultiplayerMerchantSlot>>("slots");

        private static readonly AccessTools.FieldRef<UIMultiplayerEscSubmenu, UIMultiplayerMerchantRoster> Roster =
            AccessTools.FieldRefAccess<UIMultiplayerEscSubmenu, UIMultiplayerMerchantRoster>("heroSlotsController");

        private static readonly AccessTools.FieldRef<TextLocalizedListener, string> HeadingKey =
            AccessTools.FieldRefAccess<TextLocalizedListener, string>("key");

        private static readonly AccessTools.FieldRef<UIMultiplayerMerchantSlot, TextMeshProUGUI> SlotTitle =
            AccessTools.FieldRefAccess<UIMultiplayerMerchantSlot, TextMeshProUGUI>("title");

        private static readonly AccessTools.FieldRef<UIMultiplayerMerchantSlot, MultiplayerUserState> SlotOwner =
            AccessTools.FieldRefAccess<UIMultiplayerMerchantSlot, MultiplayerUserState>("owner");

        private static readonly List<Row> Rows = new List<Row>();
        private static bool _dumpPending;
        private static RectTransform _list;
        private static GameObject _heading;

        private static bool Prepare()
        {
            return Guard.Fields(typeof(MultiplayerMenuStatusPatch),
                (typeof(UIMultiplayerEscSubmenu), "heroSlotsController"),
                (typeof(UIMultiplayerMerchantRoster), "slots"),
                (typeof(TextLocalizedListener), "key"),
                (typeof(UIMultiplayerMerchantSlot), "title"),
                (typeof(UIMultiplayerMerchantSlot), "owner"));
        }

        private static void Postfix(UIMultiplayerEscSubmenu __instance)
        {
            try
            {
                // Hosts and clients each see their own container in this menu, with its own Roles rows, and
                // the game's roster only lists the host's. So the section follows the rows on screen.
                var visible = __instance.GetComponentsInChildren<UIMultiplayerMerchantSlot>(false);
                var slots = visible.Length > 0 ? visible.ToList() : RosterSlots(Roster(__instance));
                var list = slots[0].transform.parent;
                if (_heading == null || _list != list)
                {
                    Remove();
                    Build(slots.Where(slot => slot.transform.parent == list).ToList(), list);
                }
                if (Cfg.UiDebugLog.Value)
                {
                    LogShown(__instance);
                }
                PresenceStatus.PollSoon();
                Refresh();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not add the multiplayer status: " + e);
            }
        }

        private static void Remove()
        {
            if (_heading != null)
            {
                UnityEngine.Object.Destroy(_heading);
            }
            foreach (var row in Rows)
            {
                if (row.Root != null)
                {
                    UnityEngine.Object.Destroy(row.Root);
                }
            }
            Rows.Clear();
            _heading = null;
        }

        private static void LogShown(UIMultiplayerEscSubmenu menu)
        {
            var path = new System.Text.StringBuilder();
            for (Transform node = _list; node != null; node = node.parent)
            {
                path.Append(" < ").Append(node.name).Append(node.gameObject.activeSelf ? " on" : " off");
            }
            Plugin.Log.LogInfo($"Multiplayer menu shown: {Resources.FindObjectsOfTypeAll<UIMultiplayerEscSubmenu>().Length} menus, "
                + $"this one {menu.GetInstanceID()}, list visible {_list != null && _list.gameObject.activeInHierarchy}, path{path}");
        }

        private static void Build(List<UIMultiplayerMerchantSlot> slots, Transform list)
        {
            // The roster component sits on the whole Multiplayer window, so copy its parts, not it: the
            // header and rows are children of the list that also holds End Session and the other buttons.
            var heading = list.GetComponentsInChildren<TextLocalizedListener>(true)
                .FirstOrDefault(text => HeadingKey(text) == RolesHeadingKey);
            if (heading == null)
            {
                Plugin.Log.LogError("Could not add the multiplayer status: the Roles heading was not found.");
                return;
            }
            var headingBlock = heading.transform;
            while (headingBlock.parent != list)
            {
                headingBlock = headingBlock.parent;
            }

            var holder = new GameObject("GloomSpeedHolder");
            holder.SetActive(false);
            try
            {
                var headingCopy = UnityEngine.Object.Instantiate(headingBlock.gameObject, holder.transform);
                headingCopy.name = "GloomSpeedHeading";
                headingCopy.GetComponentInChildren<TextMeshProUGUI>(true).text = "GloomSpeed";
                StripToVisuals(headingCopy, new List<Transform>());

                var rows = new List<Row>();
                foreach (var template in slots)
                {
                    var rowCopy = UnityEngine.Object.Instantiate(template.gameObject, holder.transform);
                    rowCopy.name = "GloomSpeedRow";
                    var slot = rowCopy.GetComponent<UIMultiplayerMerchantSlot>();
                    var owner = SlotOwner(slot);
                    rows.Add(new Row { Root = rowCopy, Status = SlotTitle(slot), Owner = owner });
                    StripToVisuals(rowCopy, new List<Transform> { owner.transform });
                }

                headingCopy.transform.SetParent(list, false);
                headingCopy.transform.SetAsLastSibling();
                foreach (var row in rows)
                {
                    row.Root.transform.SetParent(list, false);
                    row.Root.transform.SetAsLastSibling();
                }

                // The list has a fixed height, so without this the new rows couldn't scroll into view.
                KeepTopEdge((RectTransform)list);
                var fitter = list.GetComponent<ContentSizeFitter>() ?? list.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                Rows.Clear();
                Rows.AddRange(rows);
                _heading = headingCopy;
                _list = (RectTransform)list;
                Plugin.Log.LogInfo($"Multiplayer status added: {Rows.Count} rows under {list.name}.");
                _dumpPending = Cfg.UiDebugLog.Value;
            }
            finally
            {
                UnityEngine.Object.Destroy(holder);
            }
        }

        /// <summary>The client's list is centred, so once it fits its rows it would slide down from the top.</summary>
        private static void KeepTopEdge(RectTransform list)
        {
            var corners = new Vector3[4];
            list.GetWorldCorners(corners);
            float top = corners[1].y;
            list.pivot = new Vector2(list.pivot.x, 1f);
            list.GetWorldCorners(corners);
            list.position += Vector3.up * (top - corners[1].y);
        }

        /// <summary>
        /// Leaves only what draws and lays out the copy, removed while it's inactive so nothing ever runs.
        /// A row holds buttons, tooltips and services that act on the real session. The player line keeps
        /// its own parts, because MultiplayerUserState needs them to show a player.
        /// </summary>
        internal static void StripToVisuals(GameObject root, List<Transform> keepInside)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true).Reverse())
            {
                bool visual = component is Transform || component is CanvasRenderer || component is Graphic
                    || component is LayoutGroup || component is LayoutElement || component is ContentSizeFitter
                    || component is AspectRatioFitter || component is Mask || component is RectMask2D
                    || component is BaseMeshEffect || component is CanvasGroup;
                if (visual || keepInside.Any(component.transform.IsChildOf))
                {
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(component);
            }

            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                selectable.enabled = false;
            }
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
            // Alpha stays as copied: the row's selected frame is a group held at 0 until it's selected.
            foreach (var group in root.GetComponentsInChildren<CanvasGroup>(true))
            {
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }

        private static void Dump(Transform node, int depth, System.Text.StringBuilder output)
        {
            var rect = node as RectTransform;
            var group = node.GetComponent<CanvasGroup>();
            var layout = node.GetComponent<LayoutElement>();
            var text = node.GetComponent<TextMeshProUGUI>();
            output.Append(new string(' ', depth * 2)).Append(node.name)
                .Append(node.gameObject.activeSelf ? " on" : " off")
                .Append(node.gameObject.activeInHierarchy ? "/visible" : "/hidden");
            if (rect != null)
            {
                output.Append(" size=").Append(rect.rect.size).Append(" pos=").Append(rect.anchoredPosition);
            }
            if (group != null)
            {
                output.Append(" alpha=").Append(group.alpha);
            }
            if (layout != null)
            {
                output.Append(" ignoreLayout=").Append(layout.ignoreLayout);
            }
            if (text != null)
            {
                output.Append(" text=\"").Append(text.text).Append('"');
            }
            output.Append('\n');
            if (depth < 5)
            {
                foreach (Transform child in node)
                {
                    Dump(child, depth + 1, output);
                }
            }
        }

        internal static void Refresh()
        {
            if (_heading == null)
            {
                return;
            }
            var players = PresenceStatus.MenuRows;
            _heading.SetActive(players.Count > 0);
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                if (i >= players.Count)
                {
                    row.Root.SetActive(false);
                    continue;
                }
                var (player, status, problem) = players[i];
                row.Root.SetActive(true);
                row.Owner.Show(player, isOnline: true);
                row.Status.text = problem ? $"<color={ProblemColor}>{status}</color>" : status;
            }
            // Otherwise the new rows can overlap Roles until something else refreshes the menu.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);

            // Only once the menu is on screen: the layout hasn't been worked out before then.
            if (_dumpPending && players.Count > 0 && _list.gameObject.activeInHierarchy)
            {
                _dumpPending = false;
                var dump = new System.Text.StringBuilder("Menu layout:\n");
                for (var parent = _list.parent; parent != null; parent = parent.parent)
                {
                    var parentRect = parent as RectTransform;
                    var parentGroup = parent.GetComponent<CanvasGroup>();
                    dump.Append("parent ").Append(parent.name)
                        .Append(parentRect != null ? " size=" + parentRect.rect.size + " pos=" + parentRect.anchoredPosition : "")
                        .Append(parentGroup != null ? " alpha=" + parentGroup.alpha : "")
                        .Append(parent.GetComponent<RectMask2D>() != null || parent.GetComponent<Mask>() != null ? " mask" : "")
                        .Append(parent.GetComponent<ScrollRect>() != null ? " scroll" : "")
                        .Append('\n');
                }
                Dump(_list, 0, dump);
                Plugin.Log.LogInfo(dump.ToString());
            }
        }
    }
}
