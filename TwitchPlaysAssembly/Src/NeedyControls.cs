using System;
using System.Collections.Specialized;
using System.Globalization;

public sealed class NeedyControls
{
    public float? FirstEventProbability { get; private set; }
    public float? CooldownMin { get; private set; }
    public float? CooldownMax { get; private set; }
    public bool HasOverrides { get { return FirstEventProbability.HasValue || CooldownMin.HasValue; } }

    public static NeedyControls Parse(NameValueCollection query)
    {
        var result = new NeedyControls();
        result.FirstEventProbability = ParseNumber(query["needyFirstEventProbability"]);
        result.CooldownMin = ParseNumber(query["needyCooldownMin"]);
        result.CooldownMax = ParseNumber(query["needyCooldownMax"]);
        // Zero is implemented with a negative sentinel because the game compares <=.
        if (result.FirstEventProbability.HasValue && (result.FirstEventProbability.Value < 0 || result.FirstEventProbability.Value > 1))
            throw new ArgumentException("needyFirstEventProbability must be between 0 and 1");
        if (result.CooldownMin.HasValue != result.CooldownMax.HasValue ||
            (result.CooldownMin.HasValue && (result.CooldownMin.Value < 0 || result.CooldownMax.Value < result.CooldownMin.Value)))
            throw new ArgumentException("needy cooldown bounds must both be nonnegative and ordered");
        return result;
    }

    private static float? ParseNumber(string value)
    {
        if (value == null) return null;
        float parsed;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || float.IsNaN(parsed) || float.IsInfinity(parsed))
            throw new ArgumentException("needy controls must be finite numbers");
        return parsed;
    }
}
