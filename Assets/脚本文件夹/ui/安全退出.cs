using System.Threading;
using UnityEngine;

/// <summary>
/// 退出守护：任何退出方式（设置里的退出按钮 / 窗口关闭 / Alt+F4 / 任务栏关闭）
/// 都会先进入 Unity 的 wantsToQuit，这里启动一个后台线程做“硬超时”：
/// 3 秒内进程还没退出就强制 Environment.Exit(0)，避免 Steam / 分层窗口清理把主线程卡死。
/// 存档在 OnApplicationQuit 阶段已经写完（且是原子写入），强制退出不会丢档。
/// </summary>
public static class 安全退出
{
    const int 超时毫秒 = 3000;
    static bool 已启动;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void 安装()
    {
        Application.wantsToQuit += 退出请求;
    }

    static bool 退出请求()
    {
#if !UNITY_EDITOR
        启动守护();
#endif
        return true;
    }

    public static void 启动守护()
    {
        if (已启动) return;
        已启动 = true;
        Debug.Log($"[退出] 已进入退出流程，启动 {超时毫秒}ms 强制退出守护");

#if !UNITY_EDITOR
        Thread t = new Thread(() =>
        {
            Thread.Sleep(超时毫秒);
            Debug.LogWarning($"[退出] 超过 {超时毫秒}ms 仍未退出，强制结束进程（存档已在退出前写好）");
            System.Environment.Exit(0);
        });
        t.IsBackground = true;
        t.Name = "退出守护";
        t.Start();
#endif
    }
}
