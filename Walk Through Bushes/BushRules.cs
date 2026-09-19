using System;
internal static class BushRules
{
    internal static bool IsBushName(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        // Names and object IDs observed in the supplied game assets.
        // Prefix boundary excludes unrelated strings such as "ambush".
        return value == "bush" || value.StartsWith("bush_", StringComparison.Ordinal)
            || value == "bush(Clone)" || value == "barry_bush" || value == "barry_bush(Clone)";
    }
}
