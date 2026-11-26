using System;
using System.Collections.Specialized;

static class Test
{
    static void Main()
    {
        var duration = NeedyControls.Parse(new NameValueCollection { { "needyResponseDuration", "20" } });
        Require(duration.HasOverrides && duration.ResponseDuration == 20, "response duration");
        foreach (var badDuration in new[] { "0", "-1", "NaN", "Infinity" })
        {
            try
            {
                NeedyControls.Parse(new NameValueCollection { { "needyResponseDuration", badDuration } });
                throw new Exception("accepted invalid duration " + badDuration);
            }
            catch (ArgumentException) { }
        }
        var omitted = NeedyControls.Parse(new NameValueCollection());
        Require(!omitted.HasOverrides, "omitted controls");
        var values = new NameValueCollection { { "needyFirstEventProbability", "1" }, { "needyCooldownMin", "0" }, { "needyCooldownMax", "0" } };
        var zero = NeedyControls.Parse(new NameValueCollection { { "needyFirstEventProbability", "0" } });
        Require(zero.FirstEventProbability == 0, "zero probability");
        var settings = NeedyControls.Parse(values);
        Require(settings.FirstEventProbability == 1 && settings.CooldownMin == 0 && settings.CooldownMax == 0, "valid settings and zero cooldown");
        foreach (var bad in new[] { "NaN", "-0.1", "1.1" })
        {
            try { NeedyControls.Parse(new NameValueCollection { { "needyFirstEventProbability", bad } }); throw new Exception("accepted invalid probability " + bad); }
            catch (ArgumentException) { }
        }
        foreach (var pair in new[] { new[] { "1", "0" }, new[] { "-1", "3" }, new[] { "0", "NaN" }, new[] { "1", null } })
        {
            var input = new NameValueCollection { { "needyCooldownMin", pair[0] } };
            if (pair[1] != null) input.Add("needyCooldownMax", pair[1]);
            try { NeedyControls.Parse(input); throw new Exception("accepted invalid cooldown"); }
            catch (ArgumentException) { }
        }
        Console.WriteLine("Needy control validation passed");
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
