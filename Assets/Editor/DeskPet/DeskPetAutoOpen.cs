using UnityEditor;
using UnityEngine;

namespace DeskPet
{
    /// <summary>
    /// 工程加载后自动弹出桌宠。
    /// 每个编辑器会话只弹一次；脚本重新编译（domain reload）时只负责把之前开着的桌宠恢复回来。
    /// 可在菜单 Tools > 桌宠 > 启动时自动打开 里关掉。
    /// </summary>
    [InitializeOnLoad]
    internal static class DeskPetAutoOpen
    {
        const string PrefAutoOpen = "DeskPet.AutoOpen";
        const string SessionKey = "DeskPet.Native.AutoOpenedThisSession";
        const string MenuToggle = "Tools/桌宠/启动时自动打开";

        enum Pending { None, AutoOpen, Restore }

        static Pending _pending;

        static DeskPetAutoOpen()
        {
            // 原生窗口用的是托管窗口过程，domain reload 后会失效，必须提前销毁、之后重建
            AssemblyReloadEvents.beforeAssemblyReload -= DeskPetNative.PrepareForReload;
            AssemblyReloadEvents.beforeAssemblyReload += DeskPetNative.PrepareForReload;
            EditorApplication.quitting -= DeskPetNative.Close;
            EditorApplication.quitting += DeskPetNative.Close;

            if (SessionState.GetBool(DeskPetNative.SessionOpenState, false))
                _pending = Pending.Restore;
            else if (!SessionState.GetBool(SessionKey, false) && EditorPrefs.GetBool(PrefAutoOpen, true))
                _pending = Pending.AutoOpen;

            // 注意：启动阶段 EditorApplication.delayCall 可能被丢掉，这里用 update 轮询更可靠
            if (_pending != Pending.None) EditorApplication.update += PollOpen;
        }

        static int _pollFrames;

        static void PollOpen()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _pollFrames = 0;
                return;
            }

            if (++_pollFrames < 3) return;

            EditorApplication.update -= PollOpen;
            Pending pending = _pending;
            _pending = Pending.None;

            if (pending == Pending.AutoOpen)
            {
                SessionState.SetBool(SessionKey, true);
                DeskPetNative.Open();
                Debug.Log("[桌宠] 已随工程自动打开（Tools/桌宠 可关闭自动弹出）");
            }
            else if (pending == Pending.Restore)
            {
                DeskPetNative.Open();
            }
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
