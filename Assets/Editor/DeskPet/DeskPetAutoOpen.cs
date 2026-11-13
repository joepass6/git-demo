using UnityEditor;
using UnityEngine;

namespace DeskPet
{
    /// <summary>
    /// 工程加载后自动弹出桌宠窗口。
    /// 每个编辑器会话只弹一次：脚本重新编译（domain reload）不会反复弹窗。
    /// 可在菜单 Tools > 桌宠 > 启动时自动打开 里关掉。
    /// </summary>
    [InitializeOnLoad]
    internal static class DeskPetAutoOpen
    {
        const string PrefAutoOpen = "DeskPet.AutoOpen";
        const string SessionKey = "DeskPet.AutoOpenedThisSession";
        const string MenuToggle = "Tools/桌宠/启动时自动打开";

        static DeskPetAutoOpen()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (!EditorPrefs.GetBool(PrefAutoOpen, true)) return;

            // 等编辑器界面初始化完成后再开窗口
            EditorApplication.delayCall += OpenOnce;
        }

        static void OpenOnce()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (!EditorPrefs.GetBool(PrefAutoOpen, true)) return;

            SessionState.SetBool(SessionKey, true);
            DeskPetWindow.Open();
            Debug.Log("[桌宠] 已随工程自动打开（Tools/桌宠 可关闭自动弹出）");
        }

        [MenuItem(MenuToggle, true)]
        static bool ToggleAutoOpenValidate()
        {
            Menu.SetChecked(MenuToggle, EditorPrefs.GetBool(PrefAutoOpen, true));
            return true;
        }

        [MenuItem(MenuToggle, false, 40)]
        static void ToggleAutoOpen()
        {
            bool value = !EditorPrefs.GetBool(PrefAutoOpen, true);
            EditorPrefs.SetBool(PrefAutoOpen, value);
            Debug.Log("[桌宠] 启动时自动打开：" + (value ? "开" : "关"));
        }
    }
}
