using System;
using System.Linq;
using Gloomhaven;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GloomSpeed
{
    internal enum SpeedPreset
    {
        Normal,
        FastForward,
        Faster,
        Fastest,
    }

    /// <summary>
    /// The one speed setting, which replaces the game's Auto Fast Forward row. Normal and FastForward are
    /// the game's own behaviour with Fast Forward off and on. Fastest uses the config values. Faster keeps
    /// the game's own multiplier and reveal: raising the multiplier slows the attacker's return to idle
    /// (2.50s per attack at 6x, 2.02s at 3x, in solo tests), and the shorter reveal only moved that time
    /// to the wait for the hit.
    /// </summary>
    internal static class Presets
    {
        internal const float GameMultiplier = 3f;
        internal static readonly string[] Labels = { "Normal", "Fast Forward", "Faster", "Fastest" };

        private static SpeedPreset lastFast = SpeedPreset.Fastest;

        internal static SpeedPreset Current => Cfg.Preset.Value;
        internal static string CurrentLabel => Labels[(int)Current];
        internal static bool Active => Current == SpeedPreset.Faster || Current == SpeedPreset.Fastest;
        internal static bool GameToggle => Current != SpeedPreset.Normal;
        internal static float Multiplier => Current == SpeedPreset.Fastest ? Cfg.Multiplier.Value : GameMultiplier;
        internal static float EnemyMultiplier => Current == SpeedPreset.Fastest ? Cfg.EnemyTurnMultiplier.Value : GameMultiplier;
        internal static float AttackModifierScale => Current == SpeedPreset.Fastest ? Cfg.AttackModifierTimeScale.Value : 1f;
        internal static float OtherBannerScale => Active ? Cfg.OtherBannerTimeScale.Value : 1f;
        internal static float CameraFocusScale => Active ? Cfg.CameraFocusSpeedScale.Value : 1f;

        /// <summary>Where the game's own toggle lands: its unbound Toggle Speed key and debug tools can flip it.</summary>
        internal static SpeedPreset FromGameToggle(bool on)
        {
            if (!on)
            {
                return SpeedPreset.Normal;
            }
            return lastFast;
        }

        /// <summary>Re-applies the values the game only reads when an object loads.</summary>
        internal static void ApplyLive()
        {
            try
            {
                if (Current != SpeedPreset.Normal)
                {
                    lastFast = Current;
                }
                AttackModBarAwakePatch.ApplyAll();
                CameraAwakePatch.Apply();

                var save = SaveData.Instance;
                if (save?.Global == null)
                {
                    return;
                }
                // Also moves a running Fast Forward to the new speed, through StartSpeedUpPatch.
                SpeedToggleSetterPatch.SetQuietly(save.Global, GameToggle);
                save.SaveGlobalData();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Applying the preset failed: " + e);
            }
        }
    }

    /// <summary>
    /// Replaces the Auto Fast Forward Animations row with Game Speed and adds the startup options, by
    /// copying the Display tab's display mode row, the game's own dropdown. The copies must not keep that
    /// row's language keys or callbacks. Options use plain English: the language files have no entries for this mod.
    /// </summary>
    [HarmonyPatch(typeof(GeneralSettings), "Awake")]
    internal static class SettingsRowPatch
    {
        private static readonly AccessTools.FieldRef<GeneralSettings, ButtonSwitch> SpeedRow =
            AccessTools.FieldRefAccess<GeneralSettings, ButtonSwitch>("scenarioSpeedUpToggle");

        private static readonly AccessTools.FieldRef<GeneralSettings, ButtonSwitch> CrossPlayRow =
            AccessTools.FieldRefAccess<GeneralSettings, ButtonSwitch>("crossPlayToggle");

        private static readonly AccessTools.FieldRef<ButtonSwitch, string> OnText =
            AccessTools.FieldRefAccess<ButtonSwitch, string>("onText");

        private static readonly AccessTools.FieldRef<GeneralSettings, ControllerInputAreaLocal> ControllerArea =
            AccessTools.FieldRefAccess<GeneralSettings, ControllerInputAreaLocal>("m_ControllerArea");

        private static readonly AccessTools.FieldRef<DisplaySettings, ExtendedDropdown> DisplayMode =
            AccessTools.FieldRefAccess<DisplaySettings, ExtendedDropdown>("displayModeSelectField");

        private static bool Prepare()
        {
            return Guard.Fields(typeof(SettingsRowPatch),
                (typeof(GeneralSettings), "scenarioSpeedUpToggle"),
                (typeof(GeneralSettings), "crossPlayToggle"),
                (typeof(ButtonSwitch), "onText"),
                (typeof(GeneralSettings), "m_ControllerArea"),
                (typeof(DisplaySettings), "displayModeSelectField"));
        }

        private static void Postfix(GeneralSettings __instance)
        {
            // A failure here must cost only the row, never the settings screen.
            try
            {
                Build(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not add the settings row: " + e);
            }
        }

        private static void Build(GeneralSettings settings)
        {
            var display = settings.transform.root.GetComponentInChildren<DisplaySettings>(true);
            if (display == null)
            {
                Plugin.Log.LogError("Could not add the settings row: the Display settings were not found.");
                return;
            }

            // DisplaySettings itself treats parent.parent of its dropdown as the whole row.
            var sourceRow = DisplayMode(display).transform.parent.parent;
            var neighbour = FindRow(SpeedRow(settings).transform, settings.transform);
            if (neighbour == null)
            {
                Plugin.Log.LogError("Could not add the settings row: its layout was not recognised.");
                return;
            }

            var area = ControllerArea(settings);
            var speed = AddRow(sourceRow, neighbour, neighbour, area, "Game Speed", Presets.Labels,
                (int)Presets.Current, index => Cfg.Preset.Value = (SpeedPreset)index, out var speedDropdown);

            // The game's Toggle Speed action can change the preset while this row exists.
            EventHandler follow = null;
            follow = (_, _) =>
            {
                if (speedDropdown == null)
                {
                    Cfg.Preset.SettingChanged -= follow;
                    return;
                }
                speedDropdown.SetValueWithoutNotify((int)Presets.Current);
                speedDropdown.RefreshShownValue();
            };
            Cfg.Preset.SettingChanged += follow;

            // Copied from Crossplay rather than Auto Fast Forward: its switch reads On and Off.
            var switchRow = FindRow(CrossPlayRow(settings).transform, settings.transform);
            if (switchRow == null)
            {
                switchRow = neighbour;
            }
            var intro = AddSwitchRow(switchRow, speed, area, "Skip Intro Logos", Cfg.SkipIntro);
            var epic = AddSwitchRow(switchRow, intro, area, "Skip Epic Login", Cfg.SkipEpicLogin);

            AddLinkRow(neighbour, epic, area);

            // Last, so any failure above leaves the game's own row in place.
            neighbour.gameObject.SetActive(false);

            Plugin.Log.LogInfo($"Settings rows added under {neighbour.parent.name}.");
        }

        /// <summary>
        /// A copy of the game's switch that keeps its own on and off looks. It reads Skip or Off, using the
        /// game's translated text for both.
        /// </summary>
        private static Transform AddSwitchRow(Transform switchRow, Transform after, ControllerInputAreaLocal area,
            string label, BepInEx.Configuration.ConfigEntry<bool> setting)
        {
            var copy = UnityEngine.Object.Instantiate(switchRow.gameObject, after.parent);
            copy.name = "GloomSpeedRow " + label;
            copy.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);
            copy.SetActive(true);

            var buttonSwitch = copy.GetComponentInChildren<ButtonSwitch>(true);
            var toggle = copy.GetComponentInChildren<ExtendedToggle>(true);
            var labelText = copy.GetComponentsInChildren<TextMeshProUGUI>(true)
                .First(text => !text.transform.IsChildOf(toggle.transform));

            // Only the label's language key goes. The switch's own text keys set its On and Off wording.
            var labelKey = labelText.GetComponent<TextLocalizedListener>();
            if (labelKey != null)
            {
                UnityEngine.Object.DestroyImmediate(labelKey);
            }
            labelText.text = label;

            for (int i = 0; i < toggle.onValueChanged.GetPersistentEventCount(); i++)
            {
                toggle.onValueChanged.SetPersistentListenerState(i, UnityEventCallState.Off);
            }
            OnText(buttonSwitch) = "GUI_SKIP";
            buttonSwitch.SetValue(setting.Value);
            buttonSwitch.OnValueChanged.AddListener(on => setting.Value = on);

            area.OnFocusedArea.AddListener(() => buttonSwitch.SetNavigation(Navigation.Mode.Vertical));
            area.OnUnfocusedArea.AddListener(() => buttonSwitch.DisableNavigation());
            buttonSwitch.DisableNavigation();
            return copy.transform;
        }

        /// <summary>Shows the mod version with a button to the repository, by copying the game's switch row.</summary>
        private static void AddLinkRow(Transform switchRow, Transform after, ControllerInputAreaLocal area)
        {
            CopyAsPlainButton(switchRow, after, area, "GloomSpeed v" + Plugin.Version, out var toggle, out var value);
            value.text = "View on GitHub";
            toggle.onValueChanged.AddListener(_ =>
            {
                toggle.SetIsOnWithoutNotify(true);
                Application.OpenURL(Plugin.RepoUrl);
            });
        }

        /// <summary>A copy of the game's switch row that's just a labelled button with no setting behind it.</summary>
        private static GameObject CopyAsPlainButton(Transform switchRow, Transform after, ControllerInputAreaLocal area,
            string label, out ExtendedToggle toggle, out TextMeshProUGUI value)
        {
            var copy = UnityEngine.Object.Instantiate(switchRow.gameObject, after.parent);
            copy.name = "GloomSpeedRow " + label;
            copy.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);

            foreach (var localized in copy.GetComponentsInChildren<LocalizedListener>(true))
            {
                UnityEngine.Object.DestroyImmediate(localized);
            }
            foreach (var buttonSwitch in copy.GetComponentsInChildren<ButtonSwitch>(true))
            {
                UnityEngine.Object.DestroyImmediate(buttonSwitch);
            }

            var field = copy.GetComponentInChildren<ExtendedToggle>(true);
            field.onValueChanged = new Toggle.ToggleEvent();
            field.onSelected = new UnityEvent();
            field.onDeselected = new UnityEvent();
            field.onMouseEnter = new UnityEvent();
            field.onMouseExit = new UnityEvent();
            field.group = null;
            field.SetIsOnWithoutNotify(true);

            value = field.GetComponentInChildren<TextMeshProUGUI>(true);
            copy.GetComponentsInChildren<TextMeshProUGUI>(true)
                .First(text => !text.transform.IsChildOf(field.transform))
                .text = label;

            area.OnFocusedArea.AddListener(() => field.SetNavigation(Navigation.Mode.Vertical));
            area.OnUnfocusedArea.AddListener(() => field.DisableNavigation());
            field.DisableNavigation();
            copy.SetActive(true);

            toggle = field;
            return copy;
        }

        private static Transform AddRow(Transform sourceRow, Transform after, Transform layoutFrom,
            ControllerInputAreaLocal area, string label, string[] options, int value, UnityAction<int> changed,
            out ExtendedDropdown dropdown)
        {
            var copy = UnityEngine.Object.Instantiate(sourceRow.gameObject, after.parent);
            copy.name = "GloomSpeedRow " + label;
            copy.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);
            MatchLayout((RectTransform)copy.transform, (RectTransform)layoutFrom);

            foreach (var localized in copy.GetComponentsInChildren<LocalizedListener>(true))
            {
                UnityEngine.Object.DestroyImmediate(localized);
            }

            var field = copy.GetComponentInChildren<ExtendedDropdown>(true);
            field.onValueChanged = new TMP_Dropdown.DropdownEvent();
            field.OnCreatedOptions = new UnityEvent();
            field.OnSelected = new UnityEvent();
            field.OnDeselected = new UnityEvent();
            field.OnClosed = new UnityEvent();

            field.ClearOptions();
            field.AddOptions(options.ToList());
            field.SetValueWithoutNotify(value);
            field.RefreshShownValue();
            field.onValueChanged.AddListener(changed);

            copy.GetComponentsInChildren<TextMeshProUGUI>(true)
                .First(text => !text.transform.IsChildOf(field.transform))
                .text = label;

            area.OnFocusedArea.AddListener(() => field.SetNavigation(Navigation.Mode.Vertical));
            area.OnUnfocusedArea.AddListener(() => field.DisableNavigation());
            field.OnClosed.AddListener(() =>
            {
                if (area.IsFocused)
                {
                    field.Select();
                }
            });
            field.DisableNavigation();
            copy.SetActive(true);

            dropdown = field;
            return copy.transform;
        }

        /// <summary>The Display tab may be laid out differently, so size the copy like its new neighbour.</summary>
        private static void MatchLayout(RectTransform copy, RectTransform neighbour)
        {
            copy.anchorMin = neighbour.anchorMin;
            copy.anchorMax = neighbour.anchorMax;
            copy.pivot = neighbour.pivot;
            copy.sizeDelta = neighbour.sizeDelta;
            copy.localScale = neighbour.localScale;

            var source = neighbour.GetComponent<LayoutElement>();
            var target = copy.GetComponent<LayoutElement>();
            if (source != null && target != null)
            {
                target.minHeight = source.minHeight;
                target.preferredHeight = source.preferredHeight;
                target.flexibleWidth = source.flexibleWidth;
            }
        }

        /// <summary>The smallest ancestor holding the row's label as well as its button.</summary>
        private static Transform FindRow(Transform button, Transform stop)
        {
            for (var current = button; current != null && current != stop; current = current.parent)
            {
                if (current.GetComponentsInChildren<TextMeshProUGUI>(true).Length >= 2)
                {
                    return current;
                }
            }
            return null;
        }
    }
}
