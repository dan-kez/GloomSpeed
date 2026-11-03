using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FFSNet;
using UnityEngine;

namespace GloomSpeed
{
    /// <summary>
    /// Shares mod status directly between players' Steam clients, outside the game's network traffic. It
    /// doesn't depend on friendship or Steam status settings, unlike Rich Presence. A player without the
    /// mod never reads this channel. Only players in the current session are sent to or listened to, and
    /// only the host's message can set speed. Speed is local pacing, so this doesn't touch game rules.
    /// </summary>
    internal static class SessionStatus
    {
        private const string Prefix = "GSP1";
        private const int Channel = 7219;
        private const float SendSeconds = 3f;
        private static readonly Version MinimumVotingVersion = new Version(0, 3, 3);
        private const float FreshSeconds = 15f;
        private const int MaxPacketsPerPoll = 32;
        private const int MaxPacketBytes = 240;
        private const float PollSeconds = 0.1f;
        private const int NoPush = -1;

        private static readonly Dictionary<ulong, (string Value, float Time)> Received = new Dictionary<ulong, (string, float)>();
        private static readonly HashSet<ulong> Peers = new HashSet<ulong>();
        private static float _nextSend;
        private static float _nextPoll;

        /// <summary>Reads waiting messages more often than status is sent, because votes should feel immediate.</summary>
        internal static void Poll()
        {
            if (!FFSNetwork.IsOnline || !SteamApi.IsValid || Time.realtimeSinceStartup < _nextPoll)
            {
                return;
            }
            _nextPoll = Time.realtimeSinceStartup + PollSeconds;
            Read();
        }

        private static Dictionary<ulong, NetworkPlayer> Members()
        {
            var members = new Dictionary<ulong, NetworkPlayer>();
            foreach (var player in PlayerRegistry.AllPlayers)
            {
                if (player != PlayerRegistry.MyPlayer && ulong.TryParse(player.PlatformPlayerId, out ulong id))
                {
                    members[id] = player;
                }
            }
            return members;
        }

        internal static void SendToModdedPeers(string message)
        {
            var data = Encoding.UTF8.GetBytes(message);
            foreach (var id in Members().Keys.Where(id => Get(id) != null))
            {
                SteamApi.SendPacket(id, data, Channel);
            }
        }

        internal static void SendToHost(string message)
        {
            var data = Encoding.UTF8.GetBytes(message);
            foreach (var member in Members().Where(member => !member.Value.IsClient))
            {
                SteamApi.SendPacket(member.Key, data, Channel);
            }
        }

        /// <summary>Voting needs the host to run a version that can show votes, or they'd go unseen.</summary>
        internal static bool HostSupportsVoting()
        {
            if (FFSNetwork.IsHost)
            {
                return true;
            }
            foreach (var member in Members().Where(member => !member.Value.IsClient))
            {
                string status = Get(member.Key);
                if (status != null && Version.TryParse(status.Split(';')[0], out var version))
                {
                    return version >= MinimumVotingVersion;
                }
            }
            return false;
        }

        /// <summary>The freshest directly received status for a player, or null.</summary>
        internal static string Get(ulong steamId)
        {
            return Received.TryGetValue(steamId, out var entry) && Time.realtimeSinceStartup - entry.Time < FreshSeconds
                ? entry.Value
                : null;
        }

        internal static void Exchange(string status)
        {
            if (!FFSNetwork.IsOnline)
            {
                Reset();
                return;
            }

            var members = Members();
            foreach (var gone in Peers.Except(members.Keys).ToList())
            {
                SteamApi.CloseSession(gone);
                Peers.Remove(gone);
                Received.Remove(gone);
            }

            if (Time.realtimeSinceStartup >= _nextSend)
            {
                _nextSend = Time.realtimeSinceStartup + SendSeconds;
                int push = FFSNetwork.IsHost && Cfg.Enabled.Value && Cfg.ShareSpeed.Value ? (int)Presets.Current : NoPush;
                var data = Encoding.UTF8.GetBytes($"{Prefix};{status};{push}");
                foreach (var id in members.Keys)
                {
                    SteamApi.SendPacket(id, data, Channel);
                    Peers.Add(id);
                }
            }
        }

        private static void Read()
        {
            var members = Members();
            for (int i = 0; i < MaxPacketsPerPoll; i++)
            {
                if (!SteamApi.TryReadPacket(Channel, out ulong sender, out byte[] data))
                {
                    return;
                }
                if (data.Length > MaxPacketBytes || !members.TryGetValue(sender, out var player))
                {
                    continue;
                }
                string[] parts = Encoding.UTF8.GetString(data).Split(';');
                if (parts[0] == Votes.Prefix)
                {
                    Votes.Receive(sender, player, parts);
                    continue;
                }
                if (parts[0] == SharedContinue.Prefix)
                {
                    SharedContinue.Receive(player, parts);
                    continue;
                }
                if (parts.Length != 6 || parts[0] != Prefix || !int.TryParse(parts[5], out int push))
                {
                    continue;
                }
                Received[sender] = (string.Join(";", parts.Skip(1).Take(4)), Time.realtimeSinceStartup);
                if (!player.IsClient && !FFSNetwork.IsHost)
                {
                    ApplyHostPush(push);
                }
            }
        }

        private static void ApplyHostPush(int push)
        {
            if (!Cfg.Enabled.Value)
            {
                return;
            }
            bool valid = push >= 0 && push < Presets.Labels.Length;
            Presets.SetHostSpeed(valid ? (SpeedPreset?)push : null);
        }

        private static void Reset()
        {
            foreach (var id in Peers)
            {
                SteamApi.CloseSession(id);
            }
            Peers.Clear();
            Received.Clear();
            Presets.SetHostSpeed(null);
        }
    }
}
