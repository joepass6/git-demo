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
///   3. 相机渲到 RT → CPU 预乘 alpha 转 BGRA → ULW 每帧上屏
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
    public float maxWindowScale = 1f;
    float windowScale = 1f;

    [Header("窗口按钮")]
    public Button minimizeButton;

    [Header("画面居中")]
    public Transform centerTarget;
    public Vector2 centerOffset;

    [Header("窗口贴合场景")]
    public bool fitWindowToTarget = true;
    [Range(0.3f, 1f)] public float fitHeightRatio = 0.95f;
    public bool useManualFitSize = false;
    public Vector2 manualFitSize = new Vector2(7f, 10f);
    [Tooltip("自动贴合只统计塔主体周围这个半径内的 Sprite，排除远处大图")]
    public float fitRadius = 5f;

    [Header("窗口行为")]
    public bool topMost = true;

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
    const uint WS_EX_LAYERED = 0x00080000;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020, SWP_NOACTIVATE = 0x0010;
    const int  SW_MINIMIZE = 6;
    const uint WM_NCLBUTTONDOWN = 0xA1;
    static readonly IntPtr HTCAPTION = new IntPtr(2);
    const uint ULW_ALPHA = 2;

    IntPtr hwnd;
    bool transparentReady;
    RenderTexture rt;
    Texture2D readback;
    IntPtr memDC = IntPtr.Zero, dib = IntPtr.Zero, dibOld = IntPtr.Zero, dibBits = IntPtr.Zero;
    int texW, texH;
    byte[] bgra;

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
        foreach (CanvasScaler s in FindObjectsOfType<CanvasScaler>())
        {
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 1f; // 按高度匹配
        }
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
            foreach (Renderer r in sprites)
            {
                if (r == towerBody) continue;
                if (fitRadius > 0f && Vector3.Distance(r.bounds.center, towerC) > fitRadius) continue;
                b.Encapsulate(r.bounds);
                count++;
            }

            // 兜底：塔身边没内容 → 退回全场景
            if (b.size.y < 0.5f)
            {
                Debug.LogWarning("[桌面窗口] 塔身边内容过少，退回全场景包围盒");
                b = sprites[0].bounds;
                foreach (Renderer r in sprites) b.Encapsulate(r.bounds);
            }

            // 防退化钳制：宽高比超出 0.45~1.2 → 以塔群为中心把宽度撑回来
            if (b.size.y > 0.01f)
            {
                float rawAspect = b.size.x / b.size.y;
                float clampedAspect = Mathf.Clamp(rawAspect, 0.45f, 1.2f);
                if (!Mathf.Approximately(rawAspect, clampedAspect))
                {
                    b.size = new Vector3(b.size.y * clampedAspect, b.size.y, 1f);
                    Debug.Log($"[桌面窗口] 包围盒宽高比 {rawAspect:F2} 钳制为 {clampedAspect:F2}");
                }
            }

            Debug.Log($"[桌面窗口] 贴合: 塔主体={towerBody.name} ({towerBody.bounds.size.x:F1}x{towerBody.bounds.size.y:F1}), {count} 个 Sprite, 总包围盒 {b.size.x:F1}x{b.size.y:F1}");
        }

        lastFitBounds = b;
        hasFitBounds = true;

        // 相机对准包围盒中心，正交高度正好包住内容
        cam.transform.position = new Vector3(
            b.center.x + centerOffset.x,
            b.center.y + centerOffset.y,
            cam.transform.position.z);
        cam.orthographicSize = b.extents.y;
        baseOrthoSize = b.extents.y; // 记住基准值，缩放时用

        // 窗口宽高比 = 包围盒宽高比
#if !UNITY_EDITOR
        float boxAspect = b.size.x / b.size.y;
        int h = Mathf.Max(200, Mathf.RoundToInt((float)Display.main.systemHeight * fitHeightRatio));
        int w = Mathf.Max(200, Mathf.RoundToInt(h * boxAspect));
        ClampToWorkArea(ref w, ref h);
        fitW = w; fitH = h;
        windowScale = Mathf.Clamp(windowScale, minWindowScale, maxWindowScale);

        // 窗口物理尺寸固定不变，用 orthographicSize 做缩放
        Debug.Log($"[桌面窗口] 窗口贴合: 基础orthoSize={baseOrthoSize:F2}, 窗口={fitW}x{fitH}, 当前缩放=x{windowScale:F2}");
#endif
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
        ApplyTopMost();

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | (int)WS_EX_LAYERED);
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

        // scaleFactor 直接用 windowScale，初始 windowScale=1 → scaleFactor=1（和原来一致）
        // 缩小到 0.1 时 scaleFactor=0.1，UI 和场景一起缩小
        float targetScaleFactor = Mathf.Max(0.01f, windowScale);

        foreach (Canvas c in cachedCanvases)
        {
            if (c == null || !c.isRootCanvas) continue;

            // ScreenSpaceCamera：不变形，GraphicRaycaster 正常工作
            if (c.renderMode != RenderMode.ScreenSpaceCamera)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
            }
            c.worldCamera = cam;
            c.planeDistance = cam.farClipPlane * 0.9f;

            // CanvasScaler 切 ConstantPixelSize 模式，不会自动覆盖我们设的 scaleFactor
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.enabled = true;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = targetScaleFactor;
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

        Debug.Log($"[桌面窗口] Canvas 扫描: {rootCount} 个根 Canvas, 相机={cam.name}, texW/H={texW}x{texH}, scaleFactor={targetScaleFactor:F2}, windowScale={windowScale:F2}");
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

        CreateDib();
        transparentReady = true;

        // 初始缩放
        cam.orthographicSize = baseOrthoSize / Mathf.Max(0.01f, windowScale);

        Debug.Log($"[桌面窗口] ULW 已启动 ({texW}x{texH}), orthoSize={cam.orthographicSize:F2} (base={baseOrthoSize:F2}/scale={windowScale:F2})");
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
        transparentReady = false;
        DestroyDib();
        if (rt != null) { rt.Release(); rt = null; }
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

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0, 0, texW, texH), 0, 0);
        readback.Apply(false, false);
        RenderTexture.active = prevActive;

        // RGBA → 预乘 alpha 的 BGRA
        NativeArray<Color32> px = readback.GetRawTextureData<Color32>();
        int n = texW * texH;
        for (int i = 0; i < n; i++)
        {
            Color32 c = px[i];
            uint a = c.a;
            int o = i << 2;
            bgra[o]     = (byte)(c.b * a >> 8);
            bgra[o + 1] = (byte)(c.g * a >> 8);
            bgra[o + 2] = (byte)(c.r * a >> 8);
            bgra[o + 3] = (byte)a;
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
        pendingZoomScroll += scroll;
        zoomDebounceDeadline = Time.time + 0.15f;
        float newScale = Mathf.Clamp(windowScale + pendingZoomScroll * ZoomStep, minWindowScale, maxWindowScale);
        if (Mathf.Abs(newScale - windowScale) < 0.0001f) return;
        windowScale = newScale;
        pendingZoomScroll = 0f;
#endif
    }

    void ApplyZoomDebounce()
    {
#if !UNITY_EDITOR
        if (zoomDebounceDeadline <= 0f || Time.time < zoomDebounceDeadline) return;
        zoomDebounceDeadline = 0f;
        if (baseOrthoSize <= 0f) return;

        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam == null) return;

        float targetOrtho = baseOrthoSize / Mathf.Max(0.01f, windowScale);
        if (!Mathf.Approximately(cam.orthographicSize, targetOrtho))
        {
            cam.orthographicSize = targetOrtho;
        }

        // 同步 CanvasScaler.scaleFactor：UI 和场景一起缩放
        float targetScaleFactor = Mathf.Max(0.01f, windowScale);
        foreach (Canvas c in cachedCanvases)
        {
            if (c == null || !c.isRootCanvas) continue;
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler != null && !Mathf.Approximately(scaler.scaleFactor, targetScaleFactor))
            {
                scaler.scaleFactor = targetScaleFactor;
            }
        }

        Debug.Log($"[桌面窗口] 缩放: orthoSize={cam.orthographicSize:F2}, scaleFactor={targetScaleFactor:F3} (baseOrtho={baseOrthoSize:F2}/windowScale={windowScale:F2})");
#endif
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
        windowScale = Mathf.Clamp(scale, minWindowScale, maxWindowScale);
#if !UNITY_EDITOR
        Camera cam = zoomCamera != null ? zoomCamera : Camera.main;
        if (cam != null && baseOrthoSize > 0f) cam.orthographicSize = baseOrthoSize / Mathf.Max(0.01f, windowScale);
        float targetScaleFactor = Mathf.Max(0.01f, windowScale);
        foreach (Canvas c in cachedCanvases)
        {
            if (c == null || !c.isRootCanvas) continue;
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.scaleFactor = targetScaleFactor;
        }
#endif
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
            SetWindowPos(hwnd, IntPtr.Zero, (int)x, (int)y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
#endif
    }
}
