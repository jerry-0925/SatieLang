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
        public RangeOrValue every = RangeOrValue.Null;
        public RangeOrValue volume = new(1f);
        public RangeOrValue pitch = new(1f);
        public bool overlap = false;
        public bool persistent = false;
        public bool mute = false;
        public bool solo = false;
        public RangeOrValue fade_in = RangeOrValue.Null;
        public RangeOrValue fade_out = RangeOrValue.Null;

        public enum WanderType { None, Walk, Fly, Fixed }
        public WanderType wanderType = WanderType.None;
        public Vector3 areaMin, areaMax;
        public RangeOrValue wanderHz = new(0.3f);

        public List<string> visual = new();

        public InterpolationData volumeInterpolation;
        public InterpolationData pitchInterpolation;
        public InterpolationData moveXMinInterpolation;
        public InterpolationData moveXMaxInterpolation;
        public InterpolationData moveYMinInterpolation;
        public InterpolationData moveYMaxInterpolation;
        public InterpolationData moveZMinInterpolation;
        public InterpolationData moveZMaxInterpolation;
        public InterpolationData moveSpeedInterpolation;

        // Color properties
        public Color? staticColor; // For static colors (white, #F54927, 255,100,50)
        public InterpolationData colorRInterpolation;
        public InterpolationData colorGInterpolation;
        public InterpolationData colorBInterpolation;
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
        // Syntax: loop clip or oneshot clip every 1to5
        static readonly Regex StmtRx = new(
            @"^(?:(?<count>\d+)\s*\*\s*)?(?<kind>loop|oneshot)\s+(?<clip>[^\s#]+)\s*(?:every\s+(?:(?<e1>-?\d+\.?\d*)to(?<e2>-?\d+\.?\d*)|(?<e>-?\d+\.?\d*)))?\s*(?:#.*)?\r?\n" +
            @"(?<block>(?:[ \t]+.*\r?\n?)*)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // pattern to recognise the start of a statement line, with optional count prefix
        static readonly Regex StmtStartRx = new(
            @"^(?:\d+\s*\*\s*)?(?:loop|oneshot)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Syntax: key value (space-separated)
        static readonly Regex PropRx = new(
            @"^[ \t]*(?<key>\w+)\s+(?<val>[^\r\n#]+)",
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
            bool inBlockComment = false;

            for (int i = 0; i < lines.Length; ++i)
            {
                string raw  = lines[i];
                string trimmed = raw.TrimStart();

                // Check for block comment start
                if (trimmed.StartsWith("comment", System.StringComparison.OrdinalIgnoreCase))
                {
                    inBlockComment = true;
                    continue;
                }

                // Check for block comment end
                if (trimmed.StartsWith("endcomment", System.StringComparison.OrdinalIgnoreCase))
                {
                    inBlockComment = false;
                    continue;
                }

                // Skip lines inside block comments
                if (inBlockComment)
                    continue;

                // Skip empty lines and single-line comments
                if (string.IsNullOrWhiteSpace(raw) || trimmed.StartsWith("#"))
                    continue;

                int    indent = CountIndent(raw);
                string body   = trimmed;

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
            else if (m.Groups["e"].Success)
                s.every = new RangeOrValue(float.Parse(m.Groups["e"].Value));

            // Strip block comments from the property block before parsing
            string propsBlock = StripBlockComments(m.Groups["block"].Value);

            foreach (Match p in PropRx.Matches(propsBlock))
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
                    case "persistent": s.persistent = v.ToLower().StartsWith("t"); break;
                    case "mute": s.mute = v.ToLower().StartsWith("t"); break;
                    case "solo": s.solo = v.ToLower().StartsWith("t"); break;
                    case "visual": ParseVisual(s, v); break;
                    case "move": ParseMove(s,v); break;
                    case "color": ParseColor(s, v); break;
                }
            }
            return s;
        }

        static void FlushGroup(List<Statement> dst, GroupCtx g)
        {
            bool hasVol = g.props.TryGetValue("volume", out string vRaw);
            bool hasPitch = g.props.TryGetValue("pitch",  out string pRaw);
            bool hasColor = g.props.TryGetValue("color", out string cRaw);

            InterpolationData groupVolInterp = null;
            InterpolationData groupPitchInterp = null;
            RangeOrValue gVolRange = new RangeOrValue(1f);
            RangeOrValue gPitchRange = new RangeOrValue(1f);

            // Group color data
            Color? groupStaticColor = null;
            InterpolationData groupColorRInterp = null;
            InterpolationData groupColorGInterp = null;
            InterpolationData groupColorBInterp = null;

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

            if (hasColor)
            {
                // Create a temp statement to parse color into, then extract the data
                var tempStmt = new Statement();
                ParseColor(tempStmt, cRaw);
                groupStaticColor = tempStmt.staticColor;
                groupColorRInterp = tempStmt.colorRInterpolation;
                groupColorGInterp = tempStmt.colorGInterpolation;
                groupColorBInterp = tempStmt.colorBInterpolation;
            }

            foreach (var s in g.children)
            {
                // Handle interpolations from group
                if (groupVolInterp != null && s.volumeInterpolation == null)
                    s.volumeInterpolation = groupVolInterp;
                if (groupPitchInterp != null && s.pitchInterpolation == null)
                    s.pitchInterpolation = groupPitchInterp;

                // Apply group color if statement doesn't have one
                if (hasColor)
                {
                    if (groupStaticColor.HasValue && !s.staticColor.HasValue)
                        s.staticColor = groupStaticColor;
                    if (groupColorRInterp != null && s.colorRInterpolation == null)
                        s.colorRInterpolation = groupColorRInterp;
                    if (groupColorGInterp != null && s.colorGInterpolation == null)
                        s.colorGInterpolation = groupColorGInterp;
                    if (groupColorBInterp != null && s.colorBInterpolation == null)
                        s.colorBInterpolation = groupColorBInterp;
                }

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
                        case "pitch":
                        case "color": break;   // done above
                        case "starts_at" when !s.starts_at.isSet: s.starts_at = RangeOrValue.Parse(kv.Value); break;
                        case "duration" when !s.duration.isSet: s.duration = RangeOrValue.Parse(kv.Value); break;
                        case "fade_in" when !s.fade_in.isSet: s.fade_in = RangeOrValue.Parse(kv.Value); break;
                        case "fade_out" when !s.fade_out.isSet: s.fade_out = RangeOrValue.Parse(kv.Value); break;
                        case "every" when !s.every.isSet: s.every = RangeOrValue.Parse(kv.Value); break;
                        case "overlap": s.overlap = kv.Value.ToLower().StartsWith("t"); break;
                        case "persistent": s.persistent = kv.Value.ToLower().StartsWith("t"); break;
                        case "mute": s.mute = kv.Value.ToLower().StartsWith("t"); break;
                        case "solo": s.solo = kv.Value.ToLower().StartsWith("t"); break;
                    }
                }
                dst.Add(s);
            }
        }

        static int CountIndent(string line)
        {
            int n = 0; while (n < line.Length && (line[n]==' ' || line[n]=='\t')) ++n; return n;
        }

        static string StripBlockComments(string text)
        {
            var lines = text.Split('\n');
            var result = new StringBuilder();
            bool inBlockComment = false;

            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();

                // Check for block comment start
                if (trimmed.StartsWith("comment", System.StringComparison.OrdinalIgnoreCase))
                {
                    inBlockComment = true;
                    continue;
                }

                // Check for block comment end
                if (trimmed.StartsWith("endcomment", System.StringComparison.OrdinalIgnoreCase))
                {
                    inBlockComment = false;
                    continue;
                }

                // Skip lines inside block comments
                if (inBlockComment)
                    continue;

                result.AppendLine(line);
            }

            return result.ToString();
        }

        static void ParseMove(Statement s, string v)
        {
            v = v.Trim();

            // Helper to parse range strings like "-5to5" or "10"
            static (float, float) ParseRange(string str)
            {
                str = str.Trim();
                if (str.Contains("to"))
                {
                    var parts = str.Split(new[] { "to" }, System.StringSplitOptions.None);
                    return (float.Parse(parts[0]), float.Parse(parts[1]));
                }
                float val = float.Parse(str);
                return (val, val);
            }

            // Default values
            float xMin = -5f, xMax = 5f;
            float yMin = -5f, yMax = 5f;
            float zMin = -5f, zMax = 5f;
            float speed = 1f;
            Statement.WanderType moveType = Statement.WanderType.None;

            // Check for old comma-separated syntax first (backwards compatibility)
            if (v.Contains(","))
            {
                string[] parts = v.Split(',');
                string mode = parts[0].Trim().ToLower();

                if (mode == "walk" && parts.Length >= 4)
                {
                    var (xmin, xmax) = ParseRange(parts[1]);
                    var (zmin, zmax) = ParseRange(parts[2]);
                    moveType = Statement.WanderType.Walk;
                    s.areaMin = new Vector3(xmin, 0f, zmin);
                    s.areaMax = new Vector3(xmax, 0f, zmax);
                    s.wanderHz = RangeOrValue.Parse(parts[3]);
                    s.wanderType = moveType;
                    return;
                }
                else if (mode == "fly" && parts.Length >= 5)
                {
                    var (xmin, xmax) = ParseRange(parts[1]);
                    var (ymin, ymax) = ParseRange(parts[2]);
                    var (zmin, zmax) = ParseRange(parts[3]);
                    moveType = Statement.WanderType.Fly;
                    s.areaMin = new Vector3(xmin, ymin, zmin);
                    s.areaMax = new Vector3(xmax, ymax, zmax);
                    s.wanderHz = RangeOrValue.Parse(parts[4]);
                    s.wanderType = moveType;
                    return;
                }
                else if (mode == "pos" && parts.Length >= 4)
                {
                    var (xmin, xmax) = ParseRange(parts[1]);
                    var (ymin, ymax) = ParseRange(parts[2]);
                    var (zmin, zmax) = ParseRange(parts[3]);
                    s.wanderType = Statement.WanderType.Fixed;
                    s.areaMin = new Vector3(xmin, ymin, zmin);
                    s.areaMax = new Vector3(xmax, ymax, zmax);
                    return;
                }
            }

            // New flexible syntax
            string input = v.ToLower();

            // Check for just "walk" or "fly"
            if (input == "walk")
            {
                s.wanderType = Statement.WanderType.Walk;
                s.areaMin = new Vector3(-5f, 0f, -5f);
                s.areaMax = new Vector3(5f, 0f, 5f);
                s.wanderHz = new RangeOrValue(1f);
                return;
            }
            else if (input == "fly")
            {
                s.wanderType = Statement.WanderType.Fly;
                s.areaMin = new Vector3(-5f, -5f, -5f);
                s.areaMax = new Vector3(5f, 5f, 5f);
                s.wanderHz = new RangeOrValue(1f);
                return;
            }

            // Parse axis specifications and speed
            bool hasX = false, hasY = false, hasZ = false;

            // Extract speed (can be simple value, range, or interpolation)
            // Pattern: "(?:at\s+)?speed\s+(.+?)(?:\s+(?:x|y|z)\s+|$)"
            var speedMatch = Regex.Match(v, @"(?:at\s+)?speed\s+(.+?)(?=\s+(?:x|y|z)\s+|$)", RegexOptions.IgnoreCase);
            if (speedMatch.Success)
            {
                string speedValue = speedMatch.Groups[1].Value.Trim();

                // Check if it's an interpolation
                var speedInterp = InterpolationData.Parse(speedValue);
                if (speedInterp != null)
                {
                    s.moveSpeedInterpolation = speedInterp;
                    speed = speedInterp.minValue; // Use min as starting value
                }
                else
                {
                    // Simple value or range
                    speed = float.Parse(speedValue.Replace("to", "."));
                }

                v = v.Substring(0, speedMatch.Index).Trim(); // Remove speed part
            }

            // Remove "and" but only when it's not inside parentheses (to preserve goto/gobetween syntax)
            // Simple approach: only replace " and " when followed by an axis keyword
            v = Regex.Replace(v, @"\s+and\s+(?=(?:x|y|z)\s+)", " ", RegexOptions.IgnoreCase);

            // Extract x range or interpolation
            // Pattern matches: "x -5to5" or "x gobetween(-1and-0.5 as linear in 2)to1"
            var xMatch = Regex.Match(v, @"x\s+(.+?)(?=\s+(?:y|z|speed)\s+|$)", RegexOptions.IgnoreCase);
            if (xMatch.Success)
            {
                string xValue = xMatch.Groups[1].Value.Trim();

                // Check if it contains "to" outside of parentheses (e.g., "gobetween(...)to5")
                // This means one side is interpolated and the other is a fixed value
                var rangeMatch = Regex.Match(xValue, @"^(.+?)\s*to\s*(.+?)$");
                if (rangeMatch.Success && !xValue.StartsWith("goto") && !xValue.StartsWith("gobetween"))
                {
                    // Split by "to" - could be "interp()toValue" or "valueToInterp()" or "valueToValue"
                    string leftPart = rangeMatch.Groups[1].Value.Trim();
                    string rightPart = rangeMatch.Groups[2].Value.Trim();

                    var leftInterp = InterpolationData.Parse(leftPart);
                    var rightInterp = InterpolationData.Parse(rightPart);

                    if (leftInterp != null)
                    {
                        s.moveXMinInterpolation = leftInterp;
                        xMin = leftInterp.minValue;
                    }
                    else
                    {
                        xMin = float.Parse(leftPart);
                    }

                    if (rightInterp != null)
                    {
                        s.moveXMaxInterpolation = rightInterp;
                        xMax = rightInterp.minValue;
                    }
                    else
                    {
                        xMax = float.Parse(rightPart);
                    }
                }
                else
                {
                    // Either a simple interpolation, or a simple range
                    var xInterp = InterpolationData.Parse(xValue);

                    if (xInterp != null)
                    {
                        // Single interpolation - apply to both min and max
                        s.moveXMinInterpolation = xInterp;
                        s.moveXMaxInterpolation = xInterp;
                        xMin = xInterp.minValue;
                        xMax = xInterp.maxValue;
                    }
                    else
                    {
                        try
                        {
                            (xMin, xMax) = ParseRange(xValue);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"[Satie] Failed to parse X value: '{xValue}' - {e.Message}");
                            throw;
                        }
                    }
                }
                hasX = true;
            }

            // Extract y range or interpolation
            var yMatch = Regex.Match(v, @"y\s+(.+?)(?=\s+(?:x|z|speed)\s+|$)", RegexOptions.IgnoreCase);
            if (yMatch.Success)
            {
                string yValue = yMatch.Groups[1].Value.Trim();

                var rangeMatch = Regex.Match(yValue, @"^(.+?)\s*to\s*(.+?)$");
                if (rangeMatch.Success && !yValue.StartsWith("goto") && !yValue.StartsWith("gobetween"))
                {
                    string leftPart = rangeMatch.Groups[1].Value.Trim();
                    string rightPart = rangeMatch.Groups[2].Value.Trim();

                    var leftInterp = InterpolationData.Parse(leftPart);
                    var rightInterp = InterpolationData.Parse(rightPart);

                    if (leftInterp != null)
                    {
                        s.moveYMinInterpolation = leftInterp;
                        yMin = leftInterp.minValue;
                    }
                    else
                    {
                        yMin = float.Parse(leftPart);
                    }

                    if (rightInterp != null)
                    {
                        s.moveYMaxInterpolation = rightInterp;
                        yMax = rightInterp.minValue;
                    }
                    else
                    {
                        yMax = float.Parse(rightPart);
                    }
                }
                else
                {
                    var yInterp = InterpolationData.Parse(yValue);

                    if (yInterp != null)
                    {
                        s.moveYMinInterpolation = yInterp;
                        s.moveYMaxInterpolation = yInterp;
                        yMin = yInterp.minValue;
                        yMax = yInterp.maxValue;
                    }
                    else
                    {
                        (yMin, yMax) = ParseRange(yValue);
                    }
                }
                hasY = true;
            }

            // Extract z range or interpolation
            var zMatch = Regex.Match(v, @"z\s+(.+?)(?=\s+(?:x|y|speed)\s+|$)", RegexOptions.IgnoreCase);
            if (zMatch.Success)
            {
                string zValue = zMatch.Groups[1].Value.Trim();

                var rangeMatch = Regex.Match(zValue, @"^(.+?)\s*to\s*(.+?)$");
                if (rangeMatch.Success && !zValue.StartsWith("goto") && !zValue.StartsWith("gobetween"))
                {
                    string leftPart = rangeMatch.Groups[1].Value.Trim();
                    string rightPart = rangeMatch.Groups[2].Value.Trim();

                    var leftInterp = InterpolationData.Parse(leftPart);
                    var rightInterp = InterpolationData.Parse(rightPart);

                    if (leftInterp != null)
                    {
                        s.moveZMinInterpolation = leftInterp;
                        zMin = leftInterp.minValue;
                    }
                    else
                    {
                        zMin = float.Parse(leftPart);
                    }

                    if (rightInterp != null)
                    {
                        s.moveZMaxInterpolation = rightInterp;
                        zMax = rightInterp.minValue;
                    }
                    else
                    {
                        zMax = float.Parse(rightPart);
                    }
                }
                else
                {
                    var zInterp = InterpolationData.Parse(zValue);

                    if (zInterp != null)
                    {
                        s.moveZMinInterpolation = zInterp;
                        s.moveZMaxInterpolation = zInterp;
                        zMin = zInterp.minValue;
                        zMax = zInterp.maxValue;
                    }
                    else
                    {
                        (zMin, zMax) = ParseRange(zValue);
                    }
                }
                hasZ = true;
            }

            // Determine movement type based on which axes are specified
            if (hasX && hasY && hasZ)
            {
                // All three axes = fly
                moveType = Statement.WanderType.Fly;
            }
            else if (hasX && hasZ && !hasY)
            {
                // X and Z only = walk (Y locked to 0)
                moveType = Statement.WanderType.Walk;
                yMin = 0f;
                yMax = 0f;
            }
            else if ((hasX && hasY) || (hasY && hasZ))
            {
                // Two axes including Y = fly
                moveType = Statement.WanderType.Fly;
                // Fill in defaults for unspecified axis
                if (!hasX) { xMin = -5f; xMax = 5f; }
                if (!hasY) { yMin = -5f; yMax = 5f; }
                if (!hasZ) { zMin = -5f; zMax = 5f; }
            }
            else if (hasX || hasZ)
            {
                // Only X or only Z = walk (Y locked to 0, other horizontal axis locked to 0)
                moveType = Statement.WanderType.Walk;
                yMin = 0f;
                yMax = 0f;
                if (!hasX) { xMin = 0f; xMax = 0f; }
                if (!hasZ) { zMin = 0f; zMax = 0f; }
            }
            else if (hasY)
            {
                // Only Y = fly with X and Z at 0
                moveType = Statement.WanderType.Fly;
                xMin = xMax = 0f;
                zMin = zMax = 0f;
            }

            if (moveType != Statement.WanderType.None)
            {
                s.wanderType = moveType;
                s.areaMin = new Vector3(xMin, yMin, zMin);
                s.areaMax = new Vector3(xMax, yMax, zMax);
                s.wanderHz = new RangeOrValue(speed);
            }
            else
            {
                Debug.LogError($"[Satie] Invalid move syntax: '{v}'");
            }
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

        static void ParseColor(Statement s, string v)
        {
            v = v.Trim();
            if (string.IsNullOrWhiteSpace(v)) return;

            // Check for named channel syntax
            // Pattern: red 255 green gobetween(40to255 in 10) blue gobetween(0to100 in 10)
            // or: red 255 and green gobetween(40to255 in 10) and blue gobetween(0to100 in 10)
            if (v.Contains("red ") || v.Contains("green ") || v.Contains("blue "))
            {
                // Remove optional "and" separators
                string normalized = v.Replace(" and ", " ");

                // Parse named channels
                var redMatch = Regex.Match(normalized, @"red\s+(.+?)(?=\s+(?:green|blue)|$)", RegexOptions.IgnoreCase);
                var greenMatch = Regex.Match(normalized, @"green\s+(.+?)(?=\s+(?:red|blue)|$)", RegexOptions.IgnoreCase);
                var blueMatch = Regex.Match(normalized, @"blue\s+(.+?)(?=\s+(?:red|green)|$)", RegexOptions.IgnoreCase);

                if (redMatch.Success)
                {
                    string redValue = redMatch.Groups[1].Value.Trim();
                    ParseColorChannel(s, "red", redValue);
                }

                if (greenMatch.Success)
                {
                    string greenValue = greenMatch.Groups[1].Value.Trim();
                    ParseColorChannel(s, "green", greenValue);
                }

                if (blueMatch.Success)
                {
                    string blueValue = blueMatch.Groups[1].Value.Trim();
                    ParseColorChannel(s, "blue", blueValue);
                }

                return;
            }

            // Check for gobetween interpolation between two hex colors
            // Pattern: gobetween(#000000to#FFFFFF as inquad in 5)
            var hexGoBetweenPattern = @"gobetween\s*\(\s*#([0-9A-Fa-f]{6})to#([0-9A-Fa-f]{6})\s*(?:as\s+(?<ease>\w+))?\s+in\s+(?<dur>-?[\d.]+(?:to-?[\d.]+)?)\s*\)";
            var hexGoBetweenMatch = Regex.Match(v, hexGoBetweenPattern);

            if (hexGoBetweenMatch.Success)
            {
                // Parse start and end colors
                Color startColor = HexToColor(hexGoBetweenMatch.Groups[1].Value);
                Color endColor = HexToColor(hexGoBetweenMatch.Groups[2].Value);

                string easeName = hexGoBetweenMatch.Groups["ease"].Success ? hexGoBetweenMatch.Groups["ease"].Value : "linear";
                RangeOrValue duration = RangeOrValue.Parse(hexGoBetweenMatch.Groups["dur"].Value);

                // Create interpolation data for each color channel
                s.colorRInterpolation = new InterpolationData(
                    new RangeOrValue(startColor.r),
                    new RangeOrValue(endColor.r),
                    easeName,
                    duration,
                    1,
                    true,
                    InterpolationType.GoBetween);

                s.colorGInterpolation = new InterpolationData(
                    new RangeOrValue(startColor.g),
                    new RangeOrValue(endColor.g),
                    easeName,
                    duration,
                    1,
                    true,
                    InterpolationType.GoBetween);

                s.colorBInterpolation = new InterpolationData(
                    new RangeOrValue(startColor.b),
                    new RangeOrValue(endColor.b),
                    easeName,
                    duration,
                    1,
                    true,
                    InterpolationType.GoBetween);
                return;
            }

            // Check for gobetween with single channel (0,255 in 5)
            // Pattern: gobetween(0,255 in 5)
            var singleChannelPattern = @"gobetween\s*\(\s*(?<min>-?[\d.]+)\s*,\s*(?<max>-?[\d.]+)\s+in\s+(?<dur>-?[\d.]+(?:to-?[\d.]+)?)\s*\)";
            var singleChannelMatch = Regex.Match(v, singleChannelPattern);

            if (singleChannelMatch.Success)
            {
                float min = float.Parse(singleChannelMatch.Groups["min"].Value) / 255f;
                float max = float.Parse(singleChannelMatch.Groups["max"].Value) / 255f;
                RangeOrValue duration = RangeOrValue.Parse(singleChannelMatch.Groups["dur"].Value);

                // Apply to all channels (grayscale interpolation)
                s.colorRInterpolation = new InterpolationData(
                    new RangeOrValue(min),
                    new RangeOrValue(max),
                    "linear",
                    duration,
                    1,
                    true,
                    InterpolationType.GoBetween);

                s.colorGInterpolation = new InterpolationData(
                    new RangeOrValue(min),
                    new RangeOrValue(max),
                    "linear",
                    duration,
                    1,
                    true,
                    InterpolationType.GoBetween);

                s.colorBInterpolation = new InterpolationData(
                    new RangeOrValue(min),
                    new RangeOrValue(max),
                    "linear",
                    duration,
                    1,
                    true,
                    InterpolationType.GoBetween);
                return;
            }

            // Check for hex color (#F54927)
            if (v.StartsWith("#"))
            {
                if (v.Length == 7)
                {
                    s.staticColor = HexToColor(v.Substring(1));
                }
                else
                {
                    Debug.LogWarning($"[Satie] Invalid hex color format: '{v}'. Use #RRGGBB format.");
                }
                return;
            }

            // Check for RGB color (255,100,50)
            var rgbPattern = @"^(\d+)\s*,\s*(\d+)\s*,\s*(\d+)$";
            var rgbMatch = Regex.Match(v, rgbPattern);

            if (rgbMatch.Success)
            {
                float r = float.Parse(rgbMatch.Groups[1].Value) / 255f;
                float g = float.Parse(rgbMatch.Groups[2].Value) / 255f;
                float b = float.Parse(rgbMatch.Groups[3].Value) / 255f;
                s.staticColor = new Color(r, g, b, 1f);
                return;
            }

            // Check for named colors
            string colorName = v.ToLower();
            switch (colorName)
            {
                case "white": s.staticColor = Color.white; break;
                case "black": s.staticColor = Color.black; break;
                case "red": s.staticColor = Color.red; break;
                case "green": s.staticColor = Color.green; break;
                case "blue": s.staticColor = Color.blue; break;
                case "yellow": s.staticColor = Color.yellow; break;
                case "cyan": s.staticColor = Color.cyan; break;
                case "magenta": s.staticColor = Color.magenta; break;
                case "gray":
                case "grey": s.staticColor = Color.gray; break;
                default:
                    Debug.LogWarning($"[Satie] Unknown color: '{v}'");
                    break;
            }
        }

        static void ParseColorChannel(Statement s, string channelName, string value)
        {
            InterpolationData interp = null;

            // Try to parse as interpolation first
            if (value.Contains("gobetween") || value.Contains("goto") || value.Contains("interpolate"))
            {
                interp = InterpolationData.Parse(value);

                if (interp != null)
                {
                    // Check if values need normalization (0-255 -> 0-1)
                    float minCheck = interp.minRange.isRange ?
                        Mathf.Max(interp.minRange.min, interp.minRange.max) :
                        interp.minRange.min;
                    float maxCheck = interp.maxRange.isRange ?
                        Mathf.Max(interp.maxRange.min, interp.maxRange.max) :
                        interp.maxRange.min;

                    if (minCheck > 1f || maxCheck > 1f)
                    {
                        // Normalize to 0-1 range
                        RangeOrValue normalizedMin = interp.minRange.isRange ?
                            new RangeOrValue(interp.minRange.min / 255f, interp.minRange.max / 255f) :
                            new RangeOrValue(interp.minRange.min / 255f);

                        RangeOrValue normalizedMax = interp.maxRange.isRange ?
                            new RangeOrValue(interp.maxRange.min / 255f, interp.maxRange.max / 255f) :
                            new RangeOrValue(interp.maxRange.min / 255f);

                        interp = new InterpolationData(
                            normalizedMin,
                            normalizedMax,
                            interp.easeName,
                            interp.durationRange,
                            interp.repeatCount,
                            interp.isForever,
                            interp.interpolationType);
                    }
                }
            }
            else
            {
                // Parse as static value
                if (float.TryParse(value, out float staticValue))
                {
                    // Normalize if needed
                    float normalizedValue = staticValue > 1f ? staticValue / 255f : staticValue;

                    // Create a constant "interpolation" using goto with same min/max
                    interp = new InterpolationData(
                        new RangeOrValue(normalizedValue),
                        new RangeOrValue(normalizedValue),
                        "linear",
                        new RangeOrValue(0.01f),
                        1,
                        false,
                        InterpolationType.Goto);
                }
            }

            // Assign to the appropriate channel
            if (interp != null)
            {
                switch (channelName.ToLower())
                {
                    case "red":
                        s.colorRInterpolation = interp;
                        break;
                    case "green":
                        s.colorGInterpolation = interp;
                        break;
                    case "blue":
                        s.colorBInterpolation = interp;
                        break;
                }
            }
        }

        static Color HexToColor(string hex)
        {
            if (hex.Length != 6)
            {
                Debug.LogWarning($"[Satie] Invalid hex color: {hex}");
                return Color.white;
            }

            try
            {
                int r = System.Convert.ToInt32(hex.Substring(0, 2), 16);
                int g = System.Convert.ToInt32(hex.Substring(2, 2), 16);
                int b = System.Convert.ToInt32(hex.Substring(4, 2), 16);
                return new Color(r / 255f, g / 255f, b / 255f, 1f);
            }
            catch
            {
                Debug.LogWarning($"[Satie] Failed to parse hex color: {hex}");
                return Color.white;
            }
        }
    }
}
