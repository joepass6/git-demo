using UnityEditor;
using UnityEngine;

namespace DeskPet
{
    /// <summary>
    /// 桌宠的形象设置面板。桌宠本体是一个原生透明窗口（DeskPetNative），
    /// 所以这里只放贴图槽和几个开关，不负责绘制宠物。
    /// </summary>
    internal class DeskPetSettingsWindow : EditorWindow
    {
        const string KeySpriteGuid = "DeskPet.SpriteGuid";

        /// <summary>当前贴图槽指向的资源 GUID（空 = 用代码生成的占位形象）。</summary>
        public static string SpriteGuid
        {
            get { return EditorPrefs.GetString(KeySpriteGuid, string.Empty); }
            set { EditorPrefs.SetString(KeySpriteGuid, value ?? string.Empty); }
        }

        public static void Open()
        {
            var win = GetWindow<DeskPetSettingsWindow>(true, "桌宠形象", true);
            win.minSize = new Vector2(330f, 168f);
            win.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("形象贴图", EditorStyles.boldLabel);

            UnityEngine.Object current = DeskPetArt.GetCustomObject(SpriteGuid);
            UnityEngine.Object next = EditorGUILayout.ObjectField("Sprite / PNG", current, typeof(UnityEngine.Object), false);
            if (next != current) ApplyObject(next);

            EditorGUILayout.LabelField(
                current == null ? "当前：默认猫猫动画（GIF 逐帧循环）" : "当前：" + current.name,
                EditorStyles.miniLabel);

            EditorGUILayout.Space(2f);
            if (GUILayout.Button("恢复默认猫猫动画")) ApplyObject(null);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("把 PNG / Sprite 拖到这个面板里会换成单帧形象；带透明通道的图效果最好。", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("重置桌宠位置")) DeskPetNative.ResetPosition();
                if (GUILayout.Button("打开桌宠")) DeskPetNative.Open();
                if (GUILayout.Button("关闭桌宠")) DeskPetNative.Close();
            }

            HandleDragAndDrop();
        }

        void ApplyObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                SpriteGuid = string.Empty;
                DeskPetNative.RepaintNow();
                return;
            }

            if (!(obj is Sprite) && !(obj is Texture2D))
            {
                DeskPetNative.Notify("只支持 Sprite 或 Texture2D（PNG/JPG 等图片）", true);
                return;
            }

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
            {
                DeskPetNative.Notify("请选择工程里的贴图资源", true);
                return;
            }

            // Sprite 和贴图是同一份资源的两个子资源，统一记资源路径的 GUID
            SpriteGuid = AssetDatabase.AssetPathToGUID(path);
            DeskPetNative.RepaintNow();
        }

        void HandleDragAndDrop()
        {
            Event e = Event.current;
            Rect area = position;
            if (!area.Contains(e.mousePosition)) return;

            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                bool valid = false;
                foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is Sprite || obj is Texture2D) { valid = true; break; }
                }

                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                if (valid && e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                    {
                        if (obj is Sprite || obj is Texture2D)
                        {
                            ApplyObject(obj);
                            break;
                        }
                    }
                    Repaint();
                }

                e.Use();
            }
        }
    }
}
