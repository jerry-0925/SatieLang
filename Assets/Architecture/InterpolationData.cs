using UnityEngine;

namespace Satie
{
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

        public InterpolationData(RangeOrValue min, RangeOrValue max, string easeName, RangeOrValue dur, int count = 1, bool forever = false)
        {
            minRange = min;
            maxRange = max;
            durationRange = dur;
            this.easeName = easeName;

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
        }

        public float GetValue(float deltaTime)
        {
            if (!isActive) return minValue;

            currentTime += deltaTime;

            while (currentTime >= duration)
            {
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
            return new InterpolationData(minRange, maxRange, easeName, durationRange, repeatCount, isForever);
        }

        public static InterpolationData Parse(string interpolateStr)
        {
            if (string.IsNullOrWhiteSpace(interpolateStr)) return null;

            var pattern = @"interpolate\s*\(\s*(?<min>[\d.]+(?:to[\d.]+)?)\s*and\s*(?<max>[\d.]+(?:to[\d.]+)?)\s+as\s+(?<ease>\w+)\s+in\s+(?<dur>[\d.]+(?:to[\d.]+)?)\s*(?:\s+for\s+(?<count>ever|\d+))?\s*\)";
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var match = regex.Match(interpolateStr);

            if (!match.Success) return null;

            RangeOrValue min = RangeOrValue.Parse(match.Groups["min"].Value);
            RangeOrValue max = RangeOrValue.Parse(match.Groups["max"].Value);
            string easeName = match.Groups["ease"].Value;
            RangeOrValue duration = RangeOrValue.Parse(match.Groups["dur"].Value);

            bool forever = false;
            int count = 1;

            if (match.Groups["count"].Success)
            {
                string countStr = match.Groups["count"].Value.ToLower();
                if (countStr == "ever")
                {
                    forever = true;
                }
                else
                {
                    count = int.Parse(countStr);
                }
            }

            return new InterpolationData(min, max, easeName, duration, count, forever);
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
            if (volumeInterp != null && volumeInterp.isActive)
            {
                return volumeInterp.GetValue(deltaTime);
            }
            return baseVolume;
        }

        public float GetPitch(float deltaTime)
        {
            if (pitchInterp != null && pitchInterp.isActive)
            {
                return pitchInterp.GetValue(deltaTime);
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