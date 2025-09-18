using System.Text.RegularExpressions;
using UnityEngine;

namespace Satie
{
    internal static class SatieUtil
    {
        // "birds/01to08"  → "birds/05" (random)
        static readonly Regex clipRangeRx =
            new(@"^(.*\/)?(\d+)to(\d+)$",  // folders optional
                RegexOptions.Compiled);

        public static string ResolveClip(string pattern)
        {
            var m = clipRangeRx.Match(pattern);
            if (!m.Success)
                return pattern;

            int min = int.Parse(m.Groups[2].Value);
            int max = int.Parse(m.Groups[3].Value) + 1;
            int choice = Random.Range(min, max);

            // preserve zero-padding width (001, 0007, …)
            int digits = m.Groups[2].Value.Length;
            string idx = choice.ToString().PadLeft(digits, '0');

            string prefix = m.Groups[1].Success ? m.Groups[1].Value : string.Empty;
            return prefix + idx;
        }
    }
}