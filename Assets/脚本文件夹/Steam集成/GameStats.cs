using System.Collections.Generic;

/// <summary>
/// 游戏进度统计：Steam 成就的判定依据，同时随 save.json 持久化、随云存档跨设备同步。
/// 只统计玩家真实行为（真实放置/卖出、鱼真实出现）；读档重建珊瑚与鱼不计入，
/// 避免换设备/反复读档把累计数字刷高。
/// 统计重置不影响 Steam 端已解锁成就（账号级永久记录，重复解锁是幂等的）。
/// </summary>
public static class GameStats
{
    /// <summary>累计真实放置珊瑚次数（卖出不扣减）。</summary>
    public static int TotalCoralsPlaced { get; private set; }

    /// <summary>累计真实卖出珊瑚次数。</summary>
    public static int TotalCoralsSold { get; private set; }

    /// <summary>已发现的不同鱼名集合（按鱼运动.fishName 去重）。</summary>
    static readonly HashSet<string> s_seenFish = new HashSet<string>();

    /// <summary>已真实掉过宝的鱼名集合（图鉴印章显示用，按鱼名去重）。</summary>
    static readonly HashSet<string> s_droppedFish = new HashSet<string>();

    public static int SeenFishCount => s_seenFish.Count;

    /// <summary>
    /// 图鉴/统计数据发生真实变化时触发（发现新鱼、新掉宝、旧档补登）。
    /// SaveSystem 订阅后做防抖立即写盘，保证图鉴点亮后不依赖 60 秒定时/正常退出也不丢。
    /// 读档 Restore 不触发本事件。
    /// </summary>
    public static event System.Action OnCodexChanged;

    static void FireCodexChanged()
    {
        OnCodexChanged?.Invoke();
    }

    // ==================== 由 SaveSystem 读档后调用（恢复统计，不触发成就）====================

    public static void Restore(List<string> seenFish, List<string> droppedFish, int totalCoralsPlaced, int totalCoralsSold)
    {
        // 先清空鱼运动内存图鉴：手动读档时上一轮的登记（可能含污染）必须能被空列表真正压掉，
        // 随后按存档列表单向同步；珊瑚重建生成鱼时会重新真实登记
        鱼运动.ResetCodex();

        s_seenFish.Clear();
        if (seenFish != null)
        {
            foreach (string name in seenFish)
            {
                if (!string.IsNullOrEmpty(name)) s_seenFish.Add(name);
            }
        }

        s_droppedFish.Clear();
        if (droppedFish != null)
        {
            foreach (string name in droppedFish)
            {
                if (!string.IsNullOrEmpty(name)) s_droppedFish.Add(name);
            }
        }

        TotalCoralsPlaced = totalCoralsPlaced;
        TotalCoralsSold = totalCoralsSold;

        // 同步给鱼运动的内存图鉴，保持 HasFishAppeared/HasFishDropped 语义与存档一致（幂等，不弹成就）
        foreach (string name in s_seenFish)
        {
            鱼运动.RegisterFish(name);
        }
        foreach (string name in s_droppedFish)
        {
            鱼运动.RegisterFishDrop(name);
        }
    }

    public static List<string> GetSeenFishSnapshot()
    {
        return new List<string>(s_seenFish);
    }

    public static List<string> GetDroppedFishSnapshot()
    {
        return new List<string>(s_droppedFish);
    }

    /// <summary>清除存档时归零（Steam 已解锁成就不受影响）。</summary>
    public static void Reset()
    {
        s_seenFish.Clear();
        s_droppedFish.Clear();
        TotalCoralsPlaced = 0;
        TotalCoralsSold = 0;
    }

    // ==================== 游戏事件（由各系统真实行为处调用）====================

    /// <summary>真实放置珊瑚成功一次（DragAndPlaceSystem.FinishDrag 成功分支调用）。</summary>
    public static void OnCoralPlaced()
    {
        TotalCoralsPlaced++;
        SteamAchievements.Unlock(SteamAchievements.FirstCoral);
        if (TotalCoralsPlaced >= 10) SteamAchievements.Unlock(SteamAchievements.Corals10);
    }

    /// <summary>一条鱼首次出现（鱼运动.InitFish 调用）。同一鱼名只计一次。</summary>
    public static void OnFishSeen(string fishName)
    {
        if (string.IsNullOrEmpty(fishName)) return;
        if (!s_seenFish.Add(fishName)) return;

        int count = s_seenFish.Count;
        SteamAchievements.Unlock(SteamAchievements.FirstFish);
        if (count >= 10) SteamAchievements.Unlock(SteamAchievements.FishTypes10);
        if (count >= 30) SteamAchievements.Unlock(SteamAchievements.FishTypes30);
        if (count >= 50) SteamAchievements.Unlock(SteamAchievements.FishTypes50);

        FireCodexChanged();
    }

    /// <summary>一种鱼首次真实掉宝（鱼运动.Drop 实际产出时调用）。只做图鉴记录，无成就。</summary>
    public static void OnFishDropped(string fishName)
    {
        if (string.IsNullOrEmpty(fishName)) return;
        if (s_droppedFish.Add(fishName)) FireCodexChanged();
    }

    /// <summary>真实卖出珊瑚一次（SellFishItem.Sell 成功调用）。</summary>
    public static void OnCoralSold()
    {
        TotalCoralsSold++;
        SteamAchievements.Unlock(SteamAchievements.FirstSell);
    }
}
