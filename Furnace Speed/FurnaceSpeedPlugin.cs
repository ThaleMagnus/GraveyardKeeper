using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

[BepInPlugin(PluginId, "Furnace Speed", "1.0.0")]
public sealed class FurnaceSpeedPlugin : BaseUnityPlugin
{
    public const string PluginId = "com.thalethegreat.furnacespeed";
    private static FurnaceSpeedPlugin instance;
    private ConfigEntry<float> multiplier;
    private Harmony harmony;

    private void Awake()
    {
        multiplier = Config.Bind("General", "Speed Multiplier", 2f,
            new ConfigDescription("Automatic crafting speed for Furnace I, II and III. 1 = normal, 10 = ten times speed. Fuel and ingredients per recipe are unchanged.",
                new AcceptableValueRange<float>(1f, 10f)));
        instance = this;
        try
        {
            harmony = new Harmony(PluginId);
            var target = AccessTools.DeclaredMethod(typeof(CraftComponent), "ReallyUpdateComponent", new[] { typeof(float) });
            if (target == null) throw new MissingMethodException("CraftComponent.ReallyUpdateComponent(float)");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(FurnaceSpeedPlugin), nameof(BeforeCraftUpdate)));
            Logger.LogInfo("Furnace Speed 1.0.0 loaded. Multiplier: " + multiplier.Value);
        }
        catch (Exception ex)
        {
            if (harmony != null) harmony.UnpatchSelf();
            enabled = false;
            Logger.LogError("Furnace Speed could not initialize: " + ex);
        }
    }

    private static void BeforeCraftUpdate(CraftComponent __instance, ref float __0)
    {
        var plugin = instance;
        if (plugin == null || !plugin.isActiveAndEnabled || __instance == null) return;
        var wgo = __instance.wgo;
        if (wgo == null || wgo.is_removing) return;
        var craft = __instance.current_craft;
        if (!__instance.is_crafting || craft == null || !craft.is_auto) return;
        __0 = FurnaceSpeedRules.ScaleDelta(wgo.obj_id, __0, plugin.multiplier.Value);
    }

    private void OnDestroy()
    {
        if (harmony != null) harmony.UnpatchSelf();
        if (ReferenceEquals(instance, this)) instance = null;
    }
}

