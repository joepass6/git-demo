using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DeskPet
{
    /// <summary>
    /// 把桌宠接到本工程里两个插件的能力上。菜单路径全部取自插件源码中的 MenuItem 特性，未做任何猜测：
    ///   SuperTiled2Unity：Editor/Config/SuperTiled2Unity_Config.cs 与 Editor/Importers/TilesetAtlasImporter.cs
    ///   UIEffectPlugin：插件里没有任何 MenuItem，因此改为按其公开组件类型提供“添加组件”入口
    /// </summary>
    internal static class DeskPetPluginBridge
    {
        // ---- SuperTiled2Unity：源码中真实存在的菜单路径 ----
        const string St2uExportPath = "Assets/Super Tiled2Unity/Export ST2U Asset...";
        const string St2uApplyDefaultsPath = "Assets/Super Tiled2Unity/Apply Default Settings to ST2U Assets";
        const string St2uTilesetAtlasPath = "Assets/Create/Super Tiled2Unity/Tileset Atlas";

        const string St2uFolder = "Assets/SuperTiled2Unity";
        const string UIEffectFolder = "Assets/UIEffectPlugin";

        // ---- UIEffectPlugin：命名空间 UIEffectPlugin 下的公开组件 ----
        static readonly string[] UIEffectComponents =
        {
            "UIFadeEffect",
            "UIScaleEffect",
            "UIMoveEffect",
            "UIRotateEffect",
            "UIShakeEffect",
            "UIGlowEffect",
            "UITypewriterEffect",
            "UIColorGradientEffect",
            "UIParticleEffect",
            "UIParticleLineEffect"
        };

        public static void ShowMenu()
        {
            var menu = new GenericMenu();

            // ---------- SuperTiled2Unity ----------
            menu.AddDisabledItem(new GUIContent("SuperTiled2Unity"));
            AddExecute(menu, "Export ST2U Asset...（需先选中 .st2u 资源）", St2uExportPath);
            AddExecute(menu, "Apply Default Settings to ST2U Assets", St2uApplyDefaultsPath);
            AddExecute(menu, "创建 Tileset Atlas", St2uTilesetAtlasPath);
            menu.AddItem(new GUIContent("定位 SuperTiled2Unity 目录"), false, () => PingFolder(St2uFolder));

            menu.AddSeparator("");

            // ---------- UIEffectPlugin ----------
            menu.AddDisabledItem(new GUIContent("UIEffectPlugin（该插件没有原生菜单项）"));

            GameObject target = Selection.activeGameObject;
            foreach (string name in UIEffectComponents)
            {
                string typeName = name;
                if (target == null)
                    menu.AddDisabledItem(new GUIContent("添加特效组件/" + typeName + "（先在 Hierarchy 选中对象）"));
                else
                    menu.AddItem(new GUIContent("添加特效组件/" + typeName), false, () => AddEffectComponent(typeName));
            }

            menu.AddItem(new GUIContent("打开插件说明 README"), false, OpenReadme);
            menu.AddItem(new GUIContent("定位 UIEffectPlugin 目录"), false, () => PingFolder(UIEffectFolder));

            menu.ShowAsContext();
        }

        static void AddExecute(GenericMenu menu, string label, string menuPath)
        {
            menu.AddItem(new GUIContent(label), false, () => Execute(menuPath));
        }

        static void Execute(string menuPath)
        {
            if (EditorApplication.ExecuteMenuItem(menuPath)) return;

            DeskPetWindow.Notify("菜单项当前不可用：" + menuPath + "（通常需要先在 Project 中选中对应资源）", true);
        }

        static void AddEffectComponent(string typeName)
        {
            GameObject target = Selection.activeGameObject;
            if (target == null)
            {
                DeskPetWindow.Notify("请先在 Hierarchy 中选中一个 GameObject", true);
                return;
            }

            Type type = FindType("UIEffectPlugin." + typeName);
            if (type == null)
            {
                DeskPetWindow.Notify("未找到类型 UIEffectPlugin." + typeName + "，请确认 UIEffectPlugin 已导入", true);
                return;
            }

            Undo.AddComponent(target, type);
            Debug.Log("[桌宠] 已把 " + typeName + " 添加到 " + target.name);
        }

        static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        static void OpenReadme()
        {
            string path = UIEffectFolder + "/README.md";
            if (!File.Exists(path))
            {
                DeskPetWindow.Notify("找不到 " + path, true);
                return;
            }
            EditorUtility.RevealInFinder(path);
        }

        static void PingFolder(string folder)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folder);
            if (asset == null)
            {
                DeskPetWindow.Notify("找不到目录 " + folder, true);
                return;
            }

            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }
    }
}
