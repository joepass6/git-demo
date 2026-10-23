// Steam 成就封装（Steamworks.NET 2025.164.1）。
// 新版 SDK 中成就数据由 Steam 客户端在游戏进程启动前同步完成，
// SteamManager 初始化成功后即可直接解锁，无需 RequestCurrentStats/回调等待。
// 成就 API 名（下方常量）必须与 Steamworks 合作伙伴后台
// 「统计与成就 → 成就」里填写的 API 名称完全一致，否则解锁无效。
// Steam 不可用时所有调用安全 no-op，游戏照常纯本地运行。
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

public static class SteamAchievements
{
    // ==================== 成就 API 名（改这里要同步改 Steamworks 后台）====================

    public const string FirstCoral = "FIRST_CORAL";       // 放置第一个珊瑚
    public const string Corals10 = "CORALS_10";           // 累计放置 10 个珊瑚
    public const string FirstFish = "FIRST_FISH";         // 第一条鱼出现
    public const string FishTypes10 = "FISH_TYPES_10";    // 发现 10 种鱼
    public const string FishTypes30 = "FISH_TYPES_30";    // 发现 30 种鱼
    public const string FishTypes50 = "FISH_TYPES_50";    // 发现 50 种鱼
    public const string FirstSell = "FIRST_SELL";         // 第一次卖出珊瑚

    /// <summary>解锁一个成就（重复解锁安全；Steam 不可用时静默）。</summary>
    public static void Unlock(string achievementId)
    {
        if (string.IsNullOrEmpty(achievementId)) return;
#if !DISABLESTEAMWORKS
        if (!SteamManager.Initialized) return;

        if (SteamUserStats.GetAchievement(achievementId, out bool already) && already) return;

        if (SteamUserStats.SetAchievement(achievementId))
        {
            SteamUserStats.StoreStats();
            Debug.Log($"[Steam成就] 解锁: {achievementId}");
        }
        else
        {
            Debug.LogWarning($"[Steam成就] SetAchievement 失败（API名与后台不一致？）: {achievementId}");
        }
#endif
    }

    /// <summary>清除一个成就（仅开发调试用，正式游戏不要调用）。</summary>
    public static void ClearForDebug(string achievementId)
    {
#if !DISABLESTEAMWORKS
        if (!SteamManager.Initialized || string.IsNullOrEmpty(achievementId)) return;
        SteamUserStats.ClearAchievement(achievementId);
        SteamUserStats.StoreStats();
        Debug.Log($"[Steam成就] 调试清除: {achievementId}");
#endif
    }
}
