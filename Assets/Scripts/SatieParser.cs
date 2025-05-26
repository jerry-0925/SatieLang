using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Satie
{
    // One entry per “loop/oneshot” block in the script.
    public sealed class Statement
    {
        // Playback
        public string kind;                    // "loop" | "oneshot"
        public string clip;
        public RangeOrValue starts_at = RangeOrValue.Zero;
        public RangeOrValue duration = RangeOrValue.Null;  // loop-only
        public RangeOrValue every = RangeOrValue.Zero;  // oneshot
        public RangeOrValue volume = new(1f);
        public RangeOrValue pitch = new(1f);
        public bool         overlap = false;
        public float        fade_in = 0f;
        public float        fade_out = 0f;

        // Spatial movement
        public enum WanderType { None, Walk, Fly, Fixed }
        public WanderType wanderType = WanderType.None;
        public Vector3 areaMin, areaMax;   // rect / box / fixed point
        public float   wanderHz = 0.3f;    // speed for Walk / Fly

        // Debug helpers
        public bool visualize = false;     // add TrailRenderer if true
    }

    // Utility: single value or min..max range with easy sampling.
    public readonly struct RangeOrValue
    {
        public readonly float min, max;
        public readonly bool  isRange, isSet;

        public static readonly RangeOrValue Zero = new(0f);
        public static readonly RangeOrValue Null = default;

        public RangeOrValue(float v) { min = max = v; isRange = false; isSet = true; }
        public RangeOrValue(float a, float b) { min = a; max = b; isRange = true;  isSet = true; }

        public float Sample() => !isSet ? 0f : (isRange ? Random.Range(min, max) : min);

        public static RangeOrValue Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Null;
            if (s.Contains(".."))
            {
                var p = s.Split("..");
                return new(float.Parse(p[0]), float.Parse(p[1]));
            }
            return new(float.Parse(s));
        }
    }

    // Main parser.
    public static class SatieParser
    {
        // Matches header line + indented block.
        static readonly Regex StmtRx = new(
            @"^(?<kind>loop|oneshot)\s+""(?<clip>.+?)""\s*(?:every\s+(?<e1>\d+\.?\d*)\.\.(?<e2>\d+\.?\d*))?\s*:\s*\r?\n" +
            @"(?<block>(?:[ \t]+.*\r?\n?)*)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Matches “key = value” inside block.
        static readonly Regex PropRx = new(
            @"^[ \t]+(?<key>\w+)\s*=\s*(?<val>[^\r\n#]+)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // Public entry: parse whole file.
        public static List<Statement> Parse(string scriptText)
        {
            var list = new List<Statement>();

            foreach (Match m in StmtRx.Matches(scriptText))
            {
                var s = new Statement
                {
                    kind = m.Groups["kind"].Value.ToLower(),
                    clip = m.Groups["clip"].Value.Trim()
                };

                if (m.Groups["e1"].Success)
                    s.every = new RangeOrValue(
                        float.Parse(m.Groups["e1"].Value),
                        float.Parse(m.Groups["e2"].Value));

                foreach (Match p in PropRx.Matches(m.Groups["block"].Value))
                {
                    string k = p.Groups["key"].Value.ToLower();
                    string v = p.Groups["val"].Value.Trim();

                    switch (k)
                    {
                        case "volume": s.volume = RangeOrValue.Parse(v); break;
                        case "pitch": s.pitch = RangeOrValue.Parse(v); break;
                        case "starts_at": s.starts_at = RangeOrValue.Parse(v); break;
                        case "duration": s.duration  = RangeOrValue.Parse(v); break;
                        case "fade_in": s.fade_in = float.Parse(v); break;
                        case "fade_out": s.fade_out = float.Parse(v); break;
                        case "overlap": s.overlap = v.ToLower().StartsWith("t"); break;
                        case "visualize": s.visualize = v.ToLower().StartsWith("t"); break;
                        case "move": ParseMove(s, v); break;
                    }
                }
                list.Add(s);
            }
            return list;
        }

        // Helper: remove extension, add Resources/Audio prefix.
        public static string PathFor(string clip)
            => $"Audio/{Path.GetFileNameWithoutExtension(clip)}";

        // Parses “move = …” forms: walk | fly | pos
        static void ParseMove(Statement s, string v)
        {
            string[] tok = v.Split(',');
            if (tok.Length < 4) { Debug.LogError("move: not enough parameters"); return; }

            static (float, float) Range(string str)
        {
            if (str.Contains(".."))
            {
                var p = str.Split("..");
                return (float.Parse(p[0]), float.Parse(p[1]));
            }
            float v = float.Parse(str);
            return (v, v);
        }

            string mode = tok[0].Trim().ToLower();

            if (mode == "walk" && tok.Length == 4)
            {
                var (xmin, xmax) = Range(tok[1]);
                var (zmin, zmax) = Range(tok[2]);
                s.wanderType = Statement.WanderType.Walk;
                s.areaMin = new Vector3(xmin, 0f, zmin);
                s.areaMax = new Vector3(xmax, 0f, zmax);
                s.wanderHz = float.Parse(tok[3]);
            }
            else if (mode == "fly" && tok.Length == 5)
            {
                var (xmin, xmax) = Range(tok[1]);
                var (ymin, ymax) = Range(tok[2]);
                var (zmin, zmax) = Range(tok[3]);
                s.wanderType = Statement.WanderType.Fly;
                s.areaMin = new Vector3(xmin, ymin, zmin);
                s.areaMax = new Vector3(xmax, ymax, zmax);
                s.wanderHz = float.Parse(tok[4]);
            }
            else if (mode == "pos" && tok.Length == 4)
            {
                var (xmin, xmax) = Range(tok[1]);
                var (ymin, ymax) = Range(tok[2]);
                var (zmin, zmax) = Range(tok[3]);
                s.wanderType = Statement.WanderType.Fixed;
                s.areaMin = new Vector3(xmin, ymin, zmin);
                s.areaMax = new Vector3(xmax, ymax, zmax);
            }
            else Debug.LogError($"move: bad syntax '{v}'");
        }
    }
}
