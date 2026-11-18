using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using Unity.Collections;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// 桌宠窗口控制器 —— 每像素真透明（UpdateLayeredWindow），无插件、无色键
///   1. 无边框 + 置顶
///   2. 窗口自动贴合塔大小（自动找塔主体，排除夜市背景）
///   3. 相机渲到 RT（预乘 alpha）→ GPU 通道重排为 BGRA → ULW 每帧上屏
///   4. 滚轮缩放 = 窗口等比放大缩小（打包版），窗口位置不跳
///   5. 场景任意位置可拖窗口（UI 上按下除外）
///   6. 时钟拱门钉在塔顶，Ctrl+方向键微调 + 自动存档
/// </summary>
public class DesktopWindowController : MonoBehaviour
{
    [Header("窗口透明")]
    public bool enableTransparent = true;

    [Header("拖动窗口")]
    [Tooltip("点住场景任意位置拖动窗口（UI 上按下除外）")]
    public bool dragWindowAnywhere = true;

    [Header("滚轮缩放")]
    public Camera zoomCamera;
    [Tooltip("打包版：滚轮缩放=整个桌宠窗口放大缩小，这里是倍数范围")]
    public float minWindowScale = 0.1f;
    public float maxWindowScale = 1.2f;
    float windowScale = 1f;

    // 旧场景序列化的是 0.5 / 1.5（1.5 时画面裁掉大半座塔，0.5 时缩得过小），
    // 现在场景里还存着 0.1 / 0.9（0.9 连 1:1 贴合档都到不了）。
    // 改字段初始值不会影响已保存的场景，所以按项目既有做法做一次“旧默认值→新默认值”迁移；
    // 用户手动改成别的值时仍然尊重用户值（不改 .unity，避免编辑器重载）。
    const float LegacyMinWindowScale = 0.5f;
    const float LegacyMinWindowScale2 = 0.1f;
    const float LegacyMaxWindowScale = 1.5f;
    const float LegacyMaxWindowScale2 = 0.9f;
    const float DefaultMinWindowScale = 0.7f;
    const float DefaultMaxWindowScale = 1.25f;

    /// <summary>1.0 = 1:1 贴合档（相机 orthoSize = 贴合包围盒半高），也是和编辑器对齐的档位。</summary>
    const float DefaultWindowScale = 1f;
    float EffectiveMinWindowScale =>
        (Mathf.Abs(minWindowScale - LegacyMinWindowScale) < 0.001f ||
         Mathf.Abs(minWindowScale - LegacyMinWindowScale2) < 0.001f)
            ? DefaultMinWindowScale : minWindowScale;
    float EffectiveMaxWindowScale =>
        (Mathf.Abs(maxWindowScale - LegacyMaxWindowScale) < 0.001f ||
         Mathf.Abs(maxWindowScale - LegacyMaxWindowScale2) < 0.001f)
            ? DefaultMaxWindowScale : maxWindowScale;

    /// <summary>
    /// 老存档的 windowScale 迁移：
    /// 0.50 是旧版默认档（UI 系数 0.556、相机 ortho×2），和编辑器的 1.0 档完全不是一个画面，
    /// 直接读进来就是用户说的“UI 小一圈、画面缩一圈”。这里一次性迁到 1.0；
    /// 其余值只做范围钳制，保持用户手动调过的档位。
    /// </summary>
    public static float MigrateLegacyWindowScale(float saved)
    {
        if (Mathf.Abs(saved - 0.5f) <= 0.06f) return DefaultWindowScale;
        return Mathf.Clamp(saved, DefaultMinWindowScale, DefaultMaxWindowScale);
    }

    /// <summary>
    /// UI 设计基准高度（像素）：取洞穴主体的实际像素高度（1:1 取景时约 996px）。
    /// 用 1080 时 UI 偏小（scaleFactor≈0.92），改成 1000 后又偏大（≈0.996），
    /// 1035 时构建版实测“有一点点小”，现在取 1005：
    /// UI 与 UiDesignHeight 成反比，1035 → 1005 即整体放大约 3.0%（不要一次调太多）。
    /// </summary>
    const float UiDesignHeight = 1005f;

    /// <summary>
    /// UI 基准比例（0.94 是构建版实测满意的大小）。
    ///
    /// 【必须乘 windowScale】否则滚轮缩放时相机 orthoSize 变了、UI 却纹丝不动：
    ///   世界像素/单位 = (texH/2) / orthoSize = (texH/2) / (baseOrtho/windowScale) ∝ windowScale
    ///   UI 像素/设计单位 = scaleFactor = texH/UiDesignHeight × 0.94 × windowScale ∝ windowScale
    /// 两者同比例变化，滚轮时 UI 与场景才会同步放大/缩小。
    /// 注意 texH 是窗口 RT 高度（窗口尺寸固定，不随滚轮变），所以 windowScale 是唯一的联动因子。
    /// </summary>
    const float UiBaseScaleRatio = 0.94f;

    /// <summary>窗口缩放真正生效后触发（参数=新的 windowScale）。一次性定位的 UI 靠它重新贴回场景物体。</summary>
    public static event Action<float> OnWindowScaleChanged;

    /// <summary>
    /// 当前滚轮档位（编辑器里恒为 1）。
    /// 世界→屏幕的“像素偏移”必须乘上它：场景在屏幕上的尺寸 ∝ windowScale，
    /// 固定像素偏移会让 UI 在缩小时离珊瑚越来越远、放大时贴得太近。
    /// </summary>
    public static float CurrentWindowScale { get; private set; } = 1f;

    [Header("窗口按钮")]
    public Button minimizeButton;

    [Header("画面居中")]
    public Transform centerTarget;
    public Vector2 centerOffset;

    [Header("窗口贴合场景")]
    public bool fitWindowToTarget = true;
    [Range(0.3f, 1f)] public float fitHeightRatio = 0.75f;
    public bool useManualFitSize = false;
    public Vector2 manualFitSize = new Vector2(7f, 10f);
    [Tooltip("自动贴合只统计塔主体周围这个半径内的 Sprite，排除远处大图")]
    public float fitRadius = 5f;
    [Tooltip("白名单：这些名字的对象（含其子物体）不受 fitRadius 距离限制，强制并入贴合包围盒")]
    public string[] fitAlwaysIncludeNames = { "神明" };

    [Header("神明显隐")]
    [Tooltip("神明物体；留空则运行时按名字自动查找")]
    public GameObject godRoot;
    public string godRootName = "神明";
    const string GodVisibleKey = "WindowGodVisible";
    bool godVisible = true;
    bool godStateLoaded;

    // 旧场景里序列化的是 0.438（历史默认）。改字段初始值不会影响已保存的场景，
    // 所以这里显式把“旧默认值”迁移为新默认值；用户手动改成其他值时仍然尊重用户值。
    // 0.62 是上一版默认（正方形改版前），也一起迁移到 0.75。
    const float LegacyFitHeightRatio = 0.438f;
    const float LegacyFitHeightRatio2 = 0.62f;
    const float DefaultFitHeightRatio = 0.75f;
    float EffectiveFitHeightRatio =>
        (Mathf.Abs(fitHeightRatio - LegacyFitHeightRatio) < 0.001f ||
         Mathf.Abs(fitHeightRatio - LegacyFitHeightRatio2) < 0.001f)
            ? DefaultFitHeightRatio
            : fitHeightRatio;

    [Header("窗口行为")]
    public bool topMost = true;
    const string TopMostKey = "WindowTopMost";

    // 窗口位置/缩放属于“显示偏好”，放 PlayerPrefs（每次拖动/缩放后 1 秒内落盘），
    // save.json 里的 windowState 继续保留，作为老存档的一次性迁移来源。
    const string WindowPosXKey = "WindowPosX";
    const string WindowPosYKey = "WindowPosY";
    const string WindowScalePrefKey = "WindowScale";

    [Header("点击诊断")]
    [Tooltip("每次鼠标按下打印屏幕坐标/客户区/UI 射线命中，用于定位“按钮点不到”；定位完可关掉")]
    public bool debugClickLog = true;

    [Header("时钟面板锁定塔身")]
    public bool lockClockToTower = true;
    [Tooltip("微调偏移（世界单位），Ctrl+方向键调完自动存档")]
    public Vector2 clockNudgeOffset = Vector2.zero;

    // 贴合状态（固定窗口尺寸，不再因 SetResolution 跳动）
    int fitW, fitH;
    float baseOrthoSize; // 贴合时的 orthographicSize，缩放时做基准

    // 滚轮防抖：累积滚动量，停转 150ms 后才真正改 orthographicSize
    float pendingZoomScroll;
    float zoomDebounceDeadline;
    const float ZoomStep = 0.05f; // 每次滚轮小步

    // Shift+Space 贴底缩小功能
    bool isMiniBottomMode = false;
    float savedWindowScale;
    Vector2 savedWindowPos;

    // 时钟锁定
    UIFollower clockFollower;
    Bounds lastFitBounds;
    bool hasFitBounds;
    const string ClockNudgeXKey = "ClockNudgeOffsetX";
    const string ClockNudgeYKey = "ClockNudgeOffsetY";

    // Canvas 缓存（避免热路径反复 FindObjectsOfType）
    List<Canvas> cachedCanvases = new List<Canvas>();
    const float CanvasCacheRefreshInterval = 5f;  // 每 5 秒刷新一次缓存（运行时新建的 UI 也能被捕获）
    float canvasCacheRefreshTimer;

    // ============ Win32 API ============
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    const uint SPI_GETWORKAREA = 0x0030;
    const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    static readonly IntPtr DPI_AWARE_PER_MONITOR_V2 = new IntPtr(-4);

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int  ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]  static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]  static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]  static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
    [DllImport("gdi32.dll")]  static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")]  static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

    const int  GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const uint WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    const uint WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000, WS_SYSMENU = 0x00080000;
    const uint WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020, SWP_NOACTIVATE = 0x0010;
    const int  SW_MINIMIZE = 6;
    const uint WM_NCLBUTTONDOWN = 0xA1;
    static readonly IntPtr HTCAPTION = new IntPtr(2);
    const uint ULW_ALPHA = 2;

    IntPtr hwnd;
    bool transparentReady;
    RenderTexture rt;
    RenderTexture rtPremul;      // GPU 预乘后的中间 RT（无深度，尺寸与 rt 一致），双缓冲 A
    RenderTexture rtPremul2;     // 双缓冲 B：A 在异步回读时写 B，避免读写同一张 RT
    Material premulMat;          // 预乘 + BGR 重排材质，创建一次后复用
    Texture2D readback;
    IntPtr memDC = IntPtr.Zero, dib = IntPtr.Zero, dibOld = IntPtr.Zero, dibBits = IntPtr.Zero;
    int texW, texH;
    byte[] bgra;                 // 正在上屏的一帧（BGRA）
    byte[] bgraPending;          // GPU 回读落点

    // 异步回读状态：GPU→CPU 的整帧回读是每帧最大的停顿来源（1200×1200×4≈5.8MB），
    // 改成 AsyncGPUReadback 后 CPU 不再等 GPU，代价是上屏比逻辑帧晚 1 帧。
    int premulPending = -1;      // 正在回读的缓冲下标，-1 = 空闲
    int premulWrite;             // 本帧写入的缓冲下标
    bool asyncReadbackEnabled;
    bool frameReady;

    // 性能计数：每 300 帧打一条，用户回传日志即可判断卡顿是否来自上屏
    int presentFrameCount;
    float presentAccumMs;
    float lastRepairTime;

    // 公开的 RT 尺寸（供其他脚本做 Screen→RT 坐标转换）
    public static int RTWidth { get; private set; }
    public static int RTHeight { get; private set; }

    /// <summary>
    /// 屏幕坐标（Input.mousePosition 所在空间，即窗口客户区像素）→ RenderTexture 像素。
    ///
    /// 这是全项目唯一的“鼠标 → RT”换算入口：鱼点击、珊瑚拖拽、UI 射线、
    /// 世界坐标悬停判定都必须调它，不要再各写一套（编辑器/打包版各写一份补丁，
    /// 迟早会有一处漏掉或多乘一次）。
    ///
    /// 透明窗口把画面渲进固定尺寸的 RT，而窗口被 SetWindowPos 改成另一个尺寸；
    /// 只有当 Screen 尺寸与 RT 尺寸不同才需要缩放，相同时原样返回（开销为零）。
    /// 编辑器里 RTWidth=0，退回相机像素尺寸，通常与 Screen 相同 → 等价于不换算。
    /// </summary>
    public static Vector2 ScreenToRenderTexturePoint(Vector2 screenPos)
    {
        float sw = Screen.width, sh = Screen.height;
        Camera cam = Camera.main;
        float rw = RTWidth > 0 ? RTWidth : (cam != null ? cam.pixelWidth : 0);
        float rh = RTHeight > 0 ? RTHeight : (cam != null ? cam.pixelHeight : 0);
        if (sw <= 0f || sh <= 0f || rw <= 0f || rh <= 0f) return screenPos;
        if (Mathf.Approximately(sw, rw) && Mathf.Approximately(sh, rh)) return screenPos;
        return new Vector2(screenPos.x * rw / sw, screenPos.y * rh / sh);
    }

    /// <summary>
    /// 屏幕坐标 → 相机世界坐标。2D 正交相机下 z 取近裁剪面即可（x/y 与 z 无关）。
    /// </summary>
    public static Vector3 ScreenToWorldPointCorrect(Camera cam, Vector2 screenPos)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return new Vector3(screenPos.x, screenPos.y, 0f);
        Vector2 rtPos = ScreenToRenderTexturePoint(screenPos);
        return cam.ScreenToWorldPoint(new Vector3(rtPos.x, rtPos.y, cam.nearClipPlane));
    }

    /// <summary>
    /// 鼠标是否点在场景里可点击的对象上（鱼/珊瑚）。
    ///
    /// 透明窗口版里“点场景任意位置拖动窗口”会立刻进入 Windows 模态拖动循环，
    /// Unity 这一帧及之后收不到输入 → 表现就是“点鱼没反应、窗口反而动了”。
    /// 编辑器里 HandleWindowDrag 是空操作，所以编辑器没有这个症状。
    /// </summary>
    bool IsPointerOverWorldInteractable()
    {
        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam == null) return false;

        Vector2 world = ScreenToWorldPointCorrect(cam, Input.mousePosition);
        Collider2D[] hits = Physics2D.OverlapPointAll(world);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D h = hits[i];
            if (h == null) continue;
            if (h.GetComponentInParent<鱼运动>() != null) return true;
            if (h.CompareTag("coral")) return true;
        }
        return false;
    }

    // ==================== DPI ====================
    void MakeProcessDpiAware()
    {
        if (SetProcessDpiAwarenessContext(DPI_AWARE_PER_MONITOR_V2) != IntPtr.Zero)
            Debug.Log("[桌面窗口] DPI = PerMonitorV2");
        else if (SetProcessDPIAware())
            Debug.Log("[桌面窗口] DPI = System Aware");
    }

    // ==================== Start ====================
    void Start()
    {
        // 从存档读取偏好（Inspector 里设了默认值，PlayerPrefs 有值时覆盖）
        if (PlayerPrefs.HasKey(TopMostKey))
            topMost = PlayerPrefs.GetInt(TopMostKey) == 1;

        // 神明开关：必须先按存档显隐，再算贴合包围盒（否则关掉后仍会被白名单撑大窗口）
        LoadGodState();

        // 窗口位置/缩放：在第一次摆窗口之前就按 PlayerPrefs 里的显示偏好恢复，
        // 这样启动时不会先闪到默认位置再跳回来。
        LoadWindowPrefs();

        Application.runInBackground = true;


#if !UNITY_EDITOR
        MakeProcessDpiAware();
#endif
        // 编辑器里也执行：以前编辑器走 CanvasScaler 自己的 ScaleWithScreenSize，
        // 构建版走 ConstantPixelSize + 本项目公式，两边 UI 大小天然对不上。
        // 现在两边同一模式、同一公式（windowScale 在编辑器里恒为 1），
        // 相同画面高度下 UI 才是像素级一致。
        FixCanvasScalers();
        CenterCameraOnTarget();
        FitWindowToTarget();

#if UNITY_EDITOR
        if (enableTransparent) Debug.Log("[桌面窗口] 编辑器里不执行，打包 Windows exe 后生效");
#else
        if (enableTransparent)
        {
            SetupWindowStylesEarly();
            StartCoroutine(DelayedSetup());
        }
#endif

        StartCoroutine(LockClockWhenReady());
        ValidateEventSystem();

#if !UNITY_EDITOR
        // 窗口位置/缩放不是每次都能等到 OnApplicationQuit（任务管理器/崩溃/强关都收不到），
        // 这里每秒比对一次，玩家拖完/缩放完 1 秒内就落盘到 PlayerPrefs。
        StartCoroutine(AutoPersistWindowState());
        if (!enableTransparent) StartCoroutine(RestoreWindowPositionWhenReady());
#endif

        if (minimizeButton != null)
        {
            minimizeButton.onClick = new Button.ButtonClickedEvent();
            minimizeButton.onClick.AddListener(MinimizeWindow);
        }
    }

    void FixCanvasScalers()
    {
        // 必须包含未激活的 Canvas（设置页等默认隐藏），否则它们的参考分辨率停留在 800x600，
        // 下次打开时会按 600 的设计高度算，UI 反而被放大。
        foreach (CanvasScaler s in FindObjectsOfType<CanvasScaler>(true))
        {
            // 只处理根 Canvas：嵌套 Canvas（设置页面、显示面板等）本身不参与 CanvasScaler 计算
            // （UGUI 的 CanvasScaler 对非根 Canvas 直接跳过），它们靠父 Canvas 的缩放继承，
            // 这里若强行改配置只会让 Inspector 看起来“被驱动了”而运行时毫无作用。
            var ownCanvas = s.GetComponent<Canvas>();
            if (ownCanvas != null && !ownCanvas.isRootCanvas) continue;

            // 必须和运行时驱动（EnsureCanvasesOnTop）用同一种模式：
            // ScaleWithScreenSize 会由 UGUI 自己在 Handle() 里算 scaleFactor，
            // 我们手写的值会被覆盖 → 滚轮缩放时 UI 看起来“被锁死”。
            // 这里先切成 ConstantPixelSize 并写入同一公式的初值，
            // 之后 EnsureCanvasesOnTop / ApplyWindowScale 只管更新这个值即可。
            s.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            float initSF = TargetScaleFactor();
            if (!Mathf.Approximately(s.scaleFactor, initSF)) s.scaleFactor = initSF;
        }
    }

    /// <summary>
    /// UI 的基准缩放系数：固定按高度匹配（等价于 CanvasScaler 的 MatchWidthOrHeight=1）。
    ///
    /// 不能用 min(宽比, 高比)：桌宠窗口又高又窄（如 453x730），按宽度算只有 0.24，
    /// 而场景是按高度装满窗口的（约 0.68），UI 会被额外压小 2~3 倍，缩在塔顶。
    /// 滚轮缩放时再额外乘 windowScale，保证 UI 与场景同步放大/缩小。
    /// </summary>
    float CanvasBaseScaleFactor(Vector2 referenceResolution)
    {
        // 统一按洞穴像素高度换算（不再看各 Canvas 自己序列化的参考分辨率，
        // 否则未激活的旧 Canvas 会停留在 800x600，算出来偏小）
        float designHeight = Mathf.Max(1f, UiDesignHeight);
        float pixelHeight = texH > 0 ? texH : Screen.height;
        return pixelHeight / designHeight;
    }

    /// <summary>
    /// 所有根 Canvas 唯一使用的 scaleFactor：按高度基准 × 0.94 × 当前滚轮档位。
    /// 缩放写入口分散在 EnsureCanvasesOnTop / ApplyWindowScale 两处，公式必须收在这里，
    /// 否则改一处漏一处就会再次出现“UI 不跟着滚”或“场景/UI 不同步”。
    /// </summary>
    float TargetScaleFactor()
    {
        return CanvasBaseScaleFactor(new Vector2(1920f, UiDesignHeight)) * UiBaseScaleRatio * windowScale;
    }

    void CenterCameraOnTarget()
    {
        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam == null || centerTarget == null) return;
        Vector3 p = cam.transform.position;
        cam.transform.position = new Vector3(
            centerTarget.position.x + centerOffset.x,
            centerTarget.position.y + centerOffset.y,
            p.z);
    }

    // ==================== 时钟锁定 ====================
    System.Collections.IEnumerator LockClockWhenReady()
    {
#if UNITY_EDITOR
        for (int i = 0; i < 70; i++) yield return null;
#else
        yield return new WaitForSeconds(1f); // 等分辨率管理器应用存档分辨率
#endif
        LockClockToTower();
    }

    Vector2 ClockTargetWorldOffset()
    {
        if (!hasFitBounds || centerTarget == null) return Vector2.zero;
        return new Vector2(lastFitBounds.center.x, lastFitBounds.center.y + lastFitBounds.extents.y)
             - (Vector2)centerTarget.position;
    }

    void LockClockToTower()
    {
        if (!lockClockToTower) return;
        if (centerTarget == null) return;

        LocalTimeManager mgr = FindObjectOfType<LocalTimeManager>();
        if (mgr == null) return;
        if (clockFollower != null) return;

        if (PlayerPrefs.HasKey(ClockNudgeXKey))
            clockNudgeOffset = new Vector2(
                PlayerPrefs.GetFloat(ClockNudgeXKey),
                PlayerPrefs.GetFloat(ClockNudgeYKey));

        clockFollower = mgr.GetComponent<UIFollower>();
        if (clockFollower == null) clockFollower = mgr.gameObject.AddComponent<UIFollower>();
        clockFollower.target = centerTarget;
        clockFollower.offset = Vector2.zero;
        clockFollower.worldOffset = ClockTargetWorldOffset() + clockNudgeOffset;
        Debug.Log($"[桌面窗口] 时钟钉塔顶: worldOffset={clockFollower.worldOffset}（Ctrl+方向键微调，Ctrl+0 归零）");
    }

    // ==================== 窗口贴合场景 ====================
    void FitWindowToTarget()
    {
        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam == null || !cam.orthographic || !fitWindowToTarget) return;

        Bounds b;
        if (useManualFitSize && manualFitSize.x > 0f && manualFitSize.y > 0f)
        {
            Vector3 c = centerTarget != null ? centerTarget.position : Vector3.zero;
            b = new Bounds(c, new Vector3(manualFitSize.x, manualFitSize.y, 1f));
        }
        else
        {
            // 只统计 SpriteRenderer（排除粒子/网格辅助物/隐形物体）
            Renderer[] sources = centerTarget != null
                ? centerTarget.GetComponentsInChildren<Renderer>()
                : FindObjectsOfType<Renderer>();

            List<Renderer> sprites = new List<Renderer>();
            foreach (Renderer r in sources)
            {
                if (r is ParticleSystemRenderer) continue;
                if (!(r is SpriteRenderer)) continue;
                if (r.bounds.size.x < 0.01f && r.bounds.size.y < 0.01f) continue;
                sprites.Add(r);
            }
            if (sprites.Count == 0) { Debug.LogWarning("[桌面窗口] 贴合跳过：没找到 SpriteRenderer"); return; }

            // 塔主体 = 最细最高的 Sprite（高度/宽度比值最大），且有最小尺寸排除装饰条
            Renderer towerBody = null;
            float thinnestTall = 0f;
            foreach (Renderer r in sprites)
            {
                if (r.bounds.size.x < 1.5f || r.bounds.size.y < 4.0f) continue;
                float aspect = r.bounds.size.x > 0.01f ? r.bounds.size.y / r.bounds.size.x : 0f;
                if (aspect > thinnestTall) { thinnestTall = aspect; towerBody = r; }
            }
            if (towerBody == null)
            {
                foreach (Renderer r in sprites)
                {
                    if (r.bounds.size.y < 3.0f) continue;
                    float aspect = r.bounds.size.x > 0.01f ? r.bounds.size.y / r.bounds.size.x : 0f;
                    if (aspect > thinnestTall) { thinnestTall = aspect; towerBody = r; }
                }
            }

            // 以塔主体为中心收集 fitRadius 内的 Sprite
            Vector3 towerC = towerBody.bounds.center;
            b = towerBody.bounds;
            int count = 1;
            int forced = 0;
            List<string> excluded = new List<string>();
            foreach (Renderer r in sprites)
            {
                if (r == towerBody) continue;
                if (IsForceIncluded(r))
                {
                    b.Encapsulate(r.bounds);
                    count++;
                    forced++;
                    continue;
                }
                if (fitRadius > 0f && Vector3.Distance(r.bounds.center, towerC) > fitRadius)
                {
                    if (excluded.Count < 12) excluded.Add($"{r.name}({Vector3.Distance(r.bounds.center, towerC):F1}m)");
                    continue;
                }
                b.Encapsulate(r.bounds);
                count++;
            }

            // 诊断：列出被 fitRadius 排除的远处 Sprite，便于确认某些背景/装饰是否被裁出窗口
            if (excluded.Count > 0)
                Debug.Log($"[桌面窗口] 贴合排除 {excluded.Count} 个远处 Sprite（>{fitRadius}m）: {string.Join(", ", excluded)}");

            // 兜底：塔身边没内容 → 退回全场景
            if (b.size.y < 0.5f)
            {
                Debug.LogWarning("[桌面窗口] 塔身边内容过少，退回全场景包围盒");
                b = sprites[0].bounds;
                foreach (Renderer r in sprites) b.Encapsulate(r.bounds);
            }

            Debug.Log($"[桌面窗口] 贴合: 塔主体={towerBody.name} ({towerBody.bounds.size.x:F1}x{towerBody.bounds.size.y:F1}), {count} 个 Sprite（其中强制纳入 {forced} 个）, 总包围盒 {b.size.x:F1}x{b.size.y:F1}");
        }

        lastFitBounds = b;
        hasFitBounds = true;

        // 1:1 取景：正交尺寸取包围盒宽/高的较大值，正方形视野两个方向都能完整容纳，
        // 塔身不会被上下裁掉；水平多出来的部分是透明留白。
        float halfExtent = Mathf.Max(b.extents.x, b.extents.y);
        cam.transform.position = new Vector3(
            b.center.x + centerOffset.x,
            b.center.y + centerOffset.y,
            cam.transform.position.z);
        cam.orthographicSize = halfExtent;
        baseOrthoSize = halfExtent; // 记住基准值，缩放时用

        // 窗口固定 1:1（高度占比 → 边长），不再跟随包围盒宽高比
#if !UNITY_EDITOR
        int h = Mathf.Max(200, Mathf.RoundToInt((float)Display.main.systemHeight * EffectiveFitHeightRatio));
        int w = h;
        ClampToWorkArea(ref w, ref h); // 等比缩放，正方形不会变形
        fitW = w; fitH = h;
        windowScale = Mathf.Clamp(windowScale, EffectiveMinWindowScale, EffectiveMaxWindowScale);
        CurrentWindowScale = windowScale;

        // 窗口物理尺寸固定不变，用 orthographicSize 做缩放
        Debug.Log($"[桌面窗口] 窗口贴合(1:1): 基础orthoSize={baseOrthoSize:F2}, 窗口={fitW}x{fitH}, 当前缩放=x{windowScale:F2}");
#endif
    }

    /// <summary>
    /// 白名单判定：对象自身或任一祖先（到 centerTarget 为止）名字命中 fitAlwaysIncludeNames 时，
    /// 跳过 fitRadius 距离过滤，强制并入贴合包围盒。
    /// </summary>
    bool IsForceIncluded(Renderer r)
    {
        // 神明被开关关掉时，白名单不再强制纳入，窗口才能正确收缩
        if (!godVisible && godRoot != null && r.transform.IsChildOf(godRoot.transform)) return false;
        if (fitAlwaysIncludeNames == null || fitAlwaysIncludeNames.Length == 0) return false;
        for (Transform t = r.transform; t != null; t = t.parent)
        {
            foreach (string n in fitAlwaysIncludeNames)
            {
                if (!string.IsNullOrEmpty(n) && t.name == n) return true;
            }
            if (centerTarget != null && t == centerTarget) break;
        }
        return false;
    }

    // ==================== 延迟初始化 ====================
    System.Collections.IEnumerator DelayedSetup()
    {
        for (int i = 0; i < 2; i++) yield return null;
        InitTransparentRendering();
    }

    void SetupWindowStylesEarly()
    {
        hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) { Debug.LogError("[桌面窗口] 获取窗口句柄失败"); return; }
        Debug.Log($"[桌面窗口] hwnd=0x{hwnd.ToInt64():X}");

        // 固定窗口物理尺寸（用 SetWindowPos，不走 SetResolution）
#if !UNITY_EDITOR
        if (fitW > 0 && fitH > 0)
        {
            // 有记住的位置就一次到位；没有就只改尺寸、保持 Windows 给的位置
            // （以前这里写死 0,0 且没带 SWP_NOMOVE，每次启动都先把窗口甩到左上角）
            Vector2 saved;
            if (TryGetSavedWindowPosition(out saved))
            {
                Vector2 p = ClampWindowPosToWorkArea(saved.x, saved.y);
                SetWindowPos(hwnd, IntPtr.Zero, (int)p.x, (int)p.y, fitW, fitH, SWP_NOZORDER | SWP_NOACTIVATE);
                Debug.Log($"[桌面窗口] 启动即恢复窗口位置 ({p.x:F0}, {p.y:F0})");
            }
            else
            {
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, fitW, fitH, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
            }
            Debug.Log($"[桌面窗口] 固定窗口尺寸 {fitW}x{fitH}");
        }
#endif

        EnsureBorderless();

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | (int)WS_EX_LAYERED);

        // 置顶放到所有样式写入之后：SetWindowLong(GWL_EXSTYLE)/SWP_FRAMECHANGED 都会让窗口
        // 重新走一遍非客户区计算，置顶位在它之前设，容易被这一步覆盖掉。
        ApplyTopMost();
    }

    void EnsureBorderless()
    {
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~(int)WS_CAPTION & ~(int)WS_THICKFRAME & ~(int)WS_MINIMIZEBOX & ~(int)WS_MAXIMIZEBOX & ~(int)WS_SYSMENU;
        SetWindowLong(hwnd, GWL_STYLE, style);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    public void ApplyTopMost()
    {
#if !UNITY_EDITOR
        if (hwnd == IntPtr.Zero) hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero)
        {
            Debug.LogWarning("[桌面窗口] 置顶失败：拿不到窗口句柄 hwnd");
            return;
        }
        int before = GetWindowLong(hwnd, GWL_EXSTYLE);
        bool ok = SetWindowPos(hwnd, (IntPtr)(topMost ? -1 : -2), 0, 0, 0, 0,
                               SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        int after = GetWindowLong(hwnd, GWL_EXSTYLE);
        Debug.Log($"[桌面窗口] 置顶={((after & (int)WS_EX_TOPMOST) != 0 ? 1 : 0)} 期望={topMost} " +
                  $"hwnd=0x{hwnd.ToInt64():X} TOPMOST前={((before & (int)WS_EX_TOPMOST) != 0 ? 1 : 0)} " +
                  $"SetWindowPos={ok}");
#else
        // 编辑器下不能真的调用 SetWindowPos：hwnd 会拿到 Unity 编辑器窗口，
        // 把编辑器设成置顶。这里只留日志，置顶效果只能打包验证。
        Debug.Log($"[桌面窗口] SetTopMost({topMost}) 在编辑器内是空操作（需打包验证）");
#endif
    }

    /// <summary>
    /// 设置面板调用：切换窗口置顶状态并持久化
    /// </summary>
    public void SetTopMost(bool value)
    {
        topMost = value;
        PlayerPrefs.SetInt(TopMostKey, topMost ? 1 : 0);
        PlayerPrefs.Save();
        ApplyTopMost();
    }

    /// <summary>
    /// 设置面板查询：当前是否置顶（供 Toggle 初始化用）
    /// </summary>
    public bool IsTopMost() => topMost;

    // ==================== 神明显隐 ====================
    /// <summary>
    /// 设置面板查询：神明当前是否显示（供 Toggle 初始化用）
    /// </summary>
    public bool IsGodVisible()
    {
        if (!godStateLoaded) LoadGodState();
        return godVisible;
    }

    void LoadGodState()
    {
        godStateLoaded = true;
        // 没有存档时默认显示，保持旧行为
        godVisible = !PlayerPrefs.HasKey(GodVisibleKey) || PlayerPrefs.GetInt(GodVisibleKey) == 1;
        FindGodRoot();
        if (godRoot != null) godRoot.SetActive(godVisible);
        Debug.Log($"[桌面窗口] 神明状态: {(godVisible ? "显示" : "隐藏")}（存档{(PlayerPrefs.HasKey(GodVisibleKey) ? "有" : "无")}）, godRoot={(godRoot != null ? godRoot.name : "未找到")}");
    }

    /// <summary>
    /// 按名字找神明根节点。注意 GameObject.Find 查不到未激活对象，
    /// 而场景里神明默认是未激活的（m_IsActive: 0），所以这里必须用包含未激活对象的遍历。
    /// </summary>
    GameObject FindGodRoot()
    {
        if (godRoot != null) return godRoot;
        if (string.IsNullOrEmpty(godRootName)) return null;
        // 先精确同名（避免命中「神明显示开关」这类 UI 物体），再退化为包含匹配
        Transform[] all = FindObjectsOfType<Transform>(true);
        foreach (Transform t in all)
            if (t != null && t.name == godRootName) { godRoot = t.gameObject; return godRoot; }
        foreach (Transform t in all)
            if (t != null && t.name.Contains(godRootName)) { godRoot = t.gameObject; return godRoot; }
        Debug.LogWarning($"[桌面窗口] 找不到神明根节点（名字含“{godRootName}”），显隐开关不会生效");
        return null;
    }

    /// <summary>
    /// 设置面板调用：切换神明显隐并持久化。
    /// 只做显隐，不重算取景/窗口尺寸：
    /// 运行时改 fitW/fitH 会重建 RT 并重设相机正交尺寸，ScreenSpaceCamera 画布与世界坐标的
    /// 内层画布（显示面板）会一起错位，导致整个 UI 布局崩掉。
    /// 神明是几乎全在画面内的远景淡影，显隐不需要重新取景。
    /// </summary>
    public void SetGodVisible(bool value)
    {
        godVisible = value;
        godStateLoaded = true;
        PlayerPrefs.SetInt(GodVisibleKey, godVisible ? 1 : 0);
        PlayerPrefs.Save();

        if (godRoot == null) FindGodRoot();
        if (godRoot != null) godRoot.SetActive(godVisible);

        Debug.Log($"[桌面窗口] 神明开关: {(godVisible ? "显示" : "隐藏")}（godRoot={(godRoot != null ? godRoot.name : "未找到")}，保持窗口 {fitW}x{fitH} 与 UI 缩放不变）");
    }

    // ==================== UI 永远置顶 ====================
    // Canvas 保持 ScreenSpaceCamera 模式（不变形），CanvasScaler.scaleFactor 同步缩放
    void EnsureCanvasesOnTop(Camera cam)
    {
        if (cam == null) cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam == null) { Debug.LogWarning("[桌面窗口] EnsureCanvasesOnTop: 找不到相机"); return; }

        // 远裁剪面 ≥ 放 Canvas 的距离
        if (cam.farClipPlane < 500f) cam.farClipPlane = 1000f;

        // 项目最顶层 Sorting Layer（列表最后一个）
        var layers = SortingLayer.layers;
        int topLayerId = layers != null && layers.Length > 0 ? layers[layers.Length - 1].id : 0;

        // 使用缓存而非每次 FindObjectsOfType
        int rootCount = 0;
        foreach (Canvas c in cachedCanvases)
        {
            if (c == null) continue;
            if (c.isRootCanvas) rootCount++;
        }

        // 收集所有 Canvas 所在的 layerIndex，确保相机 cullingMask 包含它们
        int canvasLayers = 0;
        foreach (Canvas c in cachedCanvases)
        {
            if (c == null || !c.isRootCanvas) continue;
            canvasLayers |= (1 << c.gameObject.layer);
        }
        int newMask = cam.cullingMask | canvasLayers;
        if (newMask != cam.cullingMask)
        {
            cam.cullingMask = newMask;
        }

        // scaleFactor 每个根 Canvas 统一 = 按高度算的基准 × 0.94 × 当前滚轮档位，
        // 相机 orthoSize 同时按 1/windowScale 变化，两者同比例，UI 与场景同步缩放。

        foreach (Canvas c in cachedCanvases)
        {
            if (c == null || !c.isRootCanvas) continue;

            // ScreenSpaceCamera：不变形，GraphicRaycaster 正常工作
            // 全部带“值变了才写”的判定：这个函数每 5 秒被调用一次，
            // 无条件重设 renderMode / worldCamera / scaleFactor 会让 Canvas 周期性重建，表现为 UI 闪一下。
            if (c.renderMode != RenderMode.ScreenSpaceCamera)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
            }
            if (c.worldCamera != cam) c.worldCamera = cam;
            float targetPlane = cam.farClipPlane * 0.9f;
            if (!Mathf.Approximately(c.planeDistance, targetPlane)) c.planeDistance = targetPlane;

            // CanvasScaler 切 ConstantPixelSize 模式，不会自动覆盖我们设的 scaleFactor
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.enabled = true;
                if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize)
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

                float targetSF = TargetScaleFactor();
                if (!Mathf.Approximately(scaler.scaleFactor, targetSF))
                    scaler.scaleFactor = targetSF;
            }

            // 确保有 CustomGraphicRaycaster（Screen 坐标 ≠ RT 坐标）
            var origRaycaster = c.GetComponent<GraphicRaycaster>();
            var customRaycaster = c.GetComponent<CustomGraphicRaycaster>();
            if (customRaycaster == null)
            {
                if (origRaycaster != null) Destroy(origRaycaster);
                customRaycaster = c.gameObject.AddComponent<CustomGraphicRaycaster>();
            }
            // 挪到最顶层 Sorting Layer + sortingOrder ≥30000
            if (c.sortingLayerID != topLayerId) c.sortingLayerID = topLayerId;
            if (c.sortingOrder < 30000) c.sortingOrder = Mathf.Min(32767, 30000 + c.sortingOrder);
        }

        // 嵌套 Canvas 也有自己的 GraphicRaycaster，而上面的循环只处理根画布。
        // 只换根画布的话，嵌套面板里的按钮仍然用原始 Screen 坐标做射线，
        // 一旦 Screen 与 RT 尺寸不同就会整体偏移（表现为“按钮要点偏一点才中”）。
        int replaced = 0;
        foreach (Canvas c in cachedCanvases)
        {
            if (c == null) continue;
            if (c.GetComponent<CustomGraphicRaycaster>() != null) continue;
            var orig = c.GetComponent<GraphicRaycaster>();
            if (orig != null) Destroy(orig);
            c.gameObject.AddComponent<CustomGraphicRaycaster>();
            replaced++;
        }
        if (replaced > 0)
            Debug.Log($"[桌面窗口] 已为 {replaced} 个嵌套 Canvas 统一射线换算");

        Debug.Log($"[桌面窗口] Canvas 扫描: {rootCount} 个根 Canvas, 相机={cam.name}, texW/H={texW}x{texH}, windowScale={windowScale:F2}");
    }

    // 刷新 Canvas 缓存（FindObjectsOfType 较慢，只在必要时调用）
    void RefreshCanvasCache()
    {
        cachedCanvases.Clear();
        cachedCanvases.AddRange(FindObjectsOfType<Canvas>(true));
    }

    // ==================== 初始化透明渲染管线 ====================
    void InitTransparentRendering()
    {
        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam == null) { Debug.LogError("[桌面窗口] 找不到相机"); return; }

        // 用固定的窗口尺寸（SetWindowPos 设的物理像素）
        texW = fitW > 0 ? fitW : Display.main.systemWidth;
        texH = fitH > 0 ? fitH : Display.main.systemHeight;
        RTWidth = texW;
        RTHeight = texH;

        // 坐标自检：鱼/珊瑚/UI 三处点击都依赖这组数字。若 Screen 与 RT 不一致，
        // 说明换算确实在生效；若一致则换算系数恒为 1，点击应当与编辑器等价。
        float kx = Screen.width > 0 ? (float)texW / Screen.width : 1f;
        float ky = Screen.height > 0 ? (float)texH / Screen.height : 1f;
        Debug.Log($"[桌面窗口] 坐标基准: Screen={Screen.width}x{Screen.height}, RT={texW}x{texH}, " +
                  $"窗口={fitW}x{fitH}, 换算系数=({kx:F3},{ky:F3})");
        if (Screen.width != texW || Screen.height != texH)
        {
            Debug.LogWarning($"[桌面窗口] Screen 与 RT 尺寸不一致（{Screen.width}x{Screen.height} vs {texW}x{texH}）：" +
                             "所有鼠标换算已自动补偿；若点击仍偏移，请把这一行日志发回。");
        }

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        if (rt != null) rt.Release();
        rt = new RenderTexture(texW, texH, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;

        RefreshCanvasCache();
        EnsureCanvasesOnTop(cam);

        if (readback != null) Destroy(readback);
        readback = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
        bgra = new byte[texW * texH * 4];
        bgraPending = new byte[texW * texH * 4];

        // 预乘中间 RT：双缓冲，尺寸/格式与 rt 保持一致，只建一次，之后每帧复用。
        // 双缓冲的作用：一块正在做异步回读时，下一帧写另一块，不会读写冲突。
        if (rtPremul != null) { rtPremul.Release(); Destroy(rtPremul); }
        rtPremul = CreatePremulRT("桌面窗口_预乘A");
        if (rtPremul2 != null) { rtPremul2.Release(); Destroy(rtPremul2); }
        rtPremul2 = CreatePremulRT("桌面窗口_预乘B");

        premulPending = -1;
        premulWrite = 0;
        frameReady = false;
        asyncReadbackEnabled = SystemInfo.supportsAsyncGPUReadback;
        presentFrameCount = 0;
        presentAccumMs = 0f;

        EnsurePremulMaterial();

        CreateDib();
#if !UNITY_EDITOR
        transparentReady = true;
#endif

        // 初始缩放
        cam.orthographicSize = baseOrthoSize / Mathf.Max(0.01f, windowScale);
        OnWindowScaleChanged?.Invoke(windowScale);

        Debug.Log($"[桌面窗口] ULW 已启动 ({texW}x{texH}), orthoSize={cam.orthographicSize:F2} (base={baseOrthoSize:F2}/scale={windowScale:F2}), UI系数={TargetScaleFactor():F3}（基准 {CanvasBaseScaleFactor(new Vector2(1920f, UiDesignHeight)):F3}×0.94×{windowScale:F2}）");
    }

    RenderTexture CreatePremulRT(string rtName)
    {
        RenderTexture t = new RenderTexture(texW, texH, 0, RenderTextureFormat.ARGB32);
        t.name = rtName;
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        t.Create();
        return t;
    }

    RenderTexture PremulAt(int index) => index == 0 ? rtPremul : rtPremul2;

    /// <summary>
    /// GPU 异步回读完成回调（Unity 在主线程调用）。
    /// request 的 NativeArray 只在回调内有效，必须当场拷贝；拿到的字节序与 ReadPixels 相同，
    /// 已经是「预乘 + BGRA」，可以直接进 DIB。
    /// </summary>
    void OnAsyncReadback(AsyncGPUReadbackRequest request)
    {
        premulPending = -1;
        if (request.hasError)
        {
            asyncReadbackEnabled = false;
            Debug.LogWarning("[桌面窗口] GPU 异步回读失败，回退到同步 ReadPixels");
            return;
        }
        request.GetData<byte>().CopyTo(bgraPending);
        byte[] tmp = bgra;
        bgra = bgraPending;
        bgraPending = tmp;
        frameReady = true;
    }

    /// <summary>同步兜底路径（不支持 AsyncGPUReadback 或异步失败时使用）。</summary>
    void SyncReadback(int index)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = PremulAt(index);
        readback.ReadPixels(new Rect(0, 0, texW, texH), 0, 0);
        RenderTexture.active = prev;

        readback.GetRawTextureData<byte>().CopyTo(bgra);
        frameReady = true;
    }

    /// <summary>
    /// 懒创建预乘材质。Shader 放在 Resources 下（PremultiplyBGRA.shader），
    /// 因为 Shader.Find 对"没有任何材质引用"的 shader 在发布版里会返回 null（被剥离），
    /// Resources.Load 能保证它被打进包里，同时不用去改 ProjectSettings 的 Always Included Shaders。
    /// </summary>
    void EnsurePremulMaterial()
    {
        if (premulMat != null) return;

        Shader sh = Resources.Load<Shader>("PremultiplyBGRA");
        if (sh == null) sh = Shader.Find("Hidden/Haodong/PremultiplyBGRA");
        if (sh == null)
        {
            Debug.LogError("[桌面窗口] 找不到预乘 Shader，本次运行回退到 CPU 逐像素预乘");
            return;
        }

        premulMat = new Material(sh);
        premulMat.hideFlags = HideFlags.DontSave;
    }

    void CreateDib()
    {
        DestroyDib();
        IntPtr desktopDC = GetDC(IntPtr.Zero);
        memDC = CreateCompatibleDC(desktopDC);
        ReleaseDC(IntPtr.Zero, desktopDC);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
        bmi.bmiHeader.biWidth = texW;
        bmi.bmiHeader.biHeight = texH;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        dib = CreateDIBSection(memDC, ref bmi, 0, out dibBits, IntPtr.Zero, 0);
        dibOld = SelectObject(memDC, dib);
    }

    void DestroyDib()
    {
        if (dibOld != IntPtr.Zero && memDC != IntPtr.Zero) { SelectObject(memDC, dibOld); dibOld = IntPtr.Zero; }
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        if (memDC != IntPtr.Zero) { DeleteDC(memDC); memDC = IntPtr.Zero; }
        dibBits = IntPtr.Zero;
    }

    void OnDestroy()
    {
#if !UNITY_EDITOR
        transparentReady = false;
#endif
        DestroyDib();
        if (rt != null) { rt.Release(); rt = null; }
        if (rtPremul != null) { rtPremul.Release(); Destroy(rtPremul); rtPremul = null; }
        if (rtPremul2 != null) { rtPremul2.Release(); Destroy(rtPremul2); rtPremul2 = null; }
        premulPending = -1;
        frameReady = false;
        if (premulMat != null) { Destroy(premulMat); premulMat = null; }
        if (readback != null) { Destroy(readback); readback = null; }
    }

    // ==================== Update ====================
    void Update()
    {
#if !UNITY_EDITOR
        // 固定窗口尺寸 → RT 永不变，永远只 PresentFrame
        if (transparentReady)
        {
            // 窗口样式自愈低频执行即可（函数内部还有 30 帧节流）：
            // 这里用时间门控，避免掉帧时“30 帧”被拉成很长、也避免高帧率下过于频繁。
            if (Time.unscaledTime - lastRepairTime >= 0.5f)
            {
                lastRepairTime = Time.unscaledTime;
                RepairWindowStyle();
            }
            PresentFrame();
        }
#endif

        // 点击诊断：按下时一次性打印“坐标是否偏 / 是否被别的 Graphic 挡住 / 输入有没有到”
        if (debugClickLog && Input.GetMouseButtonDown(0)) LogClickDiagnostics();

        HandleWindowDrag();
        HandleWheelZoom();
        ApplyZoomDebounce();

        // Shift+Space 贴底缩小切换
        if (Input.GetKeyDown(KeyCode.Space) && Input.GetKey(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.Space) && Input.GetKey(KeyCode.RightShift))
        {
            ToggleMiniBottom();
        }

        // 时钟微调：Ctrl+方向键
        if (clockFollower != null && Input.GetKey(KeyCode.LeftControl))
        {
            const float step = 0.05f;
            Vector2 n = clockNudgeOffset;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) n.x -= step;
            if (Input.GetKeyDown(KeyCode.RightArrow)) n.x += step;
            if (Input.GetKeyDown(KeyCode.UpArrow)) n.y += step;
            if (Input.GetKeyDown(KeyCode.DownArrow)) n.y -= step;
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
            {
                n = Vector2.zero;
                PlayerPrefs.DeleteKey(ClockNudgeXKey);
                PlayerPrefs.DeleteKey(ClockNudgeYKey);
                PlayerPrefs.Save();
            }
            if (n != clockNudgeOffset)
            {
                clockNudgeOffset = n;
                PlayerPrefs.SetFloat(ClockNudgeXKey, n.x);
                PlayerPrefs.SetFloat(ClockNudgeYKey, n.y);
                PlayerPrefs.Save();
            }
        }
        if (clockFollower != null)
            clockFollower.worldOffset = ClockTargetWorldOffset() + clockNudgeOffset;

        // 定期刷新 Canvas 缓存（运行时新建的 UI 也能被捕获），然后强制置顶
        // 只有透明桌面窗口版需要，编辑器里让 Unity 自己管
#if !UNITY_EDITOR
        canvasCacheRefreshTimer += Time.deltaTime;
        if (canvasCacheRefreshTimer >= CanvasCacheRefreshInterval)
        {
            canvasCacheRefreshTimer = 0f;
            RefreshCanvasCache();
            EnsureCanvasesOnTop(null);
        }
#endif
    }

    // ==================== 每帧上屏 ====================
    void PresentFrame()
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;

        var watch = System.Diagnostics.Stopwatch.StartNew();

        if (premulMat != null && rtPremul != null && rtPremul2 != null && rt != null)
        {
            // ===== GPU 通道重排 + 异步回读 =====
            // 以前每帧 readback.ReadPixels 会强制 CPU 等 GPU 画完（1200×1200×4≈5.8MB），
            // 这是构建版卡顿的主因。改成 AsyncGPUReadback 后 CPU 不再被阻塞，
            // 上屏比逻辑帧晚 1 帧（肉眼不可见）。
            // 双缓冲：正在回读的那一块不能再写，写另一块。
            int write = premulPending == 0 ? 1 : (premulPending == 1 ? 0 : premulWrite);
            Graphics.Blit(rt, PremulAt(write), premulMat);

            if (asyncReadbackEnabled)
            {
                if (premulPending < 0)
                {
                    premulPending = write;
                    AsyncGPUReadback.Request(PremulAt(write), 0, TextureFormat.RGBA32, OnAsyncReadback);
                }
            }
            else
            {
                SyncReadback(write);
            }
            premulWrite = write == 0 ? 1 : 0;
        }
        else
        {
            // ===== 兜底：找不到预乘 Shader 时完全保持原来的 CPU 路径 =====
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, texW, texH), 0, 0);
            RenderTexture.active = prevActive;

            // RGBA → BGRA（RT 里已经是预乘 alpha，这里不再乘一次，否则半透明会变暗/发黑）
            NativeArray<Color32> px = readback.GetRawTextureData<Color32>();
            int n = texW * texH;
            for (int i = 0; i < n; i++)
            {
                Color32 c = px[i];
                int o = i << 2;
                bgra[o]     = c.b;
                bgra[o + 1] = c.g;
                bgra[o + 2] = c.r;
                bgra[o + 3] = c.a;
            }
            frameReady = true;
        }

        // 首帧 / 回读还没回来：本帧不上屏，窗口保持上一帧内容
        if (!frameReady) return;
        frameReady = false;

        Marshal.Copy(bgra, 0, dibBits, bgra.Length);

#if UNITY_EDITOR
        DiagnosePixels();
#endif

        RECT r;
        GetWindowRect(hwnd, out r);
        var pptDst = new POINT { X = r.Left, Y = r.Top };
        var psize = new SIZE { CX = texW, CY = texH };
        var pprSrc = new POINT { X = 0, Y = 0 };
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };

        IntPtr screenDC = GetDC(IntPtr.Zero);
        bool ok = UpdateLayeredWindow(hwnd, screenDC, ref pptDst, ref psize, memDC, ref pprSrc, 0, ref blend, ULW_ALPHA);
        ReleaseDC(IntPtr.Zero, screenDC);

        if (!ok && Time.frameCount % 300 == 0)
            Debug.LogWarning("[桌面窗口] UpdateLayeredWindow 失败");

        // 上屏耗时自测：每 300 帧一条，用户回传日志即可判断卡顿是否来自这条管线
        watch.Stop();
        presentAccumMs += (float)watch.Elapsed.TotalMilliseconds;
        presentFrameCount++;
        if (presentFrameCount >= 300)
        {
            Debug.Log($"[性能] 上屏平均 {presentAccumMs / presentFrameCount:F2} ms/帧（{presentFrameCount} 帧，异步回读={asyncReadbackEnabled}）");
            presentAccumMs = 0f;
            presentFrameCount = 0;
        }
    }

#if UNITY_EDITOR
    // 编辑器调试用：诊断像素颜色（打包版不编译，避免不必要的 GC 和磁盘 I/O）
    void DiagnosePixels()
    {
        if (Time.frameCount % 300 != 0 || texW <= 0 || texH <= 0) return;
        Color cc = readback.GetPixel(texW / 2, texH / 2);
        Color tl = readback.GetPixel(0, 0);
        Color tr = readback.GetPixel(texW - 1, 0);
        Color bl = readback.GetPixel(0, texH - 1);
        Color br = readback.GetPixel(texW - 1, texH - 1);
        static string Pf(Color c) => $"({(byte)(c.r*255)},{(byte)(c.g*255)},{(byte)(c.b*255)},{(byte)(c.a*255)})";
        Debug.Log($"[桌面窗口] RT 像素: center={Pf(cc)} TL={Pf(tl)} TR={Pf(tr)} BL={Pf(bl)} BR={Pf(br)}, tex={texW}x{texH}");
    }
#endif

    // ==================== 窗口样式自愈 ====================
    void RepairWindowStyle()
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            hwnd = GetActiveWindow();
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
            EnsureBorderless();
            int ex0 = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex0 | (int)WS_EX_LAYERED);
            return;
        }
        if (Time.frameCount % 30 != 0) return;

        int style = GetWindowLong(hwnd, GWL_STYLE);
        if ((style & (int)WS_CAPTION) != 0) EnsureBorderless();
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((ex & (int)WS_EX_LAYERED) == 0)
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | (int)WS_EX_LAYERED);

        // 置顶自愈：分辨率切换/窗口重建/Unity 自身的窗口设置都可能改写 WS_EX_TOPMOST，
        // 这里按“期望状态”双向校正：该置顶就补 -1，该取消就补 -2，
        // 否则关掉置顶后只要有人把位置回 TOPMOST，就再也没人把它摘掉了。
        if (topMost && (ex & (int)WS_EX_TOPMOST) == 0)
        {
            SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
        else if (!topMost && (ex & (int)WS_EX_TOPMOST) != 0)
        {
            SetWindowPos(hwnd, (IntPtr)(-2), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        // 保持窗口尺寸不弹回（Unity/Windows 可能周期性重置窗口大小）
#if !UNITY_EDITOR
        if (fitW > 0 && fitH > 0)
        {
            RECT r;
            GetWindowRect(hwnd, out r);
            int curW = r.Right - r.Left;
            int curH = r.Bottom - r.Top;
            if (Mathf.Abs(curW - fitW) > 2 || Mathf.Abs(curH - fitH) > 2)
            {
                SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, fitW, fitH, SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
#endif
    }

    // ==================== 最小化 ====================
    public void MinimizeWindow()
    {
#if !UNITY_EDITOR
        if (hwnd == IntPtr.Zero) hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_MINIMIZE);
#endif
    }

    // ==================== 点击诊断 ====================
    /// <summary>
    /// 一次点击就能区分三种故障：
    ///   1) 偏了：screen 与 rt 明显不同、命中对象与鼠标位置对不上；
    ///   2) 被挡了：最上层命中的不是目标按钮（看打印的前 5 个命中）；
    ///   3) 输入没到：EventSystem 缺失/无 InputModule，或根本没有命中。
    /// </summary>
    void LogClickDiagnostics()
    {
        Vector2 screenPos = Input.mousePosition;
        Vector2 rtPos = ScreenToRenderTexturePoint(screenPos);

        string client = "?";
        if (hwnd != IntPtr.Zero && GetClientRect(hwnd, out RECT cr))
            client = $"{cr.Right - cr.Left}x{cr.Bottom - cr.Top}";

        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
        {
            Debug.LogWarning($"[点击诊断] screen={screenPos} rt={rtPos} Screen={Screen.width}x{Screen.height} " +
                             $"客户区={client} 焦点={Application.isFocused} → EventSystem 不存在，UI 没有接收者");
            return;
        }

        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        var data = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPos };
        es.RaycastAll(data, results);

        string top = results.Count > 0
            ? $"{results[0].gameObject.name}[{results[0].module?.GetType().Name}]"
            : "无";

        Debug.Log($"[点击诊断] screen={screenPos} rt={rtPos} Screen={Screen.width}x{Screen.height} " +
                  $"客户区={client} 命中={results.Count} 最上层={top} 焦点={Application.isFocused} " +
                  $"世界交互物={IsPointerOverWorldInteractable()}");

        for (int i = 0; i < results.Count && i < 5; i++)
        {
            var r = results[i];
            var g = r.gameObject.GetComponent<UnityEngine.UI.Graphic>();
            Debug.Log($"    命中[{i}] {r.gameObject.name} depth={r.depth} sorting={r.sortingOrder} " +
                      $"graphic={(g != null ? g.GetType().Name : "无")} " +
                      $"raycast={(g != null ? g.raycastTarget : false)} " +
                      $"alpha={(g != null ? g.color.a : 0f):F2} module={r.module?.GetType().Name}");
        }
    }

    /// <summary>启动自检：没有 EventSystem / 没有 InputModule，所有 UGUI 点击都不会响应。</summary>
    void ValidateEventSystem()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
        {
            Debug.LogError("[桌面窗口] 场景里没有启用的 EventSystem：所有 UGUI 按钮都不会响应");
            return;
        }
        var module = es.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
        if (module == null || !module.isActiveAndEnabled)
        {
            Debug.LogError($"[桌面窗口] EventSystem({es.name}) 没有可用的 InputModule：" +
                           "UGUI 点击不会响应（检查 StandaloneInputModule / InputSystemUIInputModule）");
            return;
        }
        Debug.Log($"[桌面窗口] EventSystem 就绪: {es.name}, 输入模块={module.GetType().Name}");
    }

    // ==================== 拖动窗口 ====================
    void HandleWindowDrag()
    {
        if (!dragWindowAnywhere) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (IsPointerOverUI()) return;
        // 点鱼/珊瑚时不能抢：进入模态拖动后 Unity 收不到这一帧的点击，
        // 鱼的 HandleClickInput 和珊瑚的放置判定都跑不到（编辑器里拖动是空操作，所以只在打包版出现）。
        if (IsPointerOverWorldInteractable()) return;

#if !UNITY_EDITOR
        if (hwnd == IntPtr.Zero) hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero) return;
        Debug.Log("[桌面窗口] 拖动窗口");
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
#endif
    }

    bool IsPointerOverUI()
    {
        // 珊瑚拖拽图标：自己的矩形检测
        if (DragAndPlaceSystem.IsPointerOverAnyIcon(Input.mousePosition)) return true;

        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return false;

        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        var data = new UnityEngine.EventSystems.PointerEventData(es);
        data.position = Input.mousePosition;
        es.RaycastAll(data, results);

        foreach (var r in results)
        {
            if (r.gameObject.GetComponent<UnityEngine.UI.Selectable>() != null) return true;
            if (r.gameObject.GetComponent<DragAndPlaceSystem>() != null) return true;
            var g = r.gameObject.GetComponent<UnityEngine.UI.Graphic>();
            if (g != null && g.raycastTarget && g.color.a > 0.05f) return true;
        }
        return false;
    }

    // ==================== 滚轮缩放 ====================
    void HandleWheelZoom()
    {
        // 贴底缩小模式下禁用滚轮缩放，防止 savedWindowScale 与实际状态脱节
        if (isMiniBottomMode) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

#if !UNITY_EDITOR
        // 只累计滚动量，等停下 0.15s 后再一次性应用。
        // 以前这里每格直接改 windowScale，但相机要等去抖结束才变，
        // 中间 EnsureCanvasesOnTop 会拿新的 windowScale 重设 UI 缩放 ——
        // UI 先跳、相机后跟，看起来就是缩放时 UI 一闪一闪。
        pendingZoomScroll += scroll;
        zoomDebounceDeadline = Time.time + 0.15f;
#endif
    }

    void ApplyZoomDebounce()
    {
#if !UNITY_EDITOR
        if (zoomDebounceDeadline <= 0f || Time.time < zoomDebounceDeadline) return;
        zoomDebounceDeadline = 0f;
        if (Mathf.Abs(pendingZoomScroll) < 0.0001f) return;

        float newScale = Mathf.Clamp(windowScale + pendingZoomScroll * ZoomStep, EffectiveMinWindowScale, EffectiveMaxWindowScale);
        pendingZoomScroll = 0f;
        if (Mathf.Approximately(newScale, windowScale)) return;

        bool ok = ApplyWindowScale(newScale);
        if (!ok) return;

        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        Debug.Log($"[桌面窗口] 缩放: orthoSize={(cam != null ? cam.orthographicSize : 0f):F2}, baseOrtho={baseOrthoSize:F2}, windowScale={windowScale:F2}");
#endif
    }

    /// <summary>
    /// 缩放的唯一写入口：相机正交尺寸和所有 CanvasScaler 在同一个函数、同一帧里一起改。
    /// 之前 SetWindowScale / ApplyZoomDebounce / EnsureCanvasesOnTop 三处各写一遍，
    /// 时序错开一帧就会出现「场景没变、UI 先变大」的闪动。
    /// </summary>
    bool ApplyWindowScale(float scale)
    {
        windowScale = Mathf.Clamp(scale, EffectiveMinWindowScale, EffectiveMaxWindowScale);
        CurrentWindowScale = windowScale; // 供世界→屏幕的像素偏移换算（UI 贴合场景物体）
#if !UNITY_EDITOR
        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam == null || baseOrthoSize <= 0f) return false;

        cam.orthographicSize = baseOrthoSize / Mathf.Max(0.01f, windowScale);

        foreach (Canvas c in cachedCanvases)
        {
            if (c == null || !c.isRootCanvas) continue;
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler == null) continue;
            float targetSF = TargetScaleFactor();
            if (!Mathf.Approximately(scaler.scaleFactor, targetSF))
                scaler.scaleFactor = targetSF;
        }
#endif
        // 一次性定位的 UI（珊瑚详情面板等）在 scaleFactor 变化后不会自己回到场景物体上方，
        // 这里通知它们重新贴一次。放在最后：此时相机/scaleFactor 都已是新值。
        OnWindowScaleChanged?.Invoke(windowScale);
        return true;
    }

    void ClampToWorkArea(ref int w, ref int h)
    {
        RECT wa = default;
        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, ref wa, 0)) return;
        float waW = wa.Right - wa.Left, waH = wa.Bottom - wa.Top;
        float s = Mathf.Min(1f, Mathf.Min(waW / Mathf.Max(1, w), waH / Mathf.Max(1, h)));
        if (s < 1f)
        {
            w = Mathf.Max(200, Mathf.RoundToInt(w * s));
            h = Mathf.Max(200, Mathf.RoundToInt(h * s));
        }
    }

    public void ResetZoom()
    {
        // 走统一入口：相机 orthoSize 与所有根 Canvas 的 scaleFactor 必须同帧一起回到 1 倍
        ApplyWindowScale(1f);
    }

    // ==================== Shift+Space 贴底缩小 ====================
    void ToggleMiniBottom()
    {
#if !UNITY_EDITOR
        if (!isMiniBottomMode)
        {
            // 保存当前状态
            savedWindowScale = windowScale;
            savedWindowPos = GetWindowPosition();

            // 缩到最小
            SetWindowScale(EffectiveMinWindowScale);

            // 移动到屏幕底部居中
            MoveWindowToBottomCenter();

            isMiniBottomMode = true;
            Debug.Log("[桌面窗口] Shift+Space: 贴底缩小");
        }
        else
        {
            // 恢复状态
            SetWindowScale(savedWindowScale);
            SetWindowPosition(savedWindowPos.x, savedWindowPos.y);

            isMiniBottomMode = false;
            Debug.Log("[桌面窗口] Shift+Space: 恢复原状态");
        }
#endif
    }

    void MoveWindowToBottomCenter()
    {
        if (hwnd == IntPtr.Zero) hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero) return;

        // 获取工作区（排除任务栏）
        RECT wa = default;
        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, ref wa, 0)) return;

        int waW = wa.Right - wa.Left;
        int waH = wa.Bottom - wa.Top;

        // 计算底部居中位置
        int newX = wa.Left + (waW - fitW) / 2;
        int newY = wa.Bottom - fitH;

        SetWindowPos(hwnd, IntPtr.Zero, newX, newY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // ==================== 窗口位置/缩放持久化（PlayerPrefs） ====================
    // 结论：窗口位置/缩放以前只在 OnApplicationQuit 写进 save.json，
    // 强关/崩溃/切包都会丢；现在改成显示偏好走 PlayerPrefs，拖动/缩放后 1 秒内落盘，
    // save.json 的 windowState 保留为老存档的一次性迁移来源。

    /// <summary>PlayerPrefs 里是否已经有记住的窗口位置。</summary>
    public bool HasSavedWindowState()
    {
#if UNITY_EDITOR
        return false;
#else
        return PlayerPrefs.HasKey(WindowPosXKey) && PlayerPrefs.HasKey(WindowPosYKey);
#endif
    }

    /// <summary>PlayerPrefs 里是否已经有记住的窗口缩放档位。</summary>
    public bool HasSavedWindowScale()
    {
#if UNITY_EDITOR
        return false;
#else
        return PlayerPrefs.HasKey(WindowScalePrefKey);
#endif
    }

    /// <summary>读取记住的窗口位置（左上角，物理像素）。没有记录返回 false。</summary>
    public bool TryGetSavedWindowPosition(out Vector2 pos)
    {
        pos = Vector2.zero;
#if UNITY_EDITOR
        return false;
#else
        if (!HasSavedWindowState()) return false;
        pos = new Vector2(PlayerPrefs.GetFloat(WindowPosXKey), PlayerPrefs.GetFloat(WindowPosYKey));
        return true;
#endif
    }

    /// <summary>启动时把缩放档位先抬回来（在 FitWindowToTarget / 透明管线初始化之前调用）。</summary>
    void LoadWindowPrefs()
    {
#if !UNITY_EDITOR
        if (!PlayerPrefs.HasKey(WindowScalePrefKey)) return;
        float raw = PlayerPrefs.GetFloat(WindowScalePrefKey);
        float migrated = MigrateLegacyWindowScale(raw);
        windowScale = Mathf.Clamp(migrated, EffectiveMinWindowScale, EffectiveMaxWindowScale);
        CurrentWindowScale = windowScale;
        // 迁移只发生这一次：把迁移后的值写回，下次启动就是“用户当前档位”
        if (!Mathf.Approximately(migrated, raw))
        {
            PlayerPrefs.SetFloat(WindowScalePrefKey, windowScale);
            PlayerPrefs.Save();
            Debug.Log($"[桌面窗口] windowScale 旧值 {raw:F2} 已迁移为 {windowScale:F2}");
        }
#endif
    }

    /// <summary>立即把当前窗口位置/缩放写进 PlayerPrefs（拖动/缩放结束、退出时调用）。</summary>
    public void SaveWindowStateToPrefs()
    {
#if !UNITY_EDITOR
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
        Vector2 p = GetWindowPosition();
        PlayerPrefs.SetFloat(WindowPosXKey, p.x);
        PlayerPrefs.SetFloat(WindowPosYKey, p.y);
        PlayerPrefs.SetFloat(WindowScalePrefKey, windowScale);
        PlayerPrefs.Save();
#endif
    }

    /// <summary>
    /// 把窗口左上角夹回「窗口所在那块显示器」的工作区，保证窗口整体可见。
    /// 🔑 多显示器时 SPI_GETWORKAREA 只返回主屏工作区，会把副屏上的坐标强行夹回主屏，
    ///    表现就是“记住的位置每次启动都被挪走”；这里按窗口矩形找最近的那块显示器再夹。
    /// </summary>
    Vector2 ClampWindowPosToWorkArea(float x, float y)
    {
        int w = fitW > 0 ? fitW : 200;
        int h = fitH > 0 ? fitH : 200;

        RECT target = new RECT { Left = (int)x, Top = (int)y, Right = (int)x + w, Bottom = (int)y + h };
        IntPtr mon = MonitorFromRect(ref target, MONITOR_DEFAULTTONEAREST);

        RECT wa = default;
        bool hasWorkArea = false;
        if (mon != IntPtr.Zero)
        {
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(mon, ref mi))
            {
                wa = mi.rcWork;
                hasWorkArea = true;
            }
        }
        if (!hasWorkArea && !SystemParametersInfo(SPI_GETWORKAREA, 0, ref wa, 0)) return new Vector2(x, y);

        x = Mathf.Clamp(x, wa.Left, Mathf.Max(wa.Left, wa.Right - w));
        y = Mathf.Clamp(y, wa.Top, Mathf.Max(wa.Top, wa.Bottom - h));
        return new Vector2(x, y);
    }

    /// <summary>
    /// 每秒比对一次真实窗口矩形和缩放档位，有变化就写 PlayerPrefs。
    /// 拖动窗口走的是 Windows 模态循环，Unity 收不到鼠标松开事件，没法在“拖完”精确落盘，
    /// 用轮询兜底最稳：最坏只丢最后 1 秒内的移动。
    /// </summary>
    System.Collections.IEnumerator AutoPersistWindowState()
    {
#if !UNITY_EDITOR
        Vector2 lastPos = GetWindowPosition();
        float lastScale = windowScale;
        while (true)
        {
            yield return new WaitForSecondsRealtime(1f);

            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                hwnd = GetActiveWindow();
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) continue;
            }

            Vector2 p = GetWindowPosition();
            bool moved = Mathf.Abs(p.x - lastPos.x) > 1f || Mathf.Abs(p.y - lastPos.y) > 1f;
            bool zoomed = Mathf.Abs(windowScale - lastScale) > 0.0001f;
            if (!moved && !zoomed) continue;

            lastPos = p;
            lastScale = windowScale;
            PlayerPrefs.SetFloat(WindowPosXKey, p.x);
            PlayerPrefs.SetFloat(WindowPosYKey, p.y);
            PlayerPrefs.SetFloat(WindowScalePrefKey, windowScale);
            PlayerPrefs.Save();
            Debug.Log($"[桌面窗口] 已记住窗口状态: pos=({p.x:F0},{p.y:F0}) scale={windowScale:F2}");
        }
#else
        yield break;
#endif
    }

    /// <summary>
    /// 非透明窗口路径（enableTransparent=false，没走 SetupWindowStylesEarly）的兜底：
    /// 等窗口句柄可用后，把记住的位置贴回去。
    /// </summary>
    System.Collections.IEnumerator RestoreWindowPositionWhenReady()
    {
#if !UNITY_EDITOR
        for (int i = 0; i < 30; i++)
        {
            if (hwnd == IntPtr.Zero) hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero && IsWindow(hwnd)) break;
            yield return null;
        }
        Vector2 saved;
        if (TryGetSavedWindowPosition(out saved))
            SetWindowPosition(saved.x, saved.y);
#endif
        yield break;
    }

    void OnApplicationQuit()
    {
        SaveWindowStateToPrefs();
    }

    // ==================== 供 SaveSystem 调用的接口 ====================
    public float GetWindowScale() { return windowScale; }
    public void SetWindowScale(float scale)
    {
        // 统一走 ApplyWindowScale：相机和 UI 同一帧一起变，避免出现一帧错位
        ApplyWindowScale(scale);
    }

    public Vector2 GetWindowPosition()
    {
#if !UNITY_EDITOR
        if (hwnd == IntPtr.Zero) return Vector2.zero;
        RECT r;
        GetWindowRect(hwnd, out r);
        return new Vector2(r.Left, r.Top);
#else
        return Vector2.zero;
#endif
    }

    public void SetWindowPosition(float x, float y)
    {
#if !UNITY_EDITOR
        if (hwnd != IntPtr.Zero)
        {
            // 窗口变大后，旧存档里的坐标可能把窗口推到屏幕外，统一夹回工作区
            Vector2 p = ClampWindowPosToWorkArea(x, y);
            SetWindowPos(hwnd, IntPtr.Zero, (int)p.x, (int)p.y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
#endif
    }
}
