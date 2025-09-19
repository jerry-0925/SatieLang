using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Satie
{
    public sealed class Statement
    {
        public string kind;
        public string clip;
        public int    count = 1;
        public RangeOrValue starts_at = RangeOrValue.Zero;
        public RangeOrValue duration = RangeOrValue.Null;
        public RangeOrValue every = RangeOrValue.Zero;
        public RangeOrValue volume = new(1f);
        public RangeOrValue pitch = new(1f);
        public bool overlap = false;
        public RangeOrValue fade_in = RangeOrValue.Null;
        public RangeOrValue fade_out = RangeOrValue.Null;

        public enum WanderType { None, Walk, Fly, Fixed }
        public WanderType wanderType = WanderType.None;
        public Vector3 areaMin, areaMax;
        public RangeOrValue wanderHz = new(0.3f);

        public List<string> visual = new();

        public InterpolationData volumeInterpolation;
        public InterpolationData pitchInterpolation;
    }

    public readonly struct RangeOrValue
    {
        public readonly float min, max;
        public readonly bool  isRange, isSet;

        public static readonly RangeOrValue Zero = new(0f);
        public static readonly RangeOrValue Null = default;

        public RangeOrValue(float v) { min = max = v; isRange = false; isSet = true; }
        public RangeOrValue(float a, float b) { min = a; max = b; isRange = true;  isSet = true; }

        public float Sample() => !isSet ? 0f : isRange ? Random.Range(min, max) : min;

        public static RangeOrValue Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Null;
            if (s.Contains("to"))
            {
                var p = s.Split(new[] { "to" }, System.StringSplitOptions.None);
                return new(float.Parse(p[0]), float.Parse(p[1]));
            }
            return new(float.Parse(s));
        }

        public RangeOrValue Mul(float k) =>
            !isSet ? this : isRange ? new(min * k, max * k) : new(min * k);
    }

    // parser
    public static class SatieParser
    {
        static readonly Regex StmtRx = new(
            @"^(?:(?<count>\d+)\s*\*\s*)?(?<kind>loop|oneshot)\s+""(?<clip>.+?)""\s*(?:every\s+(?<e1>-?\d+\.?\d*)to(?<e2>-?\d+\.?\d*))?\s*:\s*\r?\n" +
            @"(?<block>(?:[ \t]+.*\r?\n?)*)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // pattern to recognise the start of a statement line, with optional count prefix
        static readonly Regex StmtStartRx = new(
            @"^(?:\d+\s*\*\s*)?(?:loop|oneshot)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex PropRx = new(
            @"^[ \t]*(?<key>\w+)\s*=\s*(?<val>[^\r\n#]+)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        sealed class GroupCtx
        {
            public readonly Dictionary<string,string> props = new();
            public readonly List<Statement> children = new();
            public int indent;
        }

        // Parse
        public static List<Statement> Parse(string script)
        {
            var outList = new List<Statement>();
            var lines   = script.Replace("\r\n", "\n").Split('\n');

            GroupCtx grp = null;

            for (int i = 0; i < lines.Length; ++i)
            {
                string raw  = lines[i];
                if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#"))
                    continue;

                int    indent = CountIndent(raw);
                string body   = raw.TrimStart();

                //  close grp?
                if (grp != null &&
                    indent == grp.indent &&
                    (StmtStartRx.IsMatch(body) ||
                     body.StartsWith("group ",  true, null) ||
                     body.StartsWith("endgroup",true, null)))
                {
                    FlushGroup(outList, grp);
                    grp = null;
                }
                if (grp != null && body.StartsWith("endgroup", true, null))
                    continue; // don't treat "endgroup" as a statement

                // open group
                if (body.StartsWith("group ", true, null) && body.TrimEnd().EndsWith(":"))
                {
                    grp = new GroupCtx { indent = indent };
                    continue;
                }

                // statement
                if (StmtStartRx.IsMatch(body))
                {
                    int stmtIndent = indent;
                    var sb = new StringBuilder();
                    sb.AppendLine(body);

                    int j = i + 1;
                    while (j < lines.Length && CountIndent(lines[j]) > stmtIndent)
                    {
                        sb.AppendLine(lines[j]);
                        ++j;
                    }
                    i = j - 1;

                    var st = ParseSingle(sb.ToString());
                    if (grp != null) grp.children.Add(st); else outList.Add(st);
                    continue;
                }

                //  property
                if (grp != null && PropRx.IsMatch(body))
                {
                    var m = PropRx.Match(body);
                    string k = m.Groups["key"].Value.ToLower();
                    if (k is "move" or "visual")
                        Debug.LogWarning($"[Satie] '{k}' not allowed on a group – ignored.");
                    else
                        grp.props[k] = m.Groups["val"].Value.Trim();
                    continue;
                }

                Debug.LogWarning($"[Satie] Unrecognised line: '{body}'");
            }

            if (grp != null) FlushGroup(outList, grp);
            return outList;
        }

        //  PathFor
        public static string PathFor(string clip)
        {
            if (string.IsNullOrWhiteSpace(clip)) return string.Empty;
            string c = clip.Replace('\\','/').TrimStart('/');
            int dot = c.LastIndexOf('.');
            if (dot >= 0) c = c[..dot];
            if (!c.StartsWith("Audio/")) c = $"Audio/{c}";
            return c;
        }

        // helpers
        static Statement ParseSingle(string block)
        {
            var m = StmtRx.Match(block);
            var s = new Statement
            {
                kind = m.Groups["kind"].Value.ToLower(),
                clip = m.Groups["clip"].Value.Trim(),
                count = m.Groups["count"].Success ? int.Parse(m.Groups["count"].Value) : 1
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
                    case "volume":
                        if (v.Contains("interpolate") || v.Contains("goto") || v.Contains("gobetween"))
                            s.volumeInterpolation = InterpolationData.Parse(v);
                        else
                            s.volume = RangeOrValue.Parse(v);
                        break;
                    case "pitch":
                        if (v.Contains("interpolate") || v.Contains("goto") || v.Contains("gobetween"))
                            s.pitchInterpolation = InterpolationData.Parse(v);
                        else
                            s.pitch = RangeOrValue.Parse(v);
                        break;
                    case "starts_at": s.starts_at = RangeOrValue.Parse(v); break;
                    case "duration": s.duration = RangeOrValue.Parse(v); break;
                    case "fade_in": s.fade_in = RangeOrValue.Parse(v); break;
                    case "fade_out": s.fade_out = RangeOrValue.Parse(v); break;
                    case "every": s.every = RangeOrValue.Parse(v); break;
                    case "overlap": s.overlap = v.ToLower().StartsWith("t"); break;
                    case "visual": ParseVisual(s, v); break;
                    case "move": ParseMove(s,v); break;
                }
            }
            return s;
        }

        static void FlushGroup(List<Statement> dst, GroupCtx g)
        {
            bool hasVol = g.props.TryGetValue("volume", out string vRaw);
            bool hasPitch = g.props.TryGetValue("pitch",  out string pRaw);

            InterpolationData groupVolInterp = null;
            InterpolationData groupPitchInterp = null;
            RangeOrValue gVolRange = new RangeOrValue(1f);
            RangeOrValue gPitchRange = new RangeOrValue(1f);

            if (hasVol)
            {
                if (vRaw.Contains("interpolate") || vRaw.Contains("goto") || vRaw.Contains("gobetween"))
                    groupVolInterp = InterpolationData.Parse(vRaw);
                else
                    gVolRange = RangeOrValue.Parse(vRaw);
            }

            if (hasPitch)
            {
                if (pRaw.Contains("interpolate") || pRaw.Contains("goto") || pRaw.Contains("gobetween"))
                    groupPitchInterp = InterpolationData.Parse(pRaw);
                else
                    gPitchRange = RangeOrValue.Parse(pRaw);
            }

            foreach (var s in g.children)
            {
                // Handle interpolations from group
                if (groupVolInterp != null && s.volumeInterpolation == null)
                    s.volumeInterpolation = groupVolInterp;
                if (groupPitchInterp != null && s.pitchInterpolation == null)
                    s.pitchInterpolation = groupPitchInterp;

                // Volume and pitch multiply with group values
                // Sample per statement so each gets its own random value if group has a range
                float gVol = gVolRange.Sample();
                float gPitch = gPitchRange.Sample();

                if (hasVol && groupVolInterp == null)
                    s.volume = s.volume.isSet ? s.volume.Mul(gVol) : new RangeOrValue(gVol);
                if (hasPitch && groupPitchInterp == null)
                    s.pitch = s.pitch.isSet  ? s.pitch .Mul(gPitch) : new RangeOrValue(gPitch);

                foreach (var kv in g.props)
                {
                    switch (kv.Key)
                    {
                        case "volume":
                        case "pitch": break;   // done above
                        case "starts_at" when !s.starts_at.isSet: s.starts_at = RangeOrValue.Parse(kv.Value); break;
                        case "duration" when !s.duration.isSet: s.duration = RangeOrValue.Parse(kv.Value); break;
                        case "fade_in" when !s.fade_in.isSet: s.fade_in = RangeOrValue.Parse(kv.Value); break;
                        case "fade_out" when !s.fade_out.isSet: s.fade_out = RangeOrValue.Parse(kv.Value); break;
                        case "every" when !s.every.isSet: s.every = RangeOrValue.Parse(kv.Value); break;
                        case "overlap": s.overlap = kv.Value.ToLower().StartsWith("t"); break;
                    }
                }
                dst.Add(s);
            }
        }

        static int CountIndent(string line)
        {
            int n = 0; while (n < line.Length && (line[n]==' ' || line[n]=='\t')) ++n; return n;
        }

        static void ParseMove(Statement s,string v)
        {
            string[] t = v.Split(',');
            if (t.Length < 4) { Debug.LogError("move: not enough parameters"); return; }

            static (float,float) R(string str)
            {
                if (str.Contains("to")) { var p=str.Split(new[] { "to" }, System.StringSplitOptions.None); return (float.Parse(p[0]),float.Parse(p[1])); }
                float f=float.Parse(str); return (f,f);
            }

            string mode=t[0].Trim().ToLower();
            if (mode=="walk" && t.Length==4)
            {
                var (xmin,xmax)=R(t[1]); var (zmin,zmax)=R(t[2]);
                s.wanderType=Statement.WanderType.Walk;
                s.areaMin=new Vector3(xmin,0f,zmin); s.areaMax=new Vector3(xmax,0f,zmax);
                s.wanderHz=RangeOrValue.Parse(t[3]);
            }
            else if (mode=="fly" && t.Length==5)
            {
                var (xmin,xmax)=R(t[1]); var (ymin,ymax)=R(t[2]); var (zmin,zmax)=R(t[3]);
                s.wanderType=Statement.WanderType.Fly;
                s.areaMin=new Vector3(xmin,ymin,zmin); s.areaMax=new Vector3(xmax,ymax,zmax);
                s.wanderHz=RangeOrValue.Parse(t[4]);
            }
            else if (mode=="pos" && t.Length==4)
            {
                var (xmin,xmax)=R(t[1]); var (ymin,ymax)=R(t[2]); var (zmin,zmax)=R(t[3]);
                s.wanderType=Statement.WanderType.Fixed;
                s.areaMin=new Vector3(xmin,ymin,zmin); s.areaMax=new Vector3(xmax,ymax,zmax);
            }
            else Debug.LogError($"move: bad syntax '{v}'");
        }

        static void ParseVisual(Statement s, string v)
        {
            v = v.Trim();
            if (string.IsNullOrWhiteSpace(v)) return;

            // Split by "and" to support multiple visuals
            string[] parts = v.Split(new[] { " and " }, System.StringSplitOptions.RemoveEmptyEntries);
            
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                
                // Check for object "path" syntax
                if (trimmed.StartsWith("object ", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the quoted path
                    var match = Regex.Match(trimmed, @"object\s+""(.+?)""");
                    if (match.Success)
                    {
                        s.visual.Add($"object:{match.Groups[1].Value}");
                    }
                    else
                    {
                        Debug.LogWarning($"[Satie] Invalid object syntax: '{trimmed}'");
                    }
                }
                else
                {
                    // It's a primitive type (trail, sphere, cube, etc.)
                    s.visual.Add(trimmed.ToLower());
                }
            }
        }
    }
}
