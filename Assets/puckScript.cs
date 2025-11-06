using UnityEngine;

public class puckScript : MonoBehaviour
{
    [Header("Collision Sound")]
    public AudioClip collisionClip;
    public AudioSource audioSource; // optional: assign in inspector
    [Range(0f, 2f)] public float volume = 1f;
    public bool randomizePitch = true;
    public float pitchMin = 0.9f;
    public float pitchMax = 1.1f;

    void Awake()
    {
        if (collisionClip != null && audioSource == null)
        {
            // create a local AudioSource if none assigned
            var go = new GameObject("PuckAudio");
            go.transform.SetParent(transform, false);
            audioSource = go.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f; // 3D positional by default
        }
    }

    void PlayCollisionSound()
    {
        if (collisionClip == null || audioSource == null) return;

        if (randomizePitch)
            audioSource.pitch = Random.Range(pitchMin, pitchMax);
        else
            audioSource.pitch = 1f;

        audioSource.PlayOneShot(collisionClip, volume);
    }

    // 2D physics
    void OnCollisionEnter2D(Collision2D collision)
    {
        PlayCollisionSound();
    }

    // 3D physics (in case puck uses 3D colliders)
    void OnCollisionEnter(Collision collision)
    {
        PlayCollisionSound();
    }
}
