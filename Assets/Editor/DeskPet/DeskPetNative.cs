using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DeskPet
{
    /// <summary>
    /// 桌宠的真实渲染窗口：一个 Windows 分层窗口（WS_EX_LAYERED + UpdateLayeredWindow）。
    ///
    /// 为什么不用 EditorWindow 做透明：
    ///   EditorWindow 的内容由编辑器自己的 D3D 交换链渲染，alpha 恒为 1，DWM 拿不到逐像素 alpha；
    ///   用 LWA_COLORKEY 只有 1 位透明（边缘出彩边），而且在 DXGI flip-model 交换链上经常直接失效。
    ///   所以这里自建一个原生无边框窗口，自己用 GDI DIB 画宠物再交给 DWM 合成：
    ///   真正的逐像素透明，宠物之外的区域完全透明且鼠标可直接穿透。
    ///
    /// 安全性：
    ///   - 窗口是编辑器主窗口的 owned popup，不进任务栏、不抢焦点（WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE）；
    ///   - 脚本编译触发 domain reload 时，先销毁原生窗口，重载后再恢复，避免托管 WndProc 失效导致崩溃。
    /// </summary>
    internal static class DeskPetNative
    {
        // ---------------- 设计尺寸（按系统 DPI 缩放） ----------------
        const int DesignWidth = 260;
        const int DesignHeight = 180;
        const int DesignPet = 56;

        // ---------------- 持久化 ----------------
        const string KeyPosX = "DeskPet.Native.X";
        const string KeyPosY = "DeskPet.Native.Y";
        internal const string SessionOpenState = "DeskPet.Native.WasOpen";

        // 每次 domain reload 换一个窗口类名：避免旧的托管 WndProc 指针被复用
        static readonly string ClassName = "DeskPetFloatingWnd_" + Guid.NewGuid().ToString("N");

        // ---------------- 状态 ----------------

        static int _stageW = DesignWidth;
        static int _stageH = DesignHeight;
        static int _petPx = DesignPet;
        static int _petW = DesignPet;
        static int _petH = DesignPet;

        // 逐帧动画
        static DeskPetArt.PetFrame[] _frames;
        static int _frameIndex;
        static float _frameTimer;
        static bool _usingPlaceholder;

        static Vector2 _petCenter;
        static bool _blinking;
        static float _blinkTimer;
        static float _lastTick;
        static float _renderAcc;
        static bool _open;

        static IntPtr _hwnd = IntPtr.Zero;
        static int _winX, _winY;

        // GDI
        static IntPtr _memDc = IntPtr.Zero;
        static IntPtr _dib = IntPtr.Zero;
        static IntPtr _oldBitmap = IntPtr.Zero;
        static IntPtr _dibBits = IntPtr.Zero;
        static byte[] _canvas;

        // 拖拽
        static bool _dragging;
        static bool _dragMoved;
        static POINT _dragCursorStart;
        static int _dragWinX, _dragWinY;

        // ---------------- 菜单入口 ----------------

        [MenuItem("Tools/桌宠/打开桌宠", false, 20)]
        static void MenuOpen()
        {
            Open();
        }

        [MenuItem("Tools/桌宠/关闭桌宠", false, 21)]
        static void MenuClose()
        {
            Close();
        }

        [MenuItem("Tools/桌宠/更换形象（贴图槽）", false, 22)]
        static void MenuSprite()
        {
            Open();
            DeskPetSettingsWindow.Open();
        }

        [MenuItem("Tools/桌宠/重置位置", false, 23)]
        static void MenuResetPosition()
        {
            ResetPosition();
        }

        // ---------------- 公开接口 ----------------

        public static bool IsOpen
        {
            get { return _open && _hwnd != IntPtr.Zero && IsWindow(_hwnd); }
        }

        public static void Notify(string message, bool warning)
        {
            if (warning) Debug.LogWarning("[桌宠] " + message);
            else Debug.Log("[桌宠] " + message);
        }

        public static void Open()
        {
            if (IsOpen)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                return;
            }

            if (!IsWindows)
            {
                Notify("桌宠的透明窗口目前只实现了 Windows 平台", true);
                return;
            }

            float scale = GetEditorMonitorScale();
            _stageW = Mathf.Max(DesignWidth, Mathf.RoundToInt(DesignWidth * scale));
            _stageH = Mathf.Max(DesignHeight, Mathf.RoundToInt(DesignHeight * scale));
            _petPx = Mathf.Max(DesignPet, Mathf.RoundToInt(DesignPet * scale));

            EnsureClass();
            EnsureBuffers();
            ReloadFrames();

            LoadPosition();
            _petCenter = new Vector2(_stageW * 0.5f, _stageH * 0.62f);
            ClampCenter();
            _blinking = false;
            _blinkTimer = UnityEngine.Random.Range(2f, 4f);

            int exStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            _hwnd = CreateWindowEx(
                exStyle, ClassName, "桌宠", WS_POPUP,
                _winX, _winY, _stageW, _stageH,
                FindEditorWindow(), IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                Notify("创建桌宠窗口失败（Win32 错误 " + Marshal.GetLastWin32Error() + "）", true);
                ReleaseGdi();
                return;
            }

            RenderPose();
            PushToWindow();
            ShowWindow(_hwnd, SW_SHOWNA);
            // 浮在编辑器上面：编辑器重新激活时会把自己抬到前台，所以这里固定在顶层
            SetWindowPos(_hwnd, HWND_TOPMOST, _winX, _winY, _stageW, _stageH, SWP_NOACTIVATE);

            _open = true;
            SessionState.SetBool(SessionOpenState, true);
            _lastTick = (float)EditorApplication.timeSinceStartup;
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        public static void Close()
        {
            if (_hwnd != IntPtr.Zero)
            {
                SavePosition();
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            _open = false;
            SessionState.SetBool(SessionOpenState, false);
            EditorApplication.update -= OnUpdate;
            ReleaseGdi();
        }

        public static void ResetPosition()
        {
            EditorPrefs.DeleteKey(KeyPosX);
            EditorPrefs.DeleteKey(KeyPosY);
            _winX = 240;
            _winY = 240;
            ApplyWindowPosition();
            if (IsOpen) PushToWindow();
            Notify("桌宠位置已重置", false);
        }

        public static void OpenSettings()
        {
            DeskPetSettingsWindow.Open();
        }

        /// <summary>贴图槽改变后立刻重画一帧。</summary>
        internal static void RepaintNow()
        {
            if (!IsOpen) return;
            ReloadFrames();
            RenderPose();
            PushToWindow();
        }

        /// <summary>domain reload 前销毁原生窗口（托管 WndProc 马上会失效）。</summary>
        internal static void PrepareForReload()
        {
            if (_hwnd == IntPtr.Zero) return;

            SavePosition();
            EditorApplication.update -= OnUpdate;
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            _open = false;
            ReleaseGdi();
            UnregisterClass(ClassName, GetModuleHandle(null));
            // SessionOpenState 保持 true，重载完成后由 DeskPetAutoOpen 恢复
        }

        // ---------------- 每帧 ----------------

        static void OnUpdate()
        {
            if (!IsOpen)
            {
                Close();
                return;
            }

            float now = (float)EditorApplication.timeSinceStartup;
            float dt = now - _lastTick;
            _lastTick = now;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);

            StepPet(dt);

            _renderAcc += dt;
            if (_renderAcc < 1f / 30f) return;
            _renderAcc = 0f;

            RenderPose();
            PushToWindow();
        }

        /// <summary>贴图槽 / 动画切换后重新取帧，并按画面比例算宠物显示尺寸。</summary>
        static void ReloadFrames()
        {
            string guid = DeskPetSettingsWindow.SpriteGuid;
            bool custom, placeholder;
            _frames = DeskPetArt.GetFrames(guid, out custom, out placeholder);
            _usingPlaceholder = placeholder;
            _frameIndex = 0;
            _frameTimer = 0f;

            if (_frames == null || _frames.Length == 0)
            {
                _petW = _petH = _petPx;
                return;
            }

            int w = _frames[0].Width;
            int h = _frames[0].Height;

            if (_frames.Length > 1)
            {
                // 内置动画是一整幅画：按比例放到舞台里，尽量大但不溢出
                float fit = Mathf.Min(_stageW * 0.62f / w, _stageH * 0.60f / h);
                _petW = Mathf.Max(24, Mathf.RoundToInt(w * fit));
                _petH = Mathf.Max(24, Mathf.RoundToInt(h * fit));
            }
            else
            {
                // 单帧贴图：高度沿用设计尺寸，宽度按比例，避免拉伸
                _petH = _petPx;
                _petW = Mathf.Clamp(Mathf.RoundToInt(_petPx * ((float)w / h)), 24, _stageW - 24);
            }

            ClampCenter();
        }

        /// <summary>宠物原地不动，只推进逐帧动画和眨眼计时。</summary>
        static void StepPet(float dt)
        {
            StepAnimation(dt);

            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinking = !_blinking;
                _blinkTimer = _blinking ? 0.12f : UnityEngine.Random.Range(2f, 5.5f);
            }
        }

        /// <summary>宠物比窗口大时把绘制中心夹回窗口内，保证不溢出。</summary>
        static void ClampCenter()
        {
            float minX = _petW * 0.5f;
            float maxX = _stageW - _petW * 0.5f;
            float minY = _petH * 0.5f;
            float maxY = _stageH - _petH * 0.5f;
            if (minX > maxX) minX = maxX = _stageW * 0.5f;
            if (minY > maxY) minY = maxY = _stageH * 0.5f;

            _petCenter.x = Mathf.Clamp(_petCenter.x, minX, maxX);
            _petCenter.y = Mathf.Clamp(_petCenter.y, minY, maxY);
        }

        // ---------------- 绘制 ----------------

        static void RenderPose()
        {
            if (_canvas == null) EnsureBuffers();
            if (_frames == null || _frames.Length == 0) ReloadFrames();
            if (_frames == null || _frames.Length == 0) return;

            Array.Clear(_canvas, 0, _canvas.Length);

            DeskPetArt.PetFrame frame = _frames[_frameIndex % _frames.Length];
            Color32[] src = frame.Pixels;
            int sw = frame.Width;
            int sh = frame.Height;
            int size = _petW;

            int left = Mathf.RoundToInt(_petCenter.x - size * 0.5f);
            int top = Mathf.RoundToInt(_petCenter.y - _petH * 0.5f);

            for (int y = 0; y < _petH; y++)
            {
                int dy = top + y;
                if (dy < 0 || dy >= _stageH) continue;

                float v = (y + 0.5f) / _petH;          // 0 = 顶部
                float sy = (1f - v) * (sh - 1);        // 像素数组自下而上

                for (int x = 0; x < size; x++)
                {
                    int dx = left + x;
                    if (dx < 0 || dx >= _stageW) continue;

                    float u = (x + 0.5f) / size;
                    float sx = u * (sw - 1);

                    Color32 c = SampleBilinear(src, sw, sh, sx, sy);
                    if (c.a == 0) continue;

                    float a = c.a / 255f;
                    int i = (dy * _stageW + dx) * 4;
                    // 预乘 alpha，BGRA 顺序
                    _canvas[i + 0] = (byte)(c.b * a);
                    _canvas[i + 1] = (byte)(c.g * a);
                    _canvas[i + 2] = (byte)(c.r * a);
                    _canvas[i + 3] = c.a;
                }
            }

            // 只有占位形象才自己眨眼（内置动画 / 自定义美术不动）
            if (_usingPlaceholder && _blinking) DrawBlink(left, top, size);
        }

        /// <summary>按每帧时长推进动画（内置猫猫沿用 GIF 的 10fps）。</summary>
        static void StepAnimation(float dt)
        {
            if (_frames == null || _frames.Length <= 1) return;

            float delay = _frames[_frameIndex % _frames.Length].Delay;
            if (delay <= 0f) delay = 0.1f;

            _frameTimer += dt;
            while (_frameTimer >= delay)
            {
                _frameTimer -= delay;
                _frameIndex = (_frameIndex + 1) % _frames.Length;
                delay = _frames[_frameIndex].Delay;
                if (delay <= 0f) delay = 0.1f;
            }
        }

        static void DrawBlink(int left, int top, int size)
        {
            int lineH = Mathf.Max(1, Mathf.RoundToInt(size * 0.05f));
            int lineW = Mathf.Max(2, Mathf.RoundToInt(size * 0.115f));

            for (int side = -1; side <= 1; side += 2)
            {
                int cx = left + Mathf.RoundToInt((side * 0.27f + 1f) * 0.5f * size);
                // 眼睛在归一化坐标 y = +0.10（向上为正），换算成从上往下的行号
                int cy = top + Mathf.RoundToInt((1f - (0.10f + 1f) * 0.5f) * size);
                for (int y = 0; y < lineH; y++)
                {
                    int dy = cy + y;
                    if (dy < 0 || dy >= _stageH) continue;
                    for (int x = 0; x < lineW; x++)
                    {
                        int dx = cx - lineW / 2 + x;
                        if (dx < 0 || dx >= _stageW) continue;
                        int i = (dy * _stageW + dx) * 4;
                        _canvas[i + 0] = 0x1E;
                        _canvas[i + 1] = 0x1E;
                        _canvas[i + 2] = 0x22;
                        _canvas[i + 3] = 0xFF;
                    }
                }
            }
        }

        static Color32 SampleBilinear(Color32[] px, int w, int h, float x, float y)
        {
            int x0 = Mathf.Clamp((int)x, 0, w - 1);
            int y0 = Mathf.Clamp((int)y, 0, h - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1);
            int y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = Mathf.Clamp01(x - x0);
            float fy = Mathf.Clamp01(y - y0);

            Color32 c00 = px[y0 * w + x0];
            Color32 c10 = px[y0 * w + x1];
            Color32 c01 = px[y1 * w + x0];
            Color32 c11 = px[y1 * w + x1];

            float a = Mathf.Lerp(Mathf.Lerp(c00.a, c10.a, fx), Mathf.Lerp(c01.a, c11.a, fx), fy);
            float r = Mathf.Lerp(Mathf.Lerp(c00.r, c10.r, fx), Mathf.Lerp(c01.r, c11.r, fx), fy);
            float g = Mathf.Lerp(Mathf.Lerp(c00.g, c10.g, fx), Mathf.Lerp(c01.g, c11.g, fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(c00.b, c10.b, fx), Mathf.Lerp(c01.b, c11.b, fx), fy);

            return new Color32((byte)r, (byte)g, (byte)b, (byte)a);
        }

        // ---------------- GDI 资源 ----------------

        static void EnsureBuffers()
        {
            if (_memDc != IntPtr.Zero && _canvas != null && _canvas.Length == _stageW * _stageH * 4) return;
            ReleaseGdi();

            IntPtr screen = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(screen);
            ReleaseDC(IntPtr.Zero, screen);

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = _stageW;
            bmi.bmiHeader.biHeight = -_stageH; // 负数 = 自上而下
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            IntPtr bits;
            _dib = CreateDIBSection(_memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            _dibBits = bits;
            _oldBitmap = SelectObject(_memDc, _dib);
            _canvas = new byte[_stageW * _stageH * 4];
        }

        static void ReleaseGdi()
        {
            if (_memDc != IntPtr.Zero)
            {
                if (_oldBitmap != IntPtr.Zero) SelectObject(_memDc, _oldBitmap);
                if (_dib != IntPtr.Zero) DeleteObject(_dib);
                DeleteDC(_memDc);
            }
            _memDc = IntPtr.Zero;
            _dib = IntPtr.Zero;
            _oldBitmap = IntPtr.Zero;
            _dibBits = IntPtr.Zero;
            _canvas = null;
        }

        static void PushToWindow()
        {
            if (_hwnd == IntPtr.Zero || _dibBits == IntPtr.Zero || _canvas == null) return;

            Marshal.Copy(_canvas, 0, _dibBits, _canvas.Length);

            var dst = new POINT { X = _winX, Y = _winY };
            var size = new SIZE { cx = _stageW, cy = _stageH };
            var srcPoint = new POINT { X = 0, Y = 0 };
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1 // AC_SRC_ALPHA
            };

            if (!UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref dst, ref size, _memDc, ref srcPoint, 0, ref blend, ULW_ALPHA)
                && !_ulwWarned)
            {
                _ulwWarned = true;
                Notify("分层窗口绘制失败（Win32 错误 " + Marshal.GetLastWin32Error() + "）", true);
            }
        }

        static bool _ulwWarned;

        static void ApplyWindowPosition()
        {
            if (_hwnd == IntPtr.Zero) return;
            SetWindowPos(_hwnd, HWND_TOPMOST, _winX, _winY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // ---------------- 位置 ----------------

        static void LoadPosition()
        {
            _winX = EditorPrefs.GetInt(KeyPosX, 240);
            _winY = EditorPrefs.GetInt(KeyPosY, 240);

            // 兜底：避免窗口跑到屏幕外（用整个虚拟桌面，多显示器也能放在副屏上）
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (vw > 0 && vh > 0)
            {
                _winX = Mathf.Clamp(_winX, vx - _stageW + 80, vx + vw - 80);
                _winY = Mathf.Clamp(_winY, vy, vy + vh - 80);
            }
        }

        static void SavePosition()
        {
            EditorPrefs.SetInt(KeyPosX, _winX);
            EditorPrefs.SetInt(KeyPosY, _winY);
        }

        // ---------------- 窗口过程 ----------------

        static void StartDrag()
        {
            POINT p;
            GetCursorPos(out p);
            _dragCursorStart = p;
            _dragWinX = _winX;
            _dragWinY = _winY;
            _dragging = true;
            _dragMoved = false;
            SetCapture(_hwnd);
        }

        static void UpdateDrag()
        {
            POINT p;
            GetCursorPos(out p);
            int dx = p.X - _dragCursorStart.X;
            int dy = p.Y - _dragCursorStart.Y;

            if (!_dragMoved && Mathf.Abs(dx) + Mathf.Abs(dy) > 3) _dragMoved = true;
            if (!_dragMoved) return;

            _winX = _dragWinX + dx;
            _winY = _dragWinY + dy;
            PushToWindow();
        }

        static void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseCapture();

            if (_dragMoved) SavePosition();
            else EditorApplication.delayCall += DeskPetPluginBridge.ShowMenu;
        }

        static void ShowContextMenu()
        {
            EditorApplication.delayCall += () =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("形象设置…"), false, OpenSettings);
                menu.AddItem(new GUIContent("重置位置"), false, ResetPosition);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("关闭桌宠"), false, Close);
                menu.ShowAsContext();
            };
        }

        static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_LBUTTONDOWN:
                    StartDrag();
                    return IntPtr.Zero;

                case WM_MOUSEMOVE:
                    SetCursor(LoadCursor(IntPtr.Zero, IDC_HAND));
                    if (_dragging) UpdateDrag();
                    return IntPtr.Zero;

                case WM_LBUTTONUP:
                    EndDrag();
                    return IntPtr.Zero;

                case WM_RBUTTONUP:
                    ShowContextMenu();
                    return IntPtr.Zero;

                case WM_SETCURSOR:
                    SetCursor(LoadCursor(IntPtr.Zero, IDC_HAND));
                    return (IntPtr)1;

                case WM_DESTROY:
                    _open = false;
                    _hwnd = IntPtr.Zero;
                    return IntPtr.Zero;
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        static bool _classRegistered;
        static WndProcDelegate _wndProcDelegate;

        static void EnsureClass()
        {
            if (_classRegistered && _hwnd != IntPtr.Zero) return;

            _wndProcDelegate = WindowProc;

            var wc = new WNDCLASSEX();
            wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wc.style = 0;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            wc.cbClsExtra = 0;
            wc.cbWndExtra = 0;
            wc.hInstance = GetModuleHandle(null);
            wc.hIcon = IntPtr.Zero;
            wc.hCursor = IntPtr.Zero;
            wc.hbrBackground = IntPtr.Zero;
            wc.lpszMenuName = null;
            wc.lpszClassName = ClassName;
            wc.hIconSm = IntPtr.Zero;

            RegisterClassEx(ref wc);
            _classRegistered = true;
        }

        static IntPtr FindEditorWindow()
        {
            uint pid = (uint)Process.GetCurrentProcess().Id;
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;

            EnumWindows((h, l) =>
            {
                uint windowPid;
                GetWindowThreadProcessId(h, out windowPid);
                if (windowPid != pid || !IsWindowVisible(h)) return true;

                var title = new StringBuilder(512);
                GetWindowText(h, title, 512);
                if (title.ToString().IndexOf("Unity", StringComparison.OrdinalIgnoreCase) < 0) return true;

                RECT r;
                GetWindowRect(h, out r);
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = h;
                }
                return true;
            }, IntPtr.Zero);

            return best;
        }

        /// <summary>按编辑器所在显示器的 DPI 缩放（多显示器混合 DPI 时不会被放大）。</summary>
        static float GetEditorMonitorScale()
        {
            try
            {
                IntPtr editor = FindEditorWindow();
                IntPtr monitor = MonitorFromWindow(editor, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    uint dpiX, dpiY;
                    if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0 && dpiX > 0)
                        return Mathf.Clamp(dpiX / 96f, 1f, 3f);
                }
            }
            catch
            {
                // shcore 不可用时退回系统 DPI
            }

            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return 1f;
            int dpi = GetDeviceCaps(dc, LOGPIXELSX);
            ReleaseDC(IntPtr.Zero, dc);
            if (dpi <= 0) return 1f;
            return Mathf.Clamp(dpi / 96f, 1f, 3f);
        }

        static bool IsWindows
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                    case RuntimePlatform.WindowsPlayer:
                        return true;
                    default:
                        return false;
                }
            }
        }

        // ---------------- P/Invoke ----------------

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        const int WS_POPUP = unchecked((int)0x80000000);
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        const int SW_SHOWNA = 8;
        const uint ULW_ALPHA = 0x00000002;

        const uint WM_DESTROY = 0x0002;
        const uint WM_MOUSEMOVE = 0x0200;
        const uint WM_LBUTTONDOWN = 0x0201;
        const uint WM_LBUTTONUP = 0x0202;
        const uint WM_RBUTTONUP = 0x0205;
        const uint WM_SETCURSOR = 0x0020;

        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;
        const int LOGPIXELSX = 88;
        const uint MONITOR_DEFAULTTONEAREST = 2;
        const int MDT_EFFECTIVE_DPI = 0;
        static readonly IntPtr IDC_HAND = (IntPtr)32649;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);
        [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr dstDc, ref POINT dstPoint,
            ref SIZE size, IntPtr srcDc, ref POINT srcPoint, int colorKey, ref BLENDFUNCTION blend, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int index);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO info, uint usage,
            out IntPtr bits, IntPtr section, uint offset);
    }
}
