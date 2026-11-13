using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DeskPet
{
    /// <summary>
    /// 编辑器桌宠：一个独立悬浮的小窗口，宠物自己在窗口里溜达 / 待机。
    /// 纯 EditorWindow 实现，不会进入运行时场景，也不会被打进包里。
    ///
    /// 打开方式：菜单 Tools > 桌宠 > 打开桌宠
    /// </summary>
    public class DeskPetWindow : EditorWindow
    {
        // ---------------- 菜单入口 ----------------

        [MenuItem("Tools/桌宠/打开桌宠", false, 20)]
        static void OpenFromMenu()
        {
            Open();
        }

        [MenuItem("Tools/桌宠/收起或展开", false, 21)]
        static void ToggleCollapseFromMenu()
        {
            if (Active == null) Open();
            if (Active != null) Active.SetCollapsed(!Active._collapsed);
        }

        [MenuItem("Tools/桌宠/更换形象（贴图槽）", false, 22)]
        static void OpenSpriteSlotFromMenu()
        {
            Open();
            if (Active == null) return;
            Active.SetCollapsed(false);
            Active._showSettings = true;
            Active.Repaint();
        }

        public static DeskPetWindow Open()
        {
            var win = GetWindow<DeskPetWindow>(true, "桌宠", false);
            win.ApplySizeConstraints(win._collapsed);
            win.Show();
            win.Repaint();
            return win;
        }

        /// <summary>当前打开着的桌宠窗口（没有则为 null）。</summary>
        public static DeskPetWindow Active { get; private set; }

        /// <summary>统一提示：Console + 窗口内飘一条通知。</summary>
        internal static void Notify(string message, bool warning)
        {
            if (warning) Debug.LogWarning("[桌宠] " + message);
            else Debug.Log("[桌宠] " + message);
            if (Active != null) Active.ShowNotification(new GUIContent(message));
        }

        // ---------------- 持久化键 ----------------

        const string KeyPosX = "DeskPet.PosX";
        const string KeyPosY = "DeskPet.PosY";
        const string KeySizeW = "DeskPet.SizeW";
        const string KeySizeH = "DeskPet.SizeH";
        const string KeyCollapsed = "DeskPet.Collapsed";
        const string KeySpriteGuid = "DeskPet.SpriteGuid";

        // ---------------- 尺寸常量 ----------------

        const float MinWidth = 190f;
        const float MinHeight = 130f;
        const float DefaultWidth = 268f;
        const float DefaultHeight = 200f;
        const float HeaderHeight = 20f;
        const float CollapsedSize = 96f;
        const float PetSizeExpanded = 56f;
        const float PetSizeCollapsed = 42f;
        const float SettingsHeight = 54f;

        enum PetState
        {
            Idle,
            Walk
        }

        // ---------------- 运行状态 ----------------

        PetState _state = PetState.Idle;
        float _stateTimer;
        Vector2 _petCenter;
        Vector2 _petVelocity;
        float _facing = 1f;
        float _bobPhase;
        bool _blinking;
        float _blinkTimer;
        bool _hoverPet;
        bool _collapsed;
        bool _showSettings;
        string _spriteGuid = string.Empty;

        Rect _lastTrackedRect;
        float _posDirtySince = -1f;
        float _lastTick;
        float _repaintAcc;
        Vector2 _expandedSize = new Vector2(DefaultWidth, DefaultHeight);

        // ---------------- 生命周期 ----------------

        void OnEnable()
        {
            Active = this;
            titleContent = new GUIContent("桌宠");
            wantsMouseMove = true;

            LoadPrefs();
            ApplyRestoredWindowRect();
            ResetPetToRoomCenter();

            _lastTick = (float)EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            // 首次创建窗口时 Unity 可能会自己摆一次位置，延后一帧再确认一次我们的记录值
            EditorApplication.delayCall += ReapplyRestoredWindowRect;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.delayCall -= ReapplyRestoredWindowRect;
            SavePositionNow();
            if (Active == this) Active = null;
        }

        // ---------------- 位置 / 状态 ----------------

        void LoadPrefs()
        {
            _collapsed = EditorPrefs.GetBool(KeyCollapsed, false);
            _spriteGuid = EditorPrefs.GetString(KeySpriteGuid, string.Empty);

            float w = EditorPrefs.GetFloat(KeySizeW, DefaultWidth);
            float h = EditorPrefs.GetFloat(KeySizeH, DefaultHeight);
            _expandedSize = new Vector2(Mathf.Max(MinWidth, w), Mathf.Max(MinHeight, h));
        }

        void ReapplyRestoredWindowRect()
        {
            if (this == null) return;
            ApplyRestoredWindowRect();
            Repaint();
        }

        void ApplyRestoredWindowRect()
        {
            Rect p = position;
            if (EditorPrefs.HasKey(KeyPosX) && EditorPrefs.HasKey(KeyPosY))
            {
                p.x = EditorPrefs.GetFloat(KeyPosX, p.x);
                p.y = EditorPrefs.GetFloat(KeyPosY, p.y);
            }
            p.width = _collapsed ? CollapsedSize : _expandedSize.x;
            p.height = _collapsed ? CollapsedSize : _expandedSize.y;

            ApplySizeConstraints(_collapsed);
            position = p;

            _lastTrackedRect = p;
        }

        void ApplySizeConstraints(bool collapsed)
        {
            if (collapsed)
            {
                minSize = new Vector2(CollapsedSize, CollapsedSize);
                maxSize = new Vector2(CollapsedSize, CollapsedSize);
            }
            else
            {
                minSize = new Vector2(MinWidth, MinHeight);
                maxSize = new Vector2(4000f, 4000f);
            }
        }

        void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed) return;

            Rect p = position;
            if (collapsed)
            {
                _expandedSize = new Vector2(Mathf.Max(MinWidth, p.width), Mathf.Max(MinHeight, p.height));
                EditorPrefs.SetFloat(KeySizeW, _expandedSize.x);
                EditorPrefs.SetFloat(KeySizeH, _expandedSize.y);
                _showSettings = false;
            }

            _collapsed = collapsed;
            p.width = collapsed ? CollapsedSize : _expandedSize.x;
            p.height = collapsed ? CollapsedSize : _expandedSize.y;

            ApplySizeConstraints(collapsed);
            position = p;

            _lastTrackedRect = p;
            EditorPrefs.SetBool(KeyCollapsed, collapsed);
            ResetPetToRoomCenter();
            Repaint();
        }

        void ResetPosition()
        {
            Rect p = position;
            p.x = 160f;
            p.y = 180f;
            p.width = _collapsed ? CollapsedSize : _expandedSize.x;
            p.height = _collapsed ? CollapsedSize : _expandedSize.y;
            position = p;
            _lastTrackedRect = p;
            EditorPrefs.DeleteKey(KeyPosX);
            EditorPrefs.DeleteKey(KeyPosY);
            Debug.Log("[桌宠] 窗口位置已重置。");
        }

        void SavePositionNow()
        {
            Rect p = position;
            EditorPrefs.SetFloat(KeyPosX, p.x);
            EditorPrefs.SetFloat(KeyPosY, p.y);
            if (!_collapsed)
            {
                _expandedSize = new Vector2(p.width, p.height);
                EditorPrefs.SetFloat(KeySizeW, p.width);
                EditorPrefs.SetFloat(KeySizeH, p.height);
            }
            EditorPrefs.SetBool(KeyCollapsed, _collapsed);
            _posDirtySince = -1f;
        }

        /// <summary>用户拖动 / 缩放窗口后，等手停下来再写盘。</summary>
        void TrackWindowPosition(float now)
        {
            Rect p = position;
            bool changed =
                !Mathf.Approximately(p.x, _lastTrackedRect.x) ||
                !Mathf.Approximately(p.y, _lastTrackedRect.y) ||
                (!_collapsed && (!Mathf.Approximately(p.width, _lastTrackedRect.width) ||
                                 !Mathf.Approximately(p.height, _lastTrackedRect.height)));

            if (changed)
            {
                _lastTrackedRect = p;
                _posDirtySince = now;
                return;
            }

            if (_posDirtySince < 0f || now - _posDirtySince < 0.6f) return;
            SavePositionNow();
        }

        // ---------------- 每帧模拟 ----------------

        void OnEditorUpdate()
        {
            float now = (float)EditorApplication.timeSinceStartup;
            float dt = now - _lastTick;
            _lastTick = now;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);

            StepPet(dt);
            TrackWindowPosition(now);

            _repaintAcc += dt;
            if (_repaintAcc < 1f / 30f) return;
            _repaintAcc = 0f;
            Repaint();
        }

        void StepPet(float dt)
        {
            Rect room = RoomRect;
            float half = PetPixelSize(room) * 0.5f;
            float minX = room.x + half;
            float maxX = room.xMax - half;
            float minY = room.y + half;
            float maxY = room.yMax - half;

            if (maxX < minX) minX = maxX = room.center.x;
            if (maxY < minY) minY = maxY = room.center.y;

            // 鼠标停在宠物身上时，它会停下来看着你，方便点开插件菜单
            if (_hoverPet)
            {
                _state = PetState.Idle;
                _petVelocity = Vector2.zero;
                _stateTimer = Mathf.Max(_stateTimer, 0.6f);
            }

            _stateTimer -= dt;
            if (_stateTimer <= 0f)
            {
                if (_state == PetState.Idle) StartWalking();
                else StartIdle();
            }

            if (_state == PetState.Walk)
            {
                _petCenter += _petVelocity * dt;

                if (_petCenter.x <= minX) { _petCenter.x = minX; _petVelocity.x = Mathf.Abs(_petVelocity.x); }
                if (_petCenter.x >= maxX) { _petCenter.x = maxX; _petVelocity.x = -Mathf.Abs(_petVelocity.x); }
                if (_petCenter.y <= minY) { _petCenter.y = minY; _petVelocity.y = Mathf.Abs(_petVelocity.y); }
                if (_petCenter.y >= maxY) { _petCenter.y = maxY; _petVelocity.y = -Mathf.Abs(_petVelocity.y); }

                if (Mathf.Abs(_petVelocity.x) > 0.5f) _facing = Mathf.Sign(_petVelocity.x);
            }

            _petCenter.x = Mathf.Clamp(_petCenter.x, minX, maxX);
            _petCenter.y = Mathf.Clamp(_petCenter.y, minY, maxY);

            _bobPhase += dt * (_state == PetState.Walk ? 8.5f : 2.0f);
            if (_bobPhase > 5000f) _bobPhase -= 5000f;

            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinking = !_blinking;
                _blinkTimer = _blinking ? 0.12f : UnityEngine.Random.Range(2f, 5.5f);
            }
        }

        void StartWalking()
        {
            _state = PetState.Walk;
            _stateTimer = UnityEngine.Random.Range(1.4f, 3.2f);

            float dirX = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            float dirY = UnityEngine.Random.Range(-0.45f, 0.45f);
            float speed = UnityEngine.Random.Range(12f, 24f);
            _petVelocity = new Vector2(dirX, dirY).normalized * speed;

            if (Mathf.Abs(_petVelocity.x) > 0.5f) _facing = Mathf.Sign(_petVelocity.x);
        }

        void StartIdle()
        {
            _state = PetState.Idle;
            _stateTimer = UnityEngine.Random.Range(1.6f, 4.2f);
            _petVelocity = Vector2.zero;
        }

        void ResetPetToRoomCenter()
        {
            Rect room = RoomRect;
            _petCenter = room.center;
            _petVelocity = Vector2.zero;
            _state = PetState.Idle;
            _stateTimer = UnityEngine.Random.Range(1f, 2.5f);
        }

        // ---------------- 布局 ----------------

        /// <summary>窗口内容区（去掉标题栏和底部留白）。</summary>
        Rect ContentRect
        {
            get
            {
                float top = _collapsed ? 0f : HeaderHeight;
                float bottomPadding = _collapsed ? 4f : 22f;
                float width = Mathf.Max(8f, position.width - 8f);
                float height = Mathf.Max(8f, position.height - top - bottomPadding);
                return new Rect(4f, top + 2f, width, height);
            }
        }

        /// <summary>宠物活动区域（展开形象设置时让出底部面板）。</summary>
        Rect RoomRect
        {
            get
            {
                Rect r = ContentRect;
                if (!_collapsed && _showSettings)
                    r.height = Mathf.Max(8f, r.height - SettingsHeight);
                return r;
            }
        }

        float PetPixelSize(Rect room)
        {
            float want = _collapsed ? PetSizeCollapsed : PetSizeExpanded;
            float cap = Mathf.Min(room.width, room.height) * 0.68f;
            return Mathf.Max(14f, Mathf.Min(want, cap));
        }

        Rect PetRect(Rect room)
        {
            float size = PetPixelSize(room);
            float bob = _state == PetState.Walk
                ? Mathf.Abs(Mathf.Sin(_bobPhase)) * 3f
                : (Mathf.Sin(_bobPhase) + 1f) * 0.9f;

            return new Rect(
                _petCenter.x - size * 0.5f,
                _petCenter.y - size * 0.5f - bob,
                size,
                size);
        }

        static Rect FitBottom(Rect area, float aspect)
        {
            if (aspect <= 0f) aspect = 1f;

            float w = area.width;
            float h = w / aspect;
            if (h > area.height)
            {
                h = area.height;
                w = h * aspect;
            }

            return new Rect(area.center.x - w * 0.5f, area.yMax - h, w, h);
        }

        // ---------------- 绘制 ----------------

        void OnGUI()
        {
            if (Event.current.type == EventType.Layout) return;

            Rect room = RoomRect;
            Rect petRect = PetRect(room);
            _hoverPet = petRect.Contains(Event.current.mousePosition);

            HandleDragAndDrop();
            HandleInput(petRect);

            // 背景
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.18f, 0.20f)
                : new Color(0.90f, 0.91f, 0.93f);
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), background);

            if (!_collapsed)
            {
                DrawHeader();
                EditorGUI.DrawRect(room, EditorGUIUtility.isProSkin
                    ? new Color(0.13f, 0.14f, 0.16f)
                    : new Color(0.96f, 0.97f, 0.98f));
                DrawFloor(room);
            }

            DrawPet(room, petRect);

            if (!_collapsed && _showSettings) DrawSettings();

            if (_collapsed && room.Contains(Event.current.mousePosition))
            {
                if (GUI.Button(new Rect(position.width - 18f, 2f, 16f, 16f), "+", EditorStyles.miniButton))
                    SetCollapsed(false);
            }
        }

        void DrawHeader()
        {
            var header = new Rect(0f, 0f, position.width, HeaderHeight);
            EditorGUI.DrawRect(header, EditorGUIUtility.isProSkin
                ? new Color(0.23f, 0.24f, 0.26f)
                : new Color(0.80f, 0.81f, 0.83f));

            GUI.Label(new Rect(6f, 2f, position.width - 72f, 16f), "桌宠", EditorStyles.miniBoldLabel);

            float x = position.width - 22f;
            if (GUI.Button(new Rect(x, 2f, 18f, 16f), "×", EditorStyles.miniButton)) Close();
            x -= 20f;
            if (GUI.Button(new Rect(x, 2f, 18f, 16f), "—", EditorStyles.miniButton)) SetCollapsed(true);
            x -= 20f;
            if (GUI.Button(new Rect(x, 2f, 18f, 16f), "设", EditorStyles.miniButton))
            {
                _showSettings = !_showSettings;
                Repaint();
            }
        }

        void DrawFloor(Rect room)
        {
            Color floor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.10f)
                : new Color(0f, 0f, 0f, 0.10f);
            EditorGUI.DrawRect(new Rect(room.x, room.yMax - 1f, room.width, 1f), floor);
        }

        void DrawPet(Rect room, Rect petRect)
        {
            Texture2D texture;
            Rect coords;
            bool custom = DeskPetArt.TryGetCustom(_spriteGuid, out texture, out coords);
            if (!custom)
            {
                texture = DeskPetArt.Placeholder;
                coords = new Rect(0f, 0f, 1f, 1f);
            }

            float aspect = (texture.width * coords.width) / Mathf.Max(1f, texture.height * coords.height);
            Rect fitted = FitBottom(petRect, aspect);

            Matrix4x4 oldMatrix = GUI.matrix;
            if (_facing < 0f) GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), petRect.center);
            GUI.DrawTextureWithTexCoords(fitted, texture, coords, true);
            GUI.matrix = oldMatrix;

            // 命中区域（自绘贴图时也保证可点）
            if (_hoverPet)
                EditorGUI.DrawRect(new Rect(fitted.x, fitted.yMax - 1f, fitted.width, 1f),
                    new Color(1f, 1f, 1f, 0.35f));

            // 占位形象自己眨眼（自定义贴图不动，避免破坏用户美术）
            if (!custom && _blinking) DrawBlink(fitted);

            if (_hoverPet)
            {
                GUI.Label(new Rect(room.x, room.y - 1f, room.width, 16f),
                    "点我打开插件功能", EditorStyles.centeredGreyMiniLabel);
            }
        }

        void DrawBlink(Rect fitted)
        {
            Color ink = new Color(0.17f, 0.13f, 0.12f);
            float lineHeight = Mathf.Max(1.5f, fitted.height * 0.022f);
            float lineWidth = fitted.width * 0.115f;

            for (int side = -1; side <= 1; side += 2)
            {
                float cx = fitted.x + (side * 0.27f + 1f) * 0.5f * fitted.width;
                float cy = fitted.y + (0.10f + 1f) * 0.5f * fitted.height;
                EditorGUI.DrawRect(new Rect(cx - lineWidth * 0.5f, cy, lineWidth, lineHeight), ink);
            }
        }

        void DrawSettings()
        {
            Rect content = ContentRect;
            var panel = new Rect(content.x, content.yMax - SettingsHeight, content.width, SettingsHeight);
            EditorGUI.DrawRect(panel, EditorGUIUtility.isProSkin
                ? new Color(0.08f, 0.09f, 0.10f)
                : new Color(0.86f, 0.87f, 0.89f));

            UnityEngine.Object current = DeskPetArt.GetCustomObject(_spriteGuid);

            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 32f;
            UnityEngine.Object picked = EditorGUI.ObjectField(
                new Rect(panel.x + 6f, panel.y + 5f, panel.width - 12f, 18f),
                "形象", current, typeof(UnityEngine.Object), false);
            EditorGUIUtility.labelWidth = oldLabelWidth;

            if (picked != current) SetSpriteObject(picked);

            if (GUI.Button(new Rect(panel.x + 6f, panel.y + 28f, 92f, 18f), "清除自定义贴图"))
                SetSpriteObject(null);

            GUI.Label(new Rect(panel.x + 102f, panel.y + 30f, panel.width - 108f, 16f),
                "也可以直接把 PNG 拖进窗口", EditorStyles.miniLabel);
        }

        // ---------------- 交互 ----------------

        void HandleInput(Rect petRect)
        {
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 && petRect.Contains(e.mousePosition))
            {
                DeskPetPluginBridge.ShowMenu();
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 1)
            {
                BuildContextMenu().ShowAsContext();
                e.Use();
            }
        }

        GenericMenu BuildContextMenu()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent(_collapsed ? "展开" : "收起"), false, () => SetCollapsed(!_collapsed));
            menu.AddItem(new GUIContent(_showSettings ? "隐藏形象设置" : "形象设置（更换贴图）"), false, () =>
            {
                SetCollapsed(false);
                _showSettings = !_showSettings;
                Repaint();
            });
            menu.AddItem(new GUIContent("重置窗口位置"), false, ResetPosition);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("插件功能…"), false, DeskPetPluginBridge.ShowMenu);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("关闭桌宠"), false, Close);

            return menu;
        }

        void HandleDragAndDrop()
        {
            Event e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            string guid = GetDraggedImageGuid();
            if (string.IsNullOrEmpty(guid)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SetSpriteGuid(guid);
                if (_collapsed) SetCollapsed(false);
                _showSettings = true;
                Notify("已更换桌宠形象", false);
            }
            e.Use();
        }

        static string GetDraggedImageGuid()
        {
            string[] paths = DragAndDrop.paths;
            if (paths == null) return null;

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) continue;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                switch (ext)
                {
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    case ".tga":
                    case ".psd":
                    case ".gif":
                    case ".bmp":
                    case ".tif":
                    case ".tiff":
                        return AssetDatabase.AssetPathToGUID(path);
                }
            }
            return null;
        }

        void SetSpriteObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                SetSpriteGuid(string.Empty);
                return;
            }

            if (!(obj is Sprite) && !(obj is Texture2D))
            {
                Notify("请选择 Sprite 或 Texture2D 资源", true);
                return;
            }

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
            {
                Notify("请使用 Assets 目录下的贴图资源", true);
                return;
            }

            SetSpriteGuid(AssetDatabase.AssetPathToGUID(path));
        }

        void SetSpriteGuid(string guid)
        {
            _spriteGuid = guid ?? string.Empty;
            EditorPrefs.SetString(KeySpriteGuid, _spriteGuid);
            Repaint();
        }
    }
}
