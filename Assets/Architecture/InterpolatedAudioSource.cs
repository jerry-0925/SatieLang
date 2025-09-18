using UnityEngine;
using Satie;

public class InterpolatedAudioSource : MonoBehaviour
{
    private AudioSource audioSource;
    private InterpolationManager interpolationManager;
    private float baseVolume = 1f;
    private float basePitch = 1f;
    private float childVolumeMultiplier = 1f;
    private float childPitchMultiplier = 1f;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        interpolationManager = new InterpolationManager();
    }

    public void SetupInterpolations(Statement stmt)
    {
        childVolumeMultiplier = stmt.volume.Sample();
        childPitchMultiplier = stmt.pitch.Sample();

        baseVolume = 1f;
        basePitch = 1f;

        interpolationManager.SetBaseValues(baseVolume, basePitch);

        if (stmt.volumeInterpolation != null)
        {
            interpolationManager.volumeInterp = stmt.volumeInterpolation.CreateCopy();
        }

        if (stmt.pitchInterpolation != null)
        {
            interpolationManager.pitchInterp = stmt.pitchInterpolation.CreateCopy();
        }

        audioSource.volume = childVolumeMultiplier;
        audioSource.pitch = childPitchMultiplier;
    }

    void Update()
    {
        if (audioSource && interpolationManager != null)
        {
            audioSource.volume = interpolationManager.GetVolume(Time.deltaTime) * childVolumeMultiplier;
            audioSource.pitch = interpolationManager.GetPitch(Time.deltaTime) * childPitchMultiplier;
        }
    }

    public void ResetInterpolations()
    {
        interpolationManager?.ResetInterpolations();
    }
}