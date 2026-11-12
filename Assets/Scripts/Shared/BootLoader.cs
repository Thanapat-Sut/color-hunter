using System.Collections;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// First scene of a build (Boot.unity, build index 0). Shows the game name + "Loading" while
/// it waits briefly for the tablet's "set-lang" and "set-grid" (LED layout, see LedGridLayout),
/// then loads the game scene asynchronously.
/// Without it the exe drops straight into the game scene and the language can flip on screen.
///
/// It listens on the same RMOT UDP port as the game scene's InviteToast UdpReceiver and
/// closes the socket just before the game scene activates so that receiver can bind it.
/// Other RMOT packets that arrive during boot (join-invite right after a relaunch, game-setup)
/// are kept and replayed into the game scene's UdpReceiver once it has started.
/// Scene + Build Settings are made by Tools > Boot > Create Boot Scene.
/// </summary>
public class BootLoader : MonoBehaviour
{
    [System.Serializable]
    struct GameLogo
    {
        public string scene;
        public Sprite logo;
        [Tooltip("Tints the glow behind the logo (alpha comes from glowImage).")]
        public Color accent;
    }

    [Tooltip("Logo per game scene name. Shown instead of titleText when the loaded scene has one.")]
    [SerializeField] private GameLogo[] logos;
    [SerializeField] private Image logoImage;
    [SerializeField] private Image glowImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text loadingText;
    [Tooltip("Bar fill. Its anchorMax.x follows load progress (0..1).")]
    [SerializeField] private RectTransform progressFill;

    [Tooltip("Empty = Application.productName in title case.")]
    [SerializeField] private string titleOverride;
    [SerializeField] private int rmotPort = 19790;
    [Tooltip("Max seconds to wait for set-lang + set-grid. The web resends them every second for 20 s.")]
    [SerializeField] private float langWaitSeconds = 1.5f;
    [Tooltip("Keep the loading screen at least this long so it does not just flash.")]
    [SerializeField] private float minShowSeconds = 1f;
    [SerializeField] private int gameSceneIndex = 1;

    UdpClient _udp;
    bool _langReceived;
    bool _gridReceived;
    readonly System.Collections.Generic.List<byte[]> _pending = new System.Collections.Generic.List<byte[]>();

    void Awake()
    {
        Debug.Log($"[Boot][timing] Boot Awake at {SinceProcessStart():0.00}s after exe start");
        string scene = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(gameSceneIndex));
        Sprite logo = null;
        Color accent = default;
        if (logos != null)
            foreach (var entry in logos)
                if (entry.scene == scene) { logo = entry.logo; accent = entry.accent; }

        if (glowImage != null)
        {
            glowImage.gameObject.SetActive(logo != null);
            glowImage.color = new Color(accent.r, accent.g, accent.b, glowImage.color.a);
        }

        if (logoImage != null)
        {
            logoImage.sprite = logo;
            logoImage.gameObject.SetActive(logo != null);
        }
        if (titleText != null) titleText.gameObject.SetActive(logo == null);
        if (titleText != null)
            titleText.text = string.IsNullOrEmpty(titleOverride)
                ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Application.productName)
                : titleOverride;
        SetProgress(0f);
    }

    void OnEnable()
    {
        GameLanguage.OnChanged += ApplyLanguage;
        ApplyLanguage(GameLanguage.Current);
    }

    void OnDisable() => GameLanguage.OnChanged -= ApplyLanguage;

    void OnDestroy() => CloseUdp();

    void ApplyLanguage(Language lang)
    {
        if (loadingText != null) loadingText.text = lang == Language.EN ? "Loading..." : "กำลังโหลด...";
    }

    // Seconds since the exe process started (Time.realtimeSinceStartup misses the engine's own init).
    static double SinceProcessStart()
    {
        try { return (System.DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds; }
        catch { return Time.realtimeSinceStartupAsDouble; }
    }

    static void LogGameSceneReady(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= LogGameSceneReady;
        Debug.Log($"[Boot][timing] game scene '{scene.name}' loaded at {SinceProcessStart():0.00}s after exe start");
    }

    IEnumerator Start()
    {
        float shownAt = Time.realtimeSinceStartup;
        Debug.Log($"[Boot][timing] boot screen at {SinceProcessStart():0.00}s after exe start");

        // Load the game scene while waiting for set-lang. Its objects do not Awake until activation,
        // so the language is still settled before the game scene runs.
        var op = SceneManager.LoadSceneAsync(gameSceneIndex);
        op.allowSceneActivation = false;

        OpenUdp();
        float waitUntil = Time.realtimeSinceStartup + langWaitSeconds;
        while (_udp != null && (!_langReceived || !_gridReceived) && Time.realtimeSinceStartup < waitUntil)
        {
            PollUdp();
            SetProgress(0.9f * Mathf.Clamp01(op.progress / 0.9f));
            yield return null;
        }
        Debug.Log($"[Boot] language {GameLanguage.Current} ({(_langReceived ? "from tablet" : "startup value")})");
        Debug.Log($"[Boot] grid {(LedGridLayout.HasLayout ? $"{LedGridLayout.Width}x{LedGridLayout.Height} from tablet" : "from each config")}");

        // Keep listening (and buffering) until the last moment before the game scene takes the port.
        while (op.progress < 0.9f || Time.realtimeSinceStartup - shownAt < minShowSeconds)
        {
            if (_udp != null) PollUdp();
            SetProgress(0.9f * Mathf.Clamp01(op.progress / 0.9f));
            yield return null;
        }
        if (_udp != null) PollUdp();
        CloseUdp();

        Debug.Log($"[Boot][timing] game scene ready to activate at {SinceProcessStart():0.00}s after exe start");
        SceneManager.sceneLoaded += LogGameSceneReady;
        if (_pending.Count > 0) RmotReplay.Schedule(_pending, rmotPort);
        LedGridLayout.ApplyToLoadedConfigs();   // the game scene's configs are loaded by now, its objects not awake yet
        SetProgress(1f);
        op.allowSceneActivation = true;
    }

    /// <summary>
    /// Survives the scene switch, waits until the game scene's objects have run Start (UdpReceiver
    /// bound, CommandEvent listeners hooked), then feeds the buffered packets to that UdpReceiver's
    /// onDataReceived exactly as if they had just arrived.
    /// </summary>
    class RmotReplay : MonoBehaviour
    {
        byte[][] _packets;
        int _port;

        public static void Schedule(System.Collections.Generic.List<byte[]> packets, int port)
        {
            var go = new GameObject("[BootRmotReplay]");
            DontDestroyOnLoad(go);
            var replay = go.AddComponent<RmotReplay>();
            replay._packets = packets.ToArray();
            replay._port = port;
        }

        IEnumerator Start()
        {
            // Activation happens a frame or more after Schedule; then give the new scene's Start a frame.
            int bootScene = SceneManager.GetActiveScene().buildIndex;
            while (SceneManager.GetActiveScene().buildIndex == bootScene) yield return null;
            yield return null;
            yield return null;

            var receivers = FindObjectsOfType<Fortal.UDP.UdpReceiver>(true);
            foreach (var receiver in receivers)
            {
                if (receiver.port != _port) continue;
                foreach (var data in _packets)
                {
                    Debug.Log($"[Boot] replay RMOT '{Encoding.UTF8.GetString(data, 4, data.Length - 4)}' to {receiver.name}");
                    receiver.onDataReceived?.Invoke(data);
                }
            }
            if (!System.Array.Exists(receivers, r => r.port == _port))
                Debug.LogWarning($"[Boot] no UdpReceiver on port {_port} in the game scene; {_packets.Length} RMOT packet(s) lost");
            Destroy(gameObject);
        }
    }

    void OpenUdp()
    {
        try
        {
            _udp = new UdpClient(rmotPort);
        }
        catch (SocketException ex)
        {
            Debug.LogWarning($"[Boot] cannot listen on UDP {rmotPort}, skip set-lang wait: {ex.Message}");
            _udp = null;
        }
    }

    void PollUdp()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            while (_udp.Available > 0)
            {
                byte[] data = _udp.Receive(ref ep);
                if (data.Length < 4 || Encoding.UTF8.GetString(data, 0, 4) != "RMOT") continue;

                string raw = Encoding.UTF8.GetString(data, 4, data.Length - 4);
                int space = raw.IndexOf(' ');
                string action = space >= 0 ? raw.Substring(0, space) : raw;
                string payload = space >= 0 ? raw.Substring(space + 1) : "";

                if (action == GameLanguageReceiver.Action)
                {
                    GameLanguageReceiver.ApplyFromTablet(payload);
                    _langReceived = true;
                }
                else if (action == LedGridLayout.Action)
                {
                    if (LedGridLayout.ApplyFromTablet(payload)) _gridReceived = true;
                }
                else
                {
                    Debug.Log($"[Boot] RMOT '{raw}' arrived during boot, kept for the game scene");
                    _pending.Add(data);
                }
            }
        }
        catch (SocketException ex)
        {
            Debug.LogWarning($"[Boot] UDP receive error: {ex.Message}");
            CloseUdp();
        }
    }

    void CloseUdp()
    {
        _udp?.Close();
        _udp = null;
    }

    void SetProgress(float t)
    {
        if (progressFill == null) return;
        var max = progressFill.anchorMax;
        max.x = Mathf.Clamp01(t);
        progressFill.anchorMax = max;
    }
}
