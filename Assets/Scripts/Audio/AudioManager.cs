using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Source")]
    public AudioSource seSource;
    public AudioSource bgmSource;
    public AudioSource walkSource;

    [Header("SE Clip")]
    public AudioClip SE_Cansel;
    public AudioClip SE_Clear;
    public AudioClip SE_Climb;
    public AudioClip SE_Illustration;
    public AudioClip SE_Land;
    public AudioClip SE_Over;
    public AudioClip SE_Pinch;
    public AudioClip SE_Press;
    public AudioClip SE_Release;
    public AudioClip SE_Rotate;
    public AudioClip SE_Select;
    public AudioClip SE_Slider;
    public AudioClip SE_Start;
    public AudioClip SE_Text;
    public AudioClip SE_Title;
    public AudioClip SE_Turn;
    public AudioClip SE_Tutorial;
    public AudioClip SE_Walk;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void PlaySE(AudioClip clip)
    {
        PlaySE(clip, 1f);
    }

    public void PlaySE(AudioClip clip, float volume)
    {
        if (clip == null)
        {
            Debug.LogWarning("[AudioManager] 再生する AudioClip が指定されていません。", this);
            return;
        }

        if (seSource == null)
        {
            Debug.LogWarning("[AudioManager] AudioSource が見つかりません。", this);
            return;
        }

        seSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    public void PlayWalkSE()
    {
        var source = walkSource != null ? walkSource : seSource;
        PlayClipUntilFinished(source, SE_Walk);
    }

    public void StopWalkSE()
    {
        var source = walkSource != null ? walkSource : seSource;
        StopClip(source, SE_Walk);
    }

    private void PlayClipUntilFinished(AudioSource source, AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogWarning("[AudioManager] 再生する AudioClip が指定されていません。", this);
            return;
        }

        if (source == null)
        {
            Debug.LogWarning("[AudioManager] AudioSource が見つかりません。", this);
            return;
        }

        if (source.isPlaying && source.clip == clip)
        {
            return;
        }

        source.clip = clip;
        source.loop = false;
        source.Play();
    }

    private void StopClip(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null)
        {
            return;
        }

        if (source.clip != clip)
        {
            return;
        }

        source.Stop();
        source.clip = null;
    }
}
