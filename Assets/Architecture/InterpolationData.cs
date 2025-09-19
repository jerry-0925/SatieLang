using UnityEngine;

namespace Satie
{
    public enum InterpolationType
    {
        Interpolate,  // Legacy interpolate function
        Goto,         // Goes from min to max and stays at max
        GoBetween     // Goes from min to max then back to min
    }

    public class InterpolationData
    {
        public RangeOrValue minRange;
        public RangeOrValue maxRange;
        public RangeOrValue durationRange;
        public string easeName;
        public float minValue;
        public float maxValue;
        public EaseFunctions.EaseFunction easeFunction;
        public float duration;
        public int repeatCount;
        public bool isForever;
        public float currentTime;
        public int currentRepeat;
        public bool isActive;
        public InterpolationType interpolationType;
        public bool isReturning;  // For GoBetween type, tracks if we're going back

        public InterpolationData(RangeOrValue min, RangeOrValue max, string easeName, RangeOrValue dur, int count = 1, bool forever = false, InterpolationType type = InterpolationType.Interpolate)
        {
            minRange = min;
            maxRange = max;
            durationRange = dur;
            this.easeName = easeName;
            interpolationType = type;

            // Sample initial values
            minValue = min.Sample();
            maxValue = max.Sample();
            duration = dur.Sample();

            easeFunction = EaseFunctions.GetEaseFunction(easeName);
            repeatCount = count;
            isForever = forever;
            currentTime = 0f;
            currentRepeat = 0;
            isActive = true;
            isReturning = false;
        }

        public float GetValue(float deltaTime)
        {
            if (!isActive)
            {
                // For goto, stay at max value when done
                if (interpolationType == InterpolationType.Goto)
                    return maxValue;
                return minValue;
            }

            currentTime += deltaTime;

            if (interpolationType == InterpolationType.GoBetween)
            {
                // GoBetween uses double the duration (go and return)
                float totalDuration = duration * 2;

                while (currentTime >= totalDuration)
                {
                    currentTime -= totalDuration;
                    if (!isForever)
                    {
                        currentRepeat++;
                        if (currentRepeat >= repeatCount)
                        {
                            isActive = false;
                            return minValue;  // End at starting position
                        }
                    }
                    // Re-sample values for next cycle
                    minValue = minRange.Sample();
                    maxValue = maxRange.Sample();
                    duration = durationRange.Sample();
                    totalDuration = duration * 2;
                }

                float t;
                if (currentTime < duration)
                {
                    // Going from min to max
                    t = currentTime / duration;
                    float easedT = Mathf.Clamp01(easeFunction(t));
                    return Mathf.Lerp(minValue, maxValue, easedT);
                }
                else
                {
                    // Returning from max to min
                    t = (currentTime - duration) / duration;
                    float easedT = Mathf.Clamp01(easeFunction(t));
                    return Mathf.Lerp(maxValue, minValue, easedT);
                }
            }
            else  // Goto or legacy Interpolate
            {
                while (currentTime >= duration)
                {
                    if (interpolationType == InterpolationType.Goto)
                    {
                        // For goto, stop at the end
                        isActive = false;
                        return maxValue;
                    }

                    // Legacy interpolate behavior
                    currentTime -= duration;
                    if (!isForever)
                    {
                        currentRepeat++;
                        if (currentRepeat >= repeatCount)
                        {
                            isActive = false;
                            return maxValue;
                        }
                    }
                    // Re-sample values for next cycle
                    minValue = minRange.Sample();
                    maxValue = maxRange.Sample();
                    duration = durationRange.Sample();
                }

                float t = currentTime / duration;
                float easedT = Mathf.Clamp01(easeFunction(t));
                return Mathf.Lerp(minValue, maxValue, easedT);
            }
        }

        public void Reset()
        {
            currentTime = 0f;
            currentRepeat = 0;
            isActive = true;
            // Re-sample values on reset
            minValue = minRange.Sample();
            maxValue = maxRange.Sample();
            duration = durationRange.Sample();
        }

        public InterpolationData CreateCopy()
        {
            return new InterpolationData(minRange, maxRange, easeName, durationRange, repeatCount, isForever, interpolationType);
        }

        public static InterpolationData Parse(string interpolateStr)
        {
            if (string.IsNullOrWhiteSpace(interpolateStr)) return null;

            // Check for goto function
            var gotoPattern = @"goto\s*\(\s*(?<min>[\d.]+(?:to[\d.]+)?)\s*and\s*(?<max>[\d.]+(?:to[\d.]+)?)\s+as\s+(?<ease>\w+)\s+in\s+(?<dur>[\d.]+(?:to[\d.]+)?)\s*\)";
            var gotoRegex = new System.Text.RegularExpressions.Regex(gotoPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var gotoMatch = gotoRegex.Match(interpolateStr);

            if (gotoMatch.Success)
            {
                RangeOrValue min = RangeOrValue.Parse(gotoMatch.Groups["min"].Value);
                RangeOrValue max = RangeOrValue.Parse(gotoMatch.Groups["max"].Value);
                string easeName = gotoMatch.Groups["ease"].Value;
                RangeOrValue duration = RangeOrValue.Parse(gotoMatch.Groups["dur"].Value);

                return new InterpolationData(min, max, easeName, duration, 1, false, InterpolationType.Goto);
            }

            // Check for gobetween function
            var goBetweenPattern = @"gobetween\s*\(\s*(?<min>[\d.]+(?:to[\d.]+)?)\s*and\s*(?<max>[\d.]+(?:to[\d.]+)?)\s+as\s+(?<ease>\w+)\s+in\s+(?<dur>[\d.]+(?:to[\d.]+)?)\s*(?:\s+for\s+(?<count>ever|\d+))?\s*\)";
            var goBetweenRegex = new System.Text.RegularExpressions.Regex(goBetweenPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var goBetweenMatch = goBetweenRegex.Match(interpolateStr);

            if (goBetweenMatch.Success)
            {
                RangeOrValue min = RangeOrValue.Parse(goBetweenMatch.Groups["min"].Value);
                RangeOrValue max = RangeOrValue.Parse(goBetweenMatch.Groups["max"].Value);
                string easeName = goBetweenMatch.Groups["ease"].Value;
                RangeOrValue duration = RangeOrValue.Parse(goBetweenMatch.Groups["dur"].Value);

                // Default to forever for gobetween unless a specific count is given
                bool forever = true;
                int count = 1;

                if (goBetweenMatch.Groups["count"].Success)
                {
                    string countStr = goBetweenMatch.Groups["count"].Value.ToLower();
                    if (countStr == "ever")
                    {
                        forever = true;
                    }
                    else
                    {
                        forever = false;
                        count = int.Parse(countStr);
                    }
                }

                return new InterpolationData(min, max, easeName, duration, count, forever, InterpolationType.GoBetween);
            }

            // Legacy interpolate function
            var pattern = @"interpolate\s*\(\s*(?<min>[\d.]+(?:to[\d.]+)?)\s*and\s*(?<max>[\d.]+(?:to[\d.]+)?)\s+as\s+(?<ease>\w+)\s+in\s+(?<dur>[\d.]+(?:to[\d.]+)?)\s*(?:\s+for\s+(?<count>ever|\d+))?\s*\)";
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var match = regex.Match(interpolateStr);

            if (!match.Success) return null;

            RangeOrValue minLegacy = RangeOrValue.Parse(match.Groups["min"].Value);
            RangeOrValue maxLegacy = RangeOrValue.Parse(match.Groups["max"].Value);
            string easeNameLegacy = match.Groups["ease"].Value;
            RangeOrValue durationLegacy = RangeOrValue.Parse(match.Groups["dur"].Value);

            bool foreverLegacy = false;
            int countLegacy = 1;

            if (match.Groups["count"].Success)
            {
                string countStr = match.Groups["count"].Value.ToLower();
                if (countStr == "ever")
                {
                    foreverLegacy = true;
                }
                else
                {
                    countLegacy = int.Parse(countStr);
                }
            }

            return new InterpolationData(minLegacy, maxLegacy, easeNameLegacy, durationLegacy, countLegacy, foreverLegacy, InterpolationType.Interpolate);
        }
    }

    public class InterpolationManager
    {
        public InterpolationData volumeInterp;
        public InterpolationData pitchInterp;

        private float baseVolume = 1f;
        private float basePitch = 1f;

        public void SetBaseValues(float volume, float pitch)
        {
            baseVolume = volume;
            basePitch = pitch;
        }

        public float GetVolume(float deltaTime)
        {
            if (volumeInterp != null)
            {
                // For goto type, we always get the value (even when inactive it returns the final value)
                if (volumeInterp.interpolationType == InterpolationType.Goto || volumeInterp.isActive)
                {
                    return volumeInterp.GetValue(deltaTime);
                }
            }
            return baseVolume;
        }

        public float GetPitch(float deltaTime)
        {
            if (pitchInterp != null)
            {
                // For goto type, we always get the value (even when inactive it returns the final value)
                if (pitchInterp.interpolationType == InterpolationType.Goto || pitchInterp.isActive)
                {
                    return pitchInterp.GetValue(deltaTime);
                }
            }
            return basePitch;
        }

        public void ResetInterpolations()
        {
            volumeInterp?.Reset();
            pitchInterp?.Reset();
        }
    }
}