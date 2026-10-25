// ============================================================
// 测试版专用脚本：所有鱼的宝物（掉落物库存）强制为 99999999
// 仅当打包时定义了 TEST_BUILD 宏才会编译；正式版（无此宏）
// 整个文件不参与编译，对线上逻辑零影响。
// ============================================================
#if TEST_BUILD
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class 测试版宝物启动器
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void 启动()
    {
        var go = new GameObject("[测试版]宝物数值覆盖器");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<测试版宝物覆盖器>();
    }
}

public class 测试版宝物覆盖器 : MonoBehaviour
{
    const int 测试宝物数值 = 99999999;

    float 下次覆盖时间;
    int 已执行次数;

    // DropItem._counts 私有静态库存字典：读它的键，兜住"鱼已掉落但UI面板从没打开过"的类型
    static readonly FieldInfo CountsField = typeof(DropItem).GetField(
        "_counts", BindingFlags.NonPublic | BindingFlags.Static);

    // 存档加载完成前不覆盖，避免存档值把我们压回去
    bool 存档已加载;

    void Start()
    {
        // 订阅存档加载事件：Load() 完成后立即执行第一次覆盖
        SaveSystem.OnLoadComplete -= On存档加载完成;
        SaveSystem.OnLoadComplete += On存档加载完成;

        // 兜底：如果存档文件不存在（全新游戏），SaveSystem.Load() 找不到文件不会触发事件，
        // 这里设一个超时保护
        下次覆盖时间 = 3f;
        Debug.Log("[测试版] 等待存档加载后开始覆盖所有鱼的宝物数值为 " + 测试宝物数值);
    }

    void OnDestroy()
    {
        SaveSystem.OnLoadComplete -= On存档加载完成;
    }

    void On存档加载完成(int itemTypeCount)
    {
        存档已加载 = true;
        // 收到事件后立即覆盖，不再等待
        下次覆盖时间 = 0f;
        Debug.Log("[测试版] 存档已加载，开始覆盖宝物数值");
    }

    void Update()
    {
        if (!存档已加载 && Time.time < 下次覆盖时间) return;

        // 超时保护：存档没加载也不等了（全新游戏可能没有存档）
        if (!存档已加载 && Time.time >= 下次覆盖时间)
        {
            存档已加载 = true;
            Debug.Log("[测试版] 存档加载超时，直接开始覆盖（全新游戏？）");
        }

        覆盖宝物数值();
        已执行次数++;

        // 前三次每 2 秒覆盖一轮，兜住晚注册/晚出现的宝物类型；
        // 之后每 10 秒补一次，让消耗掉的材料自动回满（测试用无限资源）
        下次覆盖时间 = Time.time + (已执行次数 < 3 ? 2f : 10f);
    }

    void 覆盖宝物数值()
    {
        var types = new HashSet<string>();

        // 1. 场景中所有计数器定义的宝物类型（含未激活面板，如各种"xx鱼宝"、珊瑚等）
        DropItemCounterUI[] counters = Resources.FindObjectsOfTypeAll<DropItemCounterUI>();
        foreach (DropItemCounterUI c in counters)
        {
            if (c == null || string.IsNullOrEmpty(c.itemType)) continue;
            if (!c.gameObject.scene.IsValid()) continue; // 排除 Project 里的预制体资源
            types.Add(c.itemType);
        }

        // 2. 运行时库存字典中已存在的类型
        if (CountsField != null)
        {
            var dict = CountsField.GetValue(null) as Dictionary<string, int>;
            if (dict != null)
            {
                foreach (string t in dict.Keys)
                {
                    if (!string.IsNullOrEmpty(t)) types.Add(t);
                }
            }
        }

        foreach (string t in types)
        {
            DropItem.SetCount(t, 测试宝物数值);
        }

        if (types.Count > 0 && 已执行次数 == 0)
        {
            Debug.Log("[测试版] 已把 " + types.Count + " 种宝物数量设置为 " + 测试宝物数值);
        }
    }
}
#endif
