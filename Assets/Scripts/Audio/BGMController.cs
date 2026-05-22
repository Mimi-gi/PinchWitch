using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BGMController : MonoBehaviour
{
    public enum SceneNameMatchType
    {
        Exact,
        Prefix,
    }

    [Serializable]
    public class SceneBGMEntry
    {
        public string sceneName;
        public SceneNameMatchType matchType;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;

        public bool MatchesExact(string activeSceneName)
        {
            return matchType == SceneNameMatchType.Exact
                && SceneNameMatches(activeSceneName, sceneName);
        }

        public bool MatchesPrefix(string activeSceneName)
        {
            return matchType == SceneNameMatchType.Prefix
                && !string.IsNullOrEmpty(sceneName)
                && activeSceneName.StartsWith(sceneName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SceneNameMatches(string activeSceneName, string configuredSceneName)
        {
            return string.Equals(activeSceneName, configuredSceneName, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static BGMController Instance { get; private set; }

    [Header("Audio Source")]
    [SerializeField] private AudioSource bgmSource;

    [Header("Scene BGM")]
    [SerializeField] private SceneBGMEntry[] sceneBGMEntries = Array.Empty<SceneBGMEntry>();
    [SerializeField] private AudioClip defaultClip;
    [SerializeField, Range(0f, 1f)] private float defaultVolume = 1f;
    [SerializeField] private bool stopWhenSceneHasNoBGM;

    private bool _hasWarnedMissingSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        ResolveBGMSource();
        ConfigureBGMSource();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        PlayForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void PlayForCurrentScene()
    {
        PlayForScene(SceneManager.GetActiveScene().name);
    }

    public void PlayForScene(string sceneName)
    {
        if (TryGetSceneBGM(sceneName, out SceneBGMEntry entry))
        {
            PlayBGM(entry.clip, entry.volume);
            return;
        }

        if (defaultClip != null)
        {
            PlayBGM(defaultClip, defaultVolume);
            return;
        }

        if (stopWhenSceneHasNoBGM)
        {
            StopBGM();
        }
    }

    public void PlayBGM(AudioClip clip)
    {
        PlayBGM(clip, 1f);
    }

    public void PlayBGM(AudioClip clip, float volume)
    {
        if (clip == null)
        {
            StopBGM();
            return;
        }

        if (!EnsureBGMSource())
        {
            return;
        }

        bgmSource.loop = true;
        bgmSource.volume = Mathf.Clamp01(volume);

        if (bgmSource.clip == clip)
        {
            if (!bgmSource.isPlaying)
            {
                bgmSource.Play();
            }

            return;
        }

        bgmSource.Stop();
        bgmSource.clip = clip;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        if (!EnsureBGMSource())
        {
            return;
        }

        bgmSource.Stop();
        bgmSource.clip = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayForScene(scene.name);
    }

    private bool TryGetSceneBGM(string sceneName, out SceneBGMEntry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(sceneName) || sceneBGMEntries == null)
        {
            return false;
        }

        for (int i = 0; i < sceneBGMEntries.Length; i++)
        {
            SceneBGMEntry candidate = sceneBGMEntries[i];
            if (candidate != null && candidate.MatchesExact(sceneName))
            {
                entry = candidate;
                return true;
            }
        }

        for (int i = 0; i < sceneBGMEntries.Length; i++)
        {
            SceneBGMEntry candidate = sceneBGMEntries[i];
            if (candidate != null && candidate.MatchesPrefix(sceneName))
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }

    private bool EnsureBGMSource()
    {
        if (bgmSource == null)
        {
            ResolveBGMSource();
            ConfigureBGMSource();
        }

        if (bgmSource != null)
        {
            return true;
        }

        if (!_hasWarnedMissingSource)
        {
            _hasWarnedMissingSource = true;
            Debug.LogWarning("[BGMController] BGM 用 AudioSource が設定されていません。", this);
        }

        return false;
    }

    private void ResolveBGMSource()
    {
        if (bgmSource != null)
        {
            return;
        }

        AudioManager audioManager = GetComponent<AudioManager>();
        if (audioManager != null && audioManager.bgmSource != null)
        {
            bgmSource = audioManager.bgmSource;
            return;
        }

        Transform bgmTransform = transform.Find("BGM_AudioSource");
        if (bgmTransform != null && bgmTransform.TryGetComponent(out AudioSource source))
        {
            bgmSource = source;
        }
    }

    private void ConfigureBGMSource()
    {
        if (bgmSource == null)
        {
            return;
        }

        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
    }
}
