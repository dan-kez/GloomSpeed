using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using FFSNet;
using HarmonyLib;
using Steamworks;
using UnityEngine;

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
    /// Shares mod version and speed through Steam Rich Presence, outside the game's network traffic,
    /// so players without the mod are unaffected. No value doesn't prove there's no mod: Steam only
    /// exposes this for friends, and it can arrive late.
    /// </summary>
    internal static class PresenceStatus
    {
        private const string Key = "gloomspeed";
        private const float PollSeconds = 5f;

        private static readonly Dictionary<int, string> Known = new Dictionary<int, string>();
        private static string _published;
        private static string _overlay = string.Empty;
        private static float _nextPoll;
        private static GUIStyle _style;
        private static bool _dead;

        internal static void Tick()
        {
            if (_dead || !Cfg.PresenceStatus.Value || Time.realtimeSinceStartup < _nextPoll)
            {
                return;
            }
            _nextPoll = Time.realtimeSinceStartup + PollSeconds;
            try
            {
                if (SteamClient.IsValid)
                {
                    Publish();
                }
                Refresh();
            }
            catch (Exception e)
            {
                _dead = true;
                _overlay = string.Empty;
                Plugin.Log.LogError("Presence status stopped: " + e);
            }
        }

        internal static void Draw()
        {
            if (_overlay.Length == 0 || !Cfg.PresenceOverlay.Value)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = false };
            }
            var content = new GUIContent(_overlay);
            var size = _style.CalcSize(content);
            GUI.Box(new Rect(8, 8, size.x, size.y), content, _style);
        }

        private static string Current()
        {
            return string.Join(";", Plugin.Version, Cfg.Enabled.Value ? "on" : "off",
                Cfg.Multiplier.Value.ToString("0.##", CultureInfo.InvariantCulture),
                Cfg.EnemyTurnMultiplier.Value.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private static void Publish()
        {
            string value = Current();
            if (value != _published)
            {
                bool ok = SteamFriends.SetRichPresence(Key, value);
                _published = value;
                Plugin.Log.LogInfo($"Presence published: {value} (accepted: {ok})");
            }
        }

        private static void Refresh()
        {
            if (!FFSNetwork.IsOnline || PlayerRegistry.AllPlayers.Count < 2)
            {
                _overlay = string.Empty;
                Known.Clear();
                return;
            }
            var lines = new List<string> { "GloomSpeed" };
            foreach (var player in PlayerRegistry.AllPlayers.OrderBy(p => p.PlayerID))
            {
                string state = Describe(player);
                lines.Add(player.Username + ": " + state);
                if (!Known.TryGetValue(player.PlayerID, out string previous) || previous != state)
                {
                    Known[player.PlayerID] = state;
                    Plugin.Log.LogInfo($"Presence: {player.Username}: {state}");
                }
            }
            _overlay = string.Join("\n", lines);
        }

        private static string Describe(NetworkPlayer player)
        {
            if (player == PlayerRegistry.MyPlayer)
            {
                return Format(Current());
            }
            if (!ulong.TryParse(player.PlatformPlayerId, out ulong id))
            {
                return "unknown (not on Steam)";
            }
            var friend = new Friend(new SteamId { Value = id });
            if (!friend.IsFriend)
            {
                return "unknown (not a Steam friend)";
            }
            string value = friend.GetRichPresence(Key);
            return value == null ? "nothing reported" : Format(value);
        }

        private static string Format(string value)
        {
            var parts = value.Split(';');
            if (parts.Length != 4)
            {
                return "unreadable (" + value + ")";
            }
            string mismatch = parts[0] == Plugin.Version ? string.Empty : " (differs from yours)";
            return parts[1] == "on"
                ? $"v{parts[0]}{mismatch}, x{parts[2]}, enemies x{parts[3]}"
                : $"v{parts[0]}{mismatch}, speed changes off";
        }
    }
}
