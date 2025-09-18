using UnityEngine;
using Satie;

public class InterpolatedAudioSource : MonoBehaviour
{
    private AudioSource audioSource;
    private InterpolationManager interpolationManager;
    private float baseVolume = 1f;
    private float basePitch = 1f;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        interpolationManager = new InterpolationManager();
    }

    public void SetupInterpolations(Statement stmt)
    {
        baseVolume = stmt.volume.Sample();
        basePitch = stmt.pitch.Sample();

        interpolationManager.SetBaseValues(baseVolume, basePitch);

        if (stmt.volumeInterpolation != null)
        {
            interpolationManager.volumeInterp = stmt.volumeInterpolation;
        }

        if (stmt.pitchInterpolation != null)
        {
            interpolationManager.pitchInterp = stmt.pitchInterpolation;
        }

        audioSource.volume = baseVolume;
        audioSource.pitch = basePitch;
    }

    void Update()
    {
        if (audioSource && interpolationManager != null)
        {
            audioSource.volume = interpolationManager.GetVolume(Time.deltaTime);
            audioSource.pitch = interpolationManager.GetPitch(Time.deltaTime);
        }
    }

    public void ResetInterpolations()
    {
        interpolationManager?.ResetInterpolations();
    }
}