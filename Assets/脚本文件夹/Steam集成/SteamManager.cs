// Steamworks 生命周期管理（基于 Steamworks.NET 官方 SteamManager 1.0.12 精简改造，
// 适配 Steamworks.NET 2025.164.1 / Steam SDK 1.64：新版无需 RequestCurrentStats，
// Steam 客户端在游戏进程启动前已自动同步成就与统计，Init 成功后可直接调用）。
// 游戏启动时自动创建，无需在场景里挂物体。
// 与官方版的行为差异：Steam 未运行 / 初始化失败时不退出游戏，
// 而是静默降级为纯本地模式（成就与云存档全部 no-op）。
// 上架前把项目根目录 steam_appid.txt 里的 480 换成正式 App ID 即可。
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

[DisallowMultipleComponent]
public class SteamManager : MonoBehaviour
{
#if !DISABLESTEAMWORKS
    static SteamManager s_instance;
    static bool s_everInitialized;

    /// <summary>SteamAPI 是否初始化成功（Steam 没开时为 false，所有 Steam 功能据此降级）。</summary>
    public static bool Initialized { get; private set; }

    SteamAPIWarningMessageHook_t m_warningHook;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoCreate()
    {
        if (s_instance != null) return;
        var go = new GameObject(nameof(SteamManager));
        go.AddComponent<SteamManager>(); // AddComponent 会立即同步执行 Awake，早于任何场景脚本
    }

    // 关闭 Domain Reload 时，进 Play Mode 前重置静态状态，避免编辑器连续运行残留
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_instance = null;
        s_everInitialized = false;
        Initialized = false;
    }

    void Awake()
    {
        if (s_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        s_instance = this;
        if (s_everInitialized) return;
        DontDestroyOnLoad(gameObject);

        if (!Packsize.Test() || !DllCheck.Test())
        {
            Debug.LogWarning("[Steam] Steamworks 二进制版本校验失败，本次运行使用纯本地模式");
            return;
        }

        // 传 Invalid 时 SteamAPI 自动读取游戏目录下的 steam_appid.txt（开发期用 480，上架后改文件即可）。
        // DllNotFound（机器没装 Steam/steam_api64.dll 缺失）不算致命错误，降级纯本地。
        try
        {
            // Steam 未运行/未通过 Steam 启动时，拉起 Steam 客户端并经它重启游戏（基本 DRM）。
            // 返回 true 表示正在重启，本次进程应退出。
            if (SteamAPI.RestartAppIfNecessary(AppId_t.Invalid))
            {
                Application.Quit();
                return;
            }
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogWarning($"[Steam] 找不到 steam_api 库，本次运行使用纯本地模式。{e.Message}");
            return;
        }

        if (!SteamAPI.Init())
        {
            Debug.LogWarning("[Steam] SteamAPI.Init() 失败（Steam 未登录/未授权该 AppID），本次运行使用纯本地模式");
            return;
        }

        s_everInitialized = true;
        Initialized = true;

        m_warningHook = new SteamAPIWarningMessageHook_t(OnDebugTextHook);
        SteamClient.SetWarningMessageHook(m_warningHook);

        Debug.Log("[Steam] SteamAPI 初始化成功（成就/云存档可用）");
    }

    [AOT.MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
    static void OnDebugTextHook(int severity, System.Text.StringBuilder text)
    {
        Debug.LogWarning($"[Steamworks] {text}");
    }

    void Update()
    {
        if (Initialized) SteamAPI.RunCallbacks();
    }

    void OnDestroy()
    {
        if (s_instance != this) return;
        s_instance = null;
        if (Initialized) SteamAPI.Shutdown();
        Initialized = false;
    }
#else
    // 非桌面平台：Steam 功能全部禁用
    public static bool Initialized => false;
#endif
}
