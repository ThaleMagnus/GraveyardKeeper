using System;

internal static class FurnaceSpeedRules
{
    internal static float ScaleDelta(string objectId, float delta, float multiplier)
    {
        // Exact object IDs: do not match ovens, alchemy equipment, construction or upgrades.
        if (objectId != "mf_furnace_0" && objectId != "mf_furnace_1" && objectId != "mf_furnace_2") return delta;
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0f) return delta;
        if (float.IsNaN(multiplier)) multiplier = 1f;
        multiplier = Math.Max(1f, Math.Min(10f, multiplier));
        float scaled = delta * multiplier;
        return float.IsInfinity(scaled) ? delta : scaled;
    }
}
