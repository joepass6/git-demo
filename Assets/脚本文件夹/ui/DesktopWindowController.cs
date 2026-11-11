using UnityEngine;
using UnityEngine.UI;
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
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
    const uint SPI_GETWORKAREA = 0x0030;
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
    RenderTexture rtPremul;      // GPU 预乘后的中间 RT（无深度，尺寸与 rt 一致），每帧复用
    Material premulMat;          // 预乘 + BGR 重排材质，创建一次后复用
    Texture2D readback;
    IntPtr memDC = IntPtr.Zero, dib = IntPtr.Zero, dibOld = IntPtr.Zero, dibBits = IntPtr.Zero;
    int texW, texH;
    byte[] bgra;

    // 公开的 RT 尺寸（供其他脚本做 Screen→RT 坐标转换）
    public static int RTWidth { get; private set; }
    public static int RTHeight { get; private set; }

    /// <summary>
    /// 透明窗口下 Screen 分辨率 ≠ RenderTexture 分辨率，
    /// 直接 Camera.ScreenToWorldPoint 会偏移 → 先把 Screen 坐标按比例缩放到 RT 坐标再转。
    /// </summary>
    public static Vector3 ScreenToWorldPointCorrect(Camera cam, Vector2 screenPos)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return new Vector3(screenPos.x, screenPos.y, 0);
#if UNITY_EDITOR
        return cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, cam.nearClipPlane));
#else
        float sw = Screen.width, sh = Screen.height;
        float rw = RTWidth > 0 ? RTWidth : cam.pixelWidth;
        float rh = RTHeight > 0 ? RTHeight : cam.pixelHeight;
        Vector2 scaledPos = screenPos;
        if (sw > 0 && sh > 0 && rw > 0 && rh > 0)
        {
            scaledPos.x = screenPos.x * rw / sw;
            scaledPos.y = screenPos.y * rh / sh;
        }
        return cam.ScreenToWorldPoint(new Vector3(scaledPos.x, scaledPos.y, cam.nearClipPlane));
#endif
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

        Application.runInBackground = true;

#if !UNITY_EDITOR
        MakeProcessDpiAware();
        FixCanvasScalers(); // 编辑器里让 Unity 自己管 ScaleWithScreenSize，不要碰
#endif
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
            // 统一“设计高度”=1080：两个 1:1 的页面 Canvas 本来没有参考分辨率，
            // 必须给一个共同基准，后面 CanvasBaseScaleFactor 才能按同一高度换算。
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 1f; // 按高度匹配
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
        if (referenceResolution.x < 100f || referenceResolution.y < 100f)
            referenceResolution = new Vector2(1920f, 1080f);

        float designHeight = Mathf.Max(1f, referenceResolution.y);
        float pixelHeight = texH > 0 ? texH : Screen.height;
        return pixelHeight / designHeight;
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
        windowScale = Mathf.Clamp(windowScale, minWindowScale, maxWindowScale);

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
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, fitW, fitH, SWP_NOZORDER | SWP_NOACTIVATE);
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
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, (IntPtr)(topMost ? -1 : -2), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);
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

        // scaleFactor 每个 Canvas 统一 = 按高度算的基准系数 × windowScale，
        // 与相机“按高度装满窗口”保持同一基准，避免窄窗口下 UI 被按宽度额外压小

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

                float targetSF = CanvasBaseScaleFactor(scaler.referenceResolution)
                                 * Mathf.Max(0.01f, windowScale);
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

        // 预乘中间 RT：尺寸/格式与 rt 保持一致，只建一次，之后每帧复用
        if (rtPremul != null) { rtPremul.Release(); Destroy(rtPremul); }
        rtPremul = new RenderTexture(texW, texH, 0, RenderTextureFormat.ARGB32);
        rtPremul.name = "桌面窗口_预乘";
        rtPremul.filterMode = FilterMode.Point;
        rtPremul.wrapMode = TextureWrapMode.Clamp;
        rtPremul.Create();
        EnsurePremulMaterial();

        CreateDib();
#if !UNITY_EDITOR
        transparentReady = true;
#endif

        // 初始缩放
        cam.orthographicSize = baseOrthoSize / Mathf.Max(0.01f, windowScale);

        Debug.Log($"[桌面窗口] ULW 已启动 ({texW}x{texH}), orthoSize={cam.orthographicSize:F2} (base={baseOrthoSize:F2}/scale={windowScale:F2}), UI基准={CanvasBaseScaleFactor(new Vector2(1920f, 1080f)):F2}");
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
            RepairWindowStyle();
            PresentFrame();
        }
#endif

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

        if (premulMat != null && rtPremul != null && rt != null)
        {
            // ===== GPU 通道重排：一次全屏 Blit 代替 texW*texH 次 C# 循环 =====
            // Graphics.Blit 会把 rt 采样后写进 rtPremul，Shader 里只做 BGR 重排
            // （RT 里已经是预乘 alpha，不能再乘一次），输出的显存字节序已经是 DIB 需要的 BGRA，
            // CPU 侧只剩纯内存拷贝。
            var prevActive = RenderTexture.active;
            Graphics.Blit(rt, rtPremul, premulMat);

            RenderTexture.active = rtPremul;
            readback.ReadPixels(new Rect(0, 0, texW, texH), 0, 0);
            RenderTexture.active = prevActive;

            // ReadPixels 之后数据已在 CPU 侧，直接整块搬进托管数组（一次 memcpy），
            // 不再逐像素做乘法和通道交换。
            readback.GetRawTextureData<byte>().CopyTo(bgra);
        }
        else
        {
            // ===== 兜底：找不到 Shader 时完全保持原来的 CPU 路径 =====
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
        }

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

        // 置顶自愈：分辨率切换/窗口重建/Unity 自身的窗口设置都可能把 WS_EX_TOPMOST 清掉，
        // 这里只在“位丢了”时补一次 SetWindowPos，不会每帧和别的窗口抢 z-order。
        if (topMost && (ex & (int)WS_EX_TOPMOST) == 0)
        {
            SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
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

    // ==================== 拖动窗口 ====================
    void HandleWindowDrag()
    {
        if (!dragWindowAnywhere) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (IsPointerOverUI()) return;

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

        float newScale = Mathf.Clamp(windowScale + pendingZoomScroll * ZoomStep, minWindowScale, maxWindowScale);
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
        windowScale = Mathf.Clamp(scale, minWindowScale, maxWindowScale);
#if !UNITY_EDITOR
        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam == null || baseOrthoSize <= 0f) return false;

        cam.orthographicSize = baseOrthoSize / Mathf.Max(0.01f, windowScale);

        foreach (Canvas c in cachedCanvases)
        {
            if (c == null || !c.isRootCanvas) continue;
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler == null) continue;
            float targetSF = CanvasBaseScaleFactor(scaler.referenceResolution)
                             * Mathf.Max(0.01f, windowScale);
            if (!Mathf.Approximately(scaler.scaleFactor, targetSF))
                scaler.scaleFactor = targetSF;
        }
#endif
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
#if !UNITY_EDITOR
        windowScale = 1f;
        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam != null && baseOrthoSize > 0f) cam.orthographicSize = baseOrthoSize;
#endif
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
            SetWindowScale(minWindowScale);

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
            RECT wa = default;
            if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref wa, 0))
            {
                int w = fitW > 0 ? fitW : 200;
                int h = fitH > 0 ? fitH : 200;
                x = Mathf.Clamp(x, wa.Left, Mathf.Max(wa.Left, wa.Right - w));
                y = Mathf.Clamp(y, wa.Top, Mathf.Max(wa.Top, wa.Bottom - h));
            }
            SetWindowPos(hwnd, IntPtr.Zero, (int)x, (int)y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
#endif
    }
}
