using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 开场视频：进入游戏后立刻在窗口内全屏播放 StreamingAssets 里的视频，播完自动销毁并回到游戏。
/// - 资源放 StreamingAssets/开头视屏/ 下（原样字节，不参与 Asset 导入，也避开 VideoClip 检查器的问题）
/// - 每次启动都播
/// - 找不到文件 / 解码失败 / 准备超时 / 播放超时 都直接跳过，不黑屏、不卡死
/// </summary>
public static class 开场视频
{
    /// <summary>播放期间为 true，世界点击入口用它屏蔽误操作。</summary>
    public static bool 播放中 { get; private set; }

    internal static void 设为播放中(bool v) { 播放中 = v; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void 启动()
    {
        try
        {
            string 文件 = 找视频文件();
            if (string.IsNullOrEmpty(文件))
            {
                播放中 = false;
                Debug.Log("[开场视频] StreamingAssets 下没有找到视频，直接进入游戏");
                return;
            }

            播放中 = true;
            GameObject go = new GameObject("[开场视频]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<开场视频播放器>().开始(文件);
            Debug.Log($"[开场视频] 开始播放：{文件}");
        }
        catch (Exception e)
        {
            播放中 = false;
            Debug.LogWarning($"[开场视频] 启动失败，已跳过开场：{e.Message}");
        }
    }

    static readonly string[] 候选目录 = { "开头视屏", "视频", "视屏", null };
    static readonly string[] 视频后缀 = { ".mp4", ".webm", ".mov", ".m4v", ".ogv" };

    static string 找视频文件()
    {
        string 根 = Application.streamingAssetsPath;
        if (string.IsNullOrEmpty(根) || !Directory.Exists(根)) return null;

        for (int d = 0; d < 候选目录.Length; d++)
        {
            string 目录 = 候选目录[d] == null ? 根 : Path.Combine(根, 候选目录[d]);
            if (!Directory.Exists(目录)) continue;

            string[] 文件s = Directory.GetFiles(目录);
            Array.Sort(文件s, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 文件s.Length; i++)
                for (int e = 0; e < 视频后缀.Length; e++)
                    if (文件s[i].EndsWith(视频后缀[e], StringComparison.OrdinalIgnoreCase))
                        return 文件s[i];
        }
        return null;
    }
}

/// <summary>运行时创建的全屏视频层：黑底 + RawImage + RenderTexture，播完整体销毁。</summary>
public class 开场视频播放器 : MonoBehaviour
{
    const float 准备超时秒 = 6f;
    const float 最短展示秒 = 5f;
    const float 最长等待秒 = 20f;

    VideoPlayer 播放器;
    RenderTexture 目标纹理;
    GameObject 界面根;
    AspectRatioFitter 比例;
    float 启动时刻;
    float 开始播放时间;
    float 准备截止时间;
    float 视频时长;
    bool 已开播;
    bool 视频播完;
    bool 已结束;
    bool 教程查找过;
    bool 有教程;
    bool 已打印等待日志;

    string 文件路径;
    bool 已用URI重试;
    bool 已打印准备状态;

    public void 开始(string 路径)
    {
        文件路径 = 路径;
        Debug.Log($"[开场视频] StreamingAssets={Application.streamingAssetsPath}");
        Debug.Log($"[开场视频] 视频文件={路径} 存在={File.Exists(路径)} 大小={(File.Exists(路径) ? new FileInfo(路径).Length : 0)} 字节");

        构建界面();

        播放器 = gameObject.AddComponent<VideoPlayer>();
        播放器.playOnAwake = false;
        播放器.isLooping = false;
        播放器.waitForFirstFrame = true;
        播放器.skipOnDrop = true;
        播放器.audioOutputMode = VideoAudioOutputMode.Direct;
        播放器.renderMode = VideoRenderMode.RenderTexture;
        播放器.targetTexture = 目标纹理;

        播放器.prepareCompleted += _ =>
         {
            if (已结束) return;
            已开播 = true;
            开始播放时间 = Time.unscaledTime;
            视频时长 = (float)播放器.length;
            Debug.Log($"[开场视频] Prepare 完成：{播放器.width}x{播放器.height} 时长={播放器.length:F2}s 帧数={播放器.frameCount} 音轨={播放器.audioTrackCount} url={播放器.url}");
            if (播放器.height > 0) 比例.aspectRatio = (float)播放器.width / 播放器.height;

            if (播放器.length <= 0.01)
            {
                Debug.LogWarning("[开场视频] 时长读到 0（文件可能损坏或编码不受支持），跳过开场");
                结束();
                return;
            }
            播放器.Play();
        };
        播放器.errorReceived += (_, msg) =>
        {
            Debug.LogWarning($"[开场视频] errorReceived 原文：{msg}（当前 url={播放器.url}）");
            if (尝试URI重试()) return;
            结束();
        };
        播放器.loopPointReached += _ => 定格等加载();

        启动时刻 = Time.unscaledTime;
        就绪最小帧 = Time.frameCount + 2;
        准备截止时间 = 启动时刻 + 准备超时秒;

        // 首选裸本地路径（Windows 对百分号编码的 file:// URI 兼容性不稳定）
        播放器.url = 路径.Replace('\\', '/');
        Debug.Log($"[开场视频] 第 1 次尝试 url={播放器.url}");
        播放器.Prepare();
    }

    /// <summary>准备失败时换一种 URL 形式再试一次：裸路径 ↔ file:// URI。</summary>
    bool 尝试URI重试()
    {
        if (已用URI重试) return false;
        已用URI重试 = true;
        try
        {
            播放器.Stop();
            string 备用 = 文件路径.Replace('\\', '/');
            播放器.url = 备用.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                ? 备用
                : new Uri(文件路径).AbsoluteUri;
            准备截止时间 = Time.unscaledTime + 准备超时秒;
            Debug.Log($"[开场视频] 第 2 次尝试 url={播放器.url}");
            播放器.Prepare();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[开场视频] URL 重试失败：{e.Message}");
            return false;
        }
    }

    void Update()
    {
        if (已结束) return;

        float now = Time.unscaledTime;

        // 总超时：加载卡死/事件没回调时强制进入游戏，不能永远停在视频上
        if (now - 启动时刻 > 最长等待秒)
        {
            Debug.LogWarning($"[开场视频] 等待超过 {最长等待秒:F0} 秒仍未就绪，强制进入游戏");
            结束();
            return;
        }

        if (!已开播)
        {
            float 等待 = now - 启动时刻;
            if (!已打印准备状态 && 等待 > 1f)
            {
                已打印准备状态 = true;
                Debug.Log($"[开场视频] Prepare 尚未完成（已等 {等待:F1}s），url={播放器.url}");
            }
            if (now > 准备截止时间)
            {
                Debug.LogWarning($"[开场视频] Prepare 超时（已等 {等待:F1}s），url={播放器.url}");
                if (尝试URI重试()) return;
                结束();
            }
            return;
        }

        // loopPointReached 在个别机器上不回调：按视频时长兜底判定“播完”
        if (!视频播完 && 视频时长 > 0.01f && now - 开始播放时间 > 视频时长 + 0.5f)
            定格等加载();
        if (!视频播完) return;

        float 已展示 = now - 启动时刻;
        if (已展示 < Mathf.Max(最短展示秒, 视频时长))
        {
            if (!已打印等待日志)
            {
                已打印等待日志 = true;
                Debug.Log($"[开场视频] 视频已播完，等满最短展示时间 {Mathf.Max(最短展示秒, 视频时长):F1} 秒");
            }
            return;
        }

        if (!就绪())
        {
            if (!已打印等待日志)
            {
                已打印等待日志 = true;
                Debug.Log("[开场视频] 视频已播完，游戏尚未就绪，定格最后一帧继续等待");
            }
            return;
        }

        Debug.Log("[开场视频] 视频播完且游戏已就绪，进入游戏");
        结束();
    }

    /// <summary>视频播完 → 停住（定格最后一帧），等加载就绪后再撤掉遮罩。</summary>
    void 定格等加载()
    {
        if (视频播完) return;
        视频播完 = true;
        if (播放器 != null && 播放器.isPlaying) 播放器.Pause();
        Debug.Log("[开场视频] 视频播放完毕，定格最后一帧作为加载遮罩");
    }

    /// <summary>
    /// 用工程里已有的可靠状态判断“游戏真的可以操作了”：
    /// 1) SaveSystem 首次自动读档结束（珊瑚/鱼/掉落物、教程判定的前置条件）
    /// 2) 窗口尺寸与 RT 已应用（DesktopWindowController.RTWidth/RTHeight）
    /// 3) 主相机已存在并输出到 RT
    /// 4) 初始教程已完成首屏判定
    /// 5) 至少经过 2 帧，保证 Start/布局执行完
    /// </summary>
    bool 就绪()
    {
        if (Time.frameCount < 就绪最小帧) return false;

        if (SaveSystem.Instance != null && !SaveSystem.首次读档完成) return false;

        if (!Application.isEditor &&
            (DesktopWindowController.RTWidth <= 0 || DesktopWindowController.RTHeight <= 0))
            return false;

        Camera cam = Camera.main;
        if (cam == null) return false;
        if (!Application.isEditor && cam.targetTexture == null) return false;

        if (!教程查找过)
        {
            教程查找过 = true;
            有教程 = FindObjectOfType<初始教程UI>() != null;
        }
        if (有教程 && !初始教程UI.已判定) return false;

        return true;
    }

    int 就绪最小帧;

    void 构建界面()
    {
        界面根 = new GameObject("[开场视频界面]");
        界面根.transform.SetParent(transform, false);

        Canvas canvas = 界面根.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        界面根.AddComponent<GraphicRaycaster>();
        CanvasScaler scaler = 界面根.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // 不透明黑底：既遮住游戏画面，也在这层吃掉所有 UI 点击
        GameObject 背景 = new GameObject("黑底", typeof(RectTransform), typeof(Image));
        背景.transform.SetParent(界面根.transform, false);
        RectTransform bgRt = 背景.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        Image bg = 背景.GetComponent<Image>();
        bg.color = Color.black;
        bg.raycastTarget = true;

        目标纹理 = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
        目标纹理.Create();

        GameObject 画面 = new GameObject("画面", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        画面.transform.SetParent(界面根.transform, false);
        RectTransform rt = 画面.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        RawImage raw = 画面.GetComponent<RawImage>();
        raw.texture = 目标纹理;
        raw.raycastTarget = false;
        比例 = 画面.GetComponent<AspectRatioFitter>();
        比例.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        比例.aspectRatio = 16f / 9f;
    }

    void 结束()
    {
        if (已结束) return;
        已结束 = true;
        开场视频.设为播放中(false);

        if (播放器 != null)
        {
            播放器.Stop();
            播放器.targetTexture = null;
        }
        if (界面根 != null) Destroy(界面根);
        if (目标纹理 != null)
        {
            目标纹理.Release();
            Destroy(目标纹理);
        }
        Debug.Log("[开场视频] 播放结束，已恢复游戏画面");
        Destroy(gameObject);
    }
}
