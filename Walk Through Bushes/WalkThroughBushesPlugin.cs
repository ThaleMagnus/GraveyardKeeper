using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[BepInPlugin(Id, "Walk Through Bushes", "1.0.0")]
public sealed class WalkThroughBushesPlugin : BaseUnityPlugin
{
    public const string Id = "com.thalethegreat.walkthroughbushes";
    private static WalkThroughBushesPlugin instance;
    private ConfigEntry<bool> allow;
    private Harmony harmony;
    private Collider2D[] nearby = new Collider2D[128];
    private readonly List<Pair> owned = new List<Pair>();
    private sealed class Pair { public Collider2D Player, Bush; public bool Seen; }

    private void Awake()
    {
        instance = this;
        allow = Config.Bind("General", "Enabled", true, "Allow the player to walk through bushes, including berry bushes. Interaction triggers remain enabled.");
        allow.SettingChanged += OnSettingChanged;
        try
        {
            harmony = new Harmony(Id);
            var target = AccessTools.DeclaredMethod(typeof(MovementComponent), "FixedUpdateComponent");
            if (target == null) throw new MissingMethodException("MovementComponent.FixedUpdateComponent");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(WalkThroughBushesPlugin), nameof(BeforeMovement)));
            Logger.LogInfo("Walk Through Bushes 1.0.0 loaded.");
        }
        catch (Exception ex)
        {
            if (harmony != null) harmony.UnpatchSelf();
            enabled = false;
            Logger.LogError("Walk Through Bushes could not initialize: " + ex);
        }
    }

    private static void BeforeMovement(MovementComponent __instance)
    {
        var p = instance;
        if (p == null || !p.isActiveAndEnabled || !p.allow.Value || MainGame.me == null) return;
        if (__instance.wgo != MainGame.me.player || MainGame.me.player == null) return;
        try { p.Refresh(MainGame.me.player); }
        catch (Exception ex)
        {
            p.Restore();
            p.enabled = false;
            p.Logger.LogError("Bush collision update stopped: " + ex);
        }
    }

    private static bool IsBush(Collider2D collider)
    {
        for (Transform t = collider.transform; t != null; t = t.parent)
        {
            var wgo = t.GetComponent<WorldGameObject>();
            if (wgo != null && BushRules.IsBushName(wgo.obj_id)) return true;
            if (BushRules.IsBushName(t.name)) return true;
            // Do not inherit a bush name from an unrelated world object's parent.
            if (wgo != null && !string.IsNullOrEmpty(wgo.obj_id)) return false;
        }
        return false;
    }

    private void Refresh(WorldGameObject player)
    {
        foreach (var pair in owned) pair.Seen = false;
        var bodies = player.GetComponentsInChildren<Collider2D>();
        Vector2 center = player.transform.position;
        float radius = 6f;
        foreach (var body in bodies)
            if (body != null && body.enabled && !body.isTrigger)
                radius = Mathf.Max(radius, Vector2.Distance(center, body.bounds.center) + body.bounds.extents.magnitude + 4f);
        int count;
        do
        {
            count = Physics2D.OverlapCircleNonAlloc(center, radius, nearby, ~0);
            if (count < nearby.Length) break;
            Array.Resize(ref nearby, nearby.Length * 2);
        } while (true);

        for (int i = 0; i < count; i++)
        {
            var bush = nearby[i];
            if (bush == null || !bush.enabled || bush.isTrigger || bush.transform.IsChildOf(player.transform) || !IsBush(bush)) continue;
            foreach (var body in bodies)
            {
                if (body == null || !body.enabled || body.isTrigger) continue;
                Pair found = null;
                foreach (var pair in owned)
                    if (pair.Player == body && pair.Bush == bush) { found = pair; break; }
                if (found == null)
                {
                    // Leave collision ignores owned by another system alone.
                    if (Physics2D.GetIgnoreCollision(body, bush)) continue;
                    found = new Pair { Player = body, Bush = bush };
                    owned.Add(found);
                }
                found.Seen = true;
                if (!Physics2D.GetIgnoreCollision(body, bush)) Physics2D.IgnoreCollision(body, bush, true);
            }
        }
        Array.Clear(nearby, 0, count);
        for (int i = owned.Count - 1; i >= 0; i--)
            if (!owned[i].Seen) { RestorePair(owned[i]); owned.RemoveAt(i); }
    }

    private static void RestorePair(Pair pair)
    {
        if (pair.Player != null && pair.Bush != null)
            Physics2D.IgnoreCollision(pair.Player, pair.Bush, false);
    }
    private void Restore() { foreach (var pair in owned) RestorePair(pair); owned.Clear(); }
    private void OnSettingChanged(object sender, EventArgs args) { if (!allow.Value) Restore(); }
    private void OnDisable() { Restore(); }
    private void OnDestroy()
    {
        Restore();
        if (allow != null) allow.SettingChanged -= OnSettingChanged;
        if (harmony != null) harmony.UnpatchSelf();
        if (ReferenceEquals(instance, this)) instance = null;
    }
}
