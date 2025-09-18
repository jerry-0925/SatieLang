using UnityEngine;

namespace Satie
{
    public class InterpolationData
    {
        public float minValue;
        public float maxValue;
        public EaseFunctions.EaseFunction easeFunction;
        public float duration;
        public int repeatCount;
        public bool isForever;
        public float currentTime;
        public int currentRepeat;
        public bool isActive;

        public InterpolationData(float min, float max, string easeName, float dur, int count = 1, bool forever = false)
        {
            minValue = min;
            maxValue = max;
            easeFunction = EaseFunctions.GetEaseFunction(easeName);
            duration = dur;
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
        }

        public static InterpolationData Parse(string interpolateStr)
        {
            if (string.IsNullOrWhiteSpace(interpolateStr)) return null;

            var pattern = @"interpolate\s*\(\s*(?<min>[\d.]+)\s*and\s*(?<max>[\d.]+)\s+as\s+(?<ease>\w+)\s+in\s+(?<dur>[\d.]+)(?:\s+for\s+(?<count>ever|\d+))?\s*\)";
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var match = regex.Match(interpolateStr);

            if (!match.Success) return null;

            float min = float.Parse(match.Groups["min"].Value);
            float max = float.Parse(match.Groups["max"].Value);
            string easeName = match.Groups["ease"].Value;
            float duration = float.Parse(match.Groups["dur"].Value);

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