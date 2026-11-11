using System;
using System.Collections.Generic;
using HarmonyLib;
using GuangJueDeepDrill;
using RimWorld;
using UnityEngine;
using Verse;

namespace WideDeepDrillPlus
{
    public sealed class WideDeepDrillPlusMod : Mod
    {
        public static WideDeepDrillPlusSettings Settings;
        public static WideDeepDrillPlusMod Instance { get; private set; }

        public WideDeepDrillPlusMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<WideDeepDrillPlusSettings>();
            DrillSettingsWindowPatch.ApplyAutoDrillWorkPerTick();
        }

        public override string SettingsCategory()
        {
            return "WDDP_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            DrillSettingsWindowPatch.DrawWorkSettings(listing, Settings);
            listing.End();
        }

        internal static void ApplyWorkPerTick(ThingDef def)
        {
            if (Settings == null || def == null || def.comps == null)
            {
                return;
            }

            for (int i = 0; i < def.comps.Count; i++)
            {
                CompProperties_GJDD_AutoDeepDrill props = def.comps[i] as CompProperties_GJDD_AutoDeepDrill;
                if (props != null)
                {
                    props.workPerTick = Mathf.Clamp(Settings.workPerTick, WideDeepDrillPlusSettings.MinWorkPerTick, WideDeepDrillPlusSettings.MaxWorkPerTick);
                    return;
                }
            }
        }
    }

    public sealed class WideDeepDrillPlusSettings : ModSettings
    {
        internal const float MinWorkSpeedFactor = 0.2f;
        internal const float MaxWorkSpeedFactor = 500f;
        internal const float MinWorkPerTick = 1f;
        internal const float MaxWorkPerTick = 60000f;

        public float workSpeedFactor = 1f;
        public float workPerTick = 1f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workSpeedFactor, "workSpeedFactor", 1f);
            Scribe_Values.Look(ref workPerTick, "workPerTick", MinWorkPerTick);
            workSpeedFactor = Mathf.Clamp(workSpeedFactor, MinWorkSpeedFactor, MaxWorkSpeedFactor);
            workPerTick = Mathf.Clamp(workPerTick, MinWorkPerTick, MaxWorkPerTick);
        }
    }

    [StaticConstructorOnStartup]
    internal static class WideDeepDrillPlusBootstrap
    {
        static WideDeepDrillPlusBootstrap()
        {
            new Harmony("lilly.widedeepdrillplus").PatchAll();
            ApplyToAutoDrillDefs();
        }

        internal static void ApplyToAutoDrillDefs()
        {
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].defName == "GuangJueDeepDrill_Auto")
                {
                    WideDeepDrillPlusMod.ApplyWorkPerTick(defs[i]);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GuangJueDeepDrillSettings), nameof(GuangJueDeepDrillSettings.GetWorkSpeedFactor))]
    internal static class WorkSpeedFactorPatch
    {
        private static bool Prefix(GuangJueDeepDrillSettings __instance, ref float __result)
        {
            float workSpeedFactor = WideDeepDrillPlusMod.Settings == null
                ? __instance.workSpeedFactor
                : WideDeepDrillPlusMod.Settings.workSpeedFactor;
            __result = Mathf.Clamp(workSpeedFactor, WideDeepDrillPlusSettings.MinWorkSpeedFactor, WideDeepDrillPlusSettings.MaxWorkSpeedFactor);
            return false;
        }
    }

    [HarmonyPatch(typeof(GuangJueDeepDrillSettings), nameof(GuangJueDeepDrillSettings.DoWindowContents))]
    internal static class DrillSettingsWindowPatch
    {
        private static string workSpeedBuffer = string.Empty;
        private static string workPerTickBuffer = string.Empty;
        private static List<ThingDef> cachedDeepResources;
        private static Vector2 scrollPosition;
        private static float scrollViewHeight = 300f;
        private static bool settingsSavePending;
        private static float lastSettingsChangeTime;

        private static bool Prefix(Rect inRect, GuangJueDeepDrillSettings __instance)
        {
            WideDeepDrillPlusSettings plusSettings = WideDeepDrillPlusMod.Settings;
            if (plusSettings == null)
            {
                return true;
            }

            float lineHeight = Text.LineHeight;
            float headerHeight = lineHeight * 6f + 12f;
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
            Listing_Standard headerListing = new Listing_Standard();
            headerListing.Begin(headerRect);

            DrawWorkSettings(headerListing, plusSettings);
            __instance.workSpeedFactor = plusSettings.workSpeedFactor;

            headerListing.End();

            Rect listRect = new Rect(inRect.x, headerRect.yMax + 6f, inRect.width, inRect.height - headerRect.height - 6f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.Begin(viewRect);
            list.Label("GJDD_SettingsResources".Translate());
            list.Label("GJDD_SettingsResourcesNote".Translate());
            list.GapLine();

            foreach (ThingDef def in GetDeepResourceDefs())
            {
                bool enabled = __instance.IsResourceEnabled(def);
                list.CheckboxLabeled(def.LabelCap, ref enabled);
                __instance.SetResourceEnabled(def, enabled);
            }

            scrollViewHeight = list.CurHeight + 6f;
            list.End();
            Widgets.EndScrollView();
            return false;
        }

        private static void DrawSliderWithNumericField(Listing_Standard listing, ref float value, ref string buffer, float min, float max)
        {
            Rect rowRect = listing.GetRect(30f);
            float fieldWidth = Mathf.Min(90f, rowRect.width * 0.35f);
            float sliderWidth = Mathf.Max(0f, rowRect.width - fieldWidth - 10f);
            Rect sliderRect = new Rect(rowRect.x, rowRect.y, sliderWidth, rowRect.height);
            Rect fieldRect = new Rect(sliderRect.xMax + 10f, rowRect.y, fieldWidth, rowRect.height);

            float previousValue = value;
            float sliderValue = Widgets.HorizontalSlider(sliderRect, value, min, max);
            if (!Mathf.Approximately(sliderValue, previousValue))
            {
                value = sliderValue;
                buffer = value.ToString("0.##");
            }

            Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, min, max);
            value = Mathf.Clamp(value, min, max);
            if (!Mathf.Approximately(value, previousValue))
            {
                buffer = value.ToString("0.##");
            }
        }

        internal static void DrawWorkSettings(Listing_Standard listing, WideDeepDrillPlusSettings settings)
        {
            float previousWorkSpeedFactor = settings.workSpeedFactor;
            listing.Label("WDDP_SettingsWorkSpeed".Translate());
            int percent = Mathf.RoundToInt(settings.workSpeedFactor * 100f);
            listing.Label("WDDP_SettingsWorkSpeedValue".Translate(percent));
            DrawSliderWithNumericField(
                listing,
                ref settings.workSpeedFactor,
                ref workSpeedBuffer,
                WideDeepDrillPlusSettings.MinWorkSpeedFactor,
                WideDeepDrillPlusSettings.MaxWorkSpeedFactor);

            listing.Label("WDDP_SettingsWorkPerTick".Translate());
            float previousWorkPerTick = settings.workPerTick;
            DrawSliderWithNumericField(
                listing,
                ref settings.workPerTick,
                ref workPerTickBuffer,
                WideDeepDrillPlusSettings.MinWorkPerTick,
                WideDeepDrillPlusSettings.MaxWorkPerTick);
            bool workPerTickChanged = !Mathf.Approximately(previousWorkPerTick, settings.workPerTick);
            if (workPerTickChanged)
            {
                ApplyAutoDrillWorkPerTick();
            }

            if (workPerTickChanged || !Mathf.Approximately(previousWorkSpeedFactor, settings.workSpeedFactor))
            {
                settingsSavePending = true;
                lastSettingsChangeTime = Time.realtimeSinceStartup;
            }

            if (settingsSavePending && Time.realtimeSinceStartup - lastSettingsChangeTime >= 0.5f)
                SaveSettings();
        }

        internal static void SaveSettings()
        {
            settingsSavePending = false;
            WideDeepDrillPlusMod.Instance?.WriteSettings();
        }

        internal static void ApplyAutoDrillWorkPerTick()
        {
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].defName == "GuangJueDeepDrill_Auto")
                {
                    WideDeepDrillPlusMod.ApplyWorkPerTick(defs[i]);
                }
            }
        }

        private static List<ThingDef> GetDeepResourceDefs()
        {
            if (cachedDeepResources != null)
            {
                return cachedDeepResources;
            }

            cachedDeepResources = new List<ThingDef>();
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                if (allDefs[i].deepCommonality > 0f)
                {
                    cachedDeepResources.Add(allDefs[i]);
                }
            }

            cachedDeepResources.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, StringComparison.OrdinalIgnoreCase));
            return cachedDeepResources;
        }
    }

    [HarmonyPatch(typeof(Dialog_ModSettings), nameof(Dialog_ModSettings.PreClose))]
    internal static class Dialog_ModSettings_PreClose_Patch
    {
        private static void Postfix()
        {
            DrillSettingsWindowPatch.SaveSettings();
        }
    }

    [HarmonyPatch(typeof(CompGJDD_AutoDeepDrill), nameof(CompGJDD_AutoDeepDrill.PostSpawnSetup))]
    internal static class AutoDrillWorkPerTickPatch
    {
        private static void Postfix(CompGJDD_AutoDeepDrill __instance)
        {
            if (__instance.parent != null)
            {
                WideDeepDrillPlusMod.ApplyWorkPerTick(__instance.parent.def);
            }
        }
    }
}
