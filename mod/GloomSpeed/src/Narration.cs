using System;
using System.Linq;
using ClockStone;
using HarmonyLib;
using UnityEngine;

namespace GloomSpeed
{
    /// <summary>
    /// Narration speed. Every narrated line plays through the story box, as a ClockStone audio item in a
    /// VONarration category. Raising the source's pitch plays it faster but higher, so a filter on the same
    /// object shifts the pitch back, leaving only the tempo changed. Narration is local to each player.
    /// </summary>
    internal static class Narration
    {
        internal static readonly float[] Speeds = { 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };
        internal static readonly string[] Labels = Speeds.Select(speed => speed.ToString("0.##") + "x").ToArray();

        internal static int SpeedIndex
        {
            get
            {
                float speed = Cfg.NarrationSpeed.Value;
                int best = 1;
                for (int i = 0; i < Speeds.Length; i++)
                {
                    if (Mathf.Abs(Speeds[i] - speed) < Mathf.Abs(Speeds[best] - speed))
                    {
                        best = i;
                    }
                }
                return best;
            }
        }

        internal static float Speed => Speeds[SpeedIndex];

        internal static bool IsNarration(AudioSubItem subItem)
        {
            for (var category = subItem?.item?.category; category != null; category = category.parentCategory)
            {
                if (category.Name != null && category.Name.StartsWith("VONarration", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(AudioController), nameof(AudioController.PlayAudioSubItem))]
    internal static class NarrationSpeedPatch
    {
        // The game resets pitch on every play, and audio objects are pooled and shared with other sounds,
        // so this sets both the pitch and the filter each time, including switching the filter off again.
        private static void Postfix(AudioSubItem subItem, AudioObject __result)
        {
            var source = __result != null ? __result.primaryAudioSource : null;
            if (source == null)
            {
                return;
            }
            var filter = source.GetComponent<NarrationPitchKeeper>();
            float speed = Narration.IsNarration(subItem) ? Narration.Speed : 1f;
            if (Mathf.Approximately(speed, 1f))
            {
                if (filter != null)
                {
                    filter.enabled = false;
                }
                return;
            }
            source.pitch *= speed;
            if (filter == null)
            {
                filter = source.gameObject.AddComponent<NarrationPitchKeeper>();
            }
            filter.Begin(1f / speed);
        }
    }

    /// <summary>
    /// Shifts pitch without changing length, by reading the recent sound back through two overlapping
    /// taps whose delay sweeps across a short window, crossfaded so each tap is silent as it jumps back.
    /// Runs on the audio thread.
    /// </summary>
    internal sealed class NarrationPitchKeeper : MonoBehaviour
    {
        private const float WindowSeconds = 0.06f;
        private const int BufferSamples = 16384;
        private const int MaxChannels = 8;

        private readonly float[,] _buffer = new float[MaxChannels, BufferSamples];
        private int _write;
        private double _phase;
        private float _window;
        private volatile float _ratio = 1f;
        private volatile bool _reset;

        private void Awake()
        {
            _window = Mathf.Min(WindowSeconds * AudioSettings.outputSampleRate, BufferSamples / 2f - 4f);
        }

        internal void Begin(float ratio)
        {
            _ratio = ratio;
            _reset = true;
            enabled = true;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels > MaxChannels || _window <= 0f)
            {
                return;
            }
            if (_reset)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _write = 0;
                _phase = 0;
                _reset = false;
            }
            double step = (1.0 - _ratio) / _window;
            for (int i = 0; i < data.Length; i += channels)
            {
                for (int c = 0; c < channels; c++)
                {
                    _buffer[c, _write] = data[i + c];
                }
                _phase += step;
                _phase -= Math.Floor(_phase);
                double other = _phase + 0.5;
                other -= Math.Floor(other);
                // sin² windows half a cycle apart always sum to one, so the level stays constant.
                double gainA = Math.Sin(Math.PI * _phase);
                double gainB = Math.Sin(Math.PI * other);
                gainA *= gainA;
                gainB *= gainB;
                double delayA = 1.0 + _phase * _window;
                double delayB = 1.0 + other * _window;
                for (int c = 0; c < channels; c++)
                {
                    data[i + c] = (float)(Read(c, delayA) * gainA + Read(c, delayB) * gainB);
                }
                _write = (_write + 1) % BufferSamples;
            }
        }

        private double Read(int channel, double delay)
        {
            double position = _write - delay;
            if (position < 0)
            {
                position += BufferSamples;
            }
            int index = (int)position;
            double fraction = position - index;
            float a = _buffer[channel, index % BufferSamples];
            float b = _buffer[channel, (index + 1) % BufferSamples];
            return a + (b - a) * fraction;
        }
    }

    /// <summary>
    /// Moves to the next narrated line once its voice has finished, as if the player had clicked. Lines
    /// with no voice still wait for a click, since there's no natural end to wait for.
    /// </summary>
    internal static class NarrationAutoAdvance
    {
        private const float PauseAfterLine = 0.6f;

        private static readonly AccessTools.FieldRef<UICharacterStoryBox, UnityEngine.UI.Button> SkipButton =
            AccessTools.FieldRefAccess<UICharacterStoryBox, UnityEngine.UI.Button>("skipButton");

        private static UICharacterStoryBox _box;
        private static string _audioId;
        private static bool _heard;
        private static float _endedAt = -1f;

        internal static void LineShown(UICharacterStoryBox box, string audioId)
        {
            _box = box;
            _audioId = string.IsNullOrEmpty(audioId) ? null : audioId;
            _heard = false;
            _endedAt = -1f;
        }

        internal static void Tick()
        {
            if (!Cfg.NarrationAutoAdvance.Value || _box == null || _audioId == null)
            {
                return;
            }
            if (!_box.gameObject.activeInHierarchy)
            {
                _box = null;
                return;
            }
            // A line's sound can start a moment after the line, so wait to hear it before waiting for its end.
            if (AudioControllerUtils.IsPlaying(_audioId))
            {
                _heard = true;
                _endedAt = -1f;
                return;
            }
            if (!_heard)
            {
                return;
            }
            if (_endedAt < 0f)
            {
                _endedAt = Time.unscaledTime;
                return;
            }
            var button = SkipButton(_box);
            if (Time.unscaledTime - _endedAt >= PauseAfterLine && button != null && button.interactable)
            {
                _audioId = null;
                _box.Skip();
            }
        }
    }

    [HarmonyPatch(typeof(UICharacterStoryBox), "DecorateLine")]
    internal static class NarrationLinePatch
    {
        private static void Postfix(UICharacterStoryBox __instance, DialogLineDTO line)
        {
            NarrationAutoAdvance.LineShown(__instance, line?.narrativeAudioId);
        }
    }
}
