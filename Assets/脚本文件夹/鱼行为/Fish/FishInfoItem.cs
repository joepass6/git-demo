using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 单条鱼信息显示组件
/// 挂在页面模板上，用于显示一条鱼的信息（鱼图像、玩耍时间、筑宝时间）
/// 数据来自绑定的 鱼运动 组件
/// 如果模板里有名字带"搬家"/"move"的Button，会自动绑定搬家功能
/// </summary>
public class FishInfoItem : MonoBehaviour
{
    [Header("UI组件引用")]
    [Tooltip("鱼图像的Image组件")]
    public Image fishImage;

    [Tooltip("玩耍时间的TextMeshProUGUI组件")]
    public TextMeshProUGUI playTimeText;

    [Tooltip("筑宝时间的TextMeshProUGUI组件")]
    public TextMeshProUGUI treasureBuildTimeText;

    [Header("自动刷新")]
    [Tooltip("是否自动刷新显示")]
    public bool autoRefresh = true;

    [Tooltip("刷新间隔（秒）")]
    public float refreshInterval = 1f;

    [Header("颜色设置")]
    [Tooltip("鱼存在时的颜色")]
    public Color existsColor = Color.white;

    [Tooltip("鱼不存在时的颜色")]
    public Color notExistsColor = Color.black;

    [Header("图鉴设置")]
    [Tooltip("此条目对应的鱼名（用于图鉴判断）。留空则从绑定的鱼获取）")]
    public string catalogFishName = "";

    // 绑定的鱼运动组件
    private 鱼运动 boundFish;
    private float refreshTimer;

    // ==================== 生命周期 ====================

    private void Update()
    {
        if (!autoRefresh)
        {
            UpdateColor();
            return;
        }

        // 鱼不存在时只更新颜色，不刷新数据
        if (boundFish == null)
        {
            UpdateColor();
            return;
        }

        refreshTimer += Time.deltaTime;
        if (refreshTimer >= refreshInterval)
        {
            refreshTimer = 0f;
            Refresh();
        }
    }

    // 编辑器中重置时自动查找子物体
    private void Reset()
    {
        AutoFindComponents();
    }

    // ==================== 自动查找子物体 ====================

    /// <summary>
    /// 自动查找并绑定子物体上的组件
    /// </summary>
    public void AutoFindComponents()
    {
        // 如果已经手动指定了，就不再自动查找
        if (fishImage == null)
        {
            Transform imgTf = FindChildRecursive(transform, "鱼图像");
            if (imgTf == null) imgTf = FindChildRecursive(transform, "fishImage");
            if (imgTf == null) imgTf = FindChildRecursive(transform, "image");
            if (imgTf != null) fishImage = imgTf.GetComponent<Image>();
        }

        if (playTimeText == null)
        {
            playTimeText = FindValueTextInContainer("玩耍时间");
        }

        if (treasureBuildTimeText == null)
        {
            treasureBuildTimeText = FindValueTextInContainer("筑宝时间");
        }

        Debug.Log("[FishInfoItem] 绑定完成 - playTimeText: " + (playTimeText != null ? playTimeText.gameObject.name : "null") + ", treasureBuildTimeText: " + (treasureBuildTimeText != null ? treasureBuildTimeText.gameObject.name : "null"));
    }

    /// <summary>
    /// 在指定容器下查找"时间"数值子物体（排除"文字"/"label"等标签）
    /// </summary>
    TextMeshProUGUI FindValueTextInContainer(string containerName)
    {
        Transform container = FindChildRecursive(transform, containerName);
        if (container == null) return null;

        // 遍历容器的直接子物体，找名字包含"时间"且不包含"文字"的
        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);

            // 跳过标签物体
            if (child.name.Contains("文字") || child.name.Contains("label"))
                continue;

            // 检查是否是数值物体（名字包含"时间"或"time"）
            if (child.name.Contains("时间") || child.name.Contains("time"))
            {
                TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp == null) tmp = child.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) return tmp;
            }
        }

        // 兜底：容器下所有子物体中，跳过"文字"/"label"，取第一个有TMP的
        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            if (child.name.Contains("文字") || child.name.Contains("label"))
                continue;

            TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = child.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) return tmp;
        }

        return null;
    }

    // ==================== 绑定与刷新 ====================

    /// <summary>
    /// 绑定一条鱼并显示其信息
    /// </summary>
    public void Bind(鱼运动 fish)
    {
        boundFish = fish;
        // 如果未手动配置鱼名，从绑定的鱼获取
        if (string.IsNullOrEmpty(catalogFishName) && fish != null)
        {
            catalogFishName = fish.fishName;
        }
        Refresh();
    }

    private bool warnedSameComponent = false;

    /// <summary>
    /// 刷新显示内容（玩耍时间=存活时间，掉宝时间=距下次掉落倒计时）
    /// </summary>
    public void Refresh()
    {
        if (boundFish == null) return;

        // 检查是否绑定到了同一个组件
        if (!warnedSameComponent && playTimeText != null && treasureBuildTimeText != null && playTimeText == treasureBuildTimeText)
        {
            Debug.LogWarning($"[FishInfoItem] 绑定错误! PlayTimeText 和 TreasureBuildTimeText 指向同一个组件: {playTimeText.gameObject.name}");
            warnedSameComponent = true;
        }

        if (fishImage != null)
        {
            fishImage.sprite = boundFish.fishSprite;
            fishImage.enabled = boundFish.fishSprite != null;
        }

        // 玩耍时间：纯数字（整数秒）
        if (playTimeText != null)
        {
            int aliveTime = Mathf.FloorToInt(boundFish.GetAliveTime());
            playTimeText.text = aliveTime.ToString();
        }

        // 筑宝时间：纯数字（整数秒）
        if (treasureBuildTimeText != null)
        {
            int dropCountdown = Mathf.FloorToInt(boundFish.GetDropCountdown());
            treasureBuildTimeText.text = dropCountdown.ToString();
        }

        // 更新颜色
        UpdateColor();
    }

    /// <summary>
    /// 根据图鉴状态更新颜色
    /// 鱼在游戏中出现过 → existsColor（白色/正常显示）
    /// 鱼从未出现过 → notExistsColor（黑色/未解锁）
    /// </summary>
    void UpdateColor()
    {
        // 判断这种鱼是否在游戏中出现过
        bool hasAppeared = 鱼运动.HasFishAppeared(catalogFishName);

        // 当前绑定的鱼还活着也算出现过
        if (!hasAppeared && boundFish != null)
        {
            hasAppeared = true;
        }

        Color c = hasAppeared ? existsColor : notExistsColor;

        if (fishImage != null) fishImage.color = c;
    }

    // ==================== 辅助方法 ====================

    public 鱼运动 GetBoundFish() => boundFish;

    public bool IsFishAlive() => boundFish != null;

    Transform FindChildRecursive(Transform parent, string keyword)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.Contains(keyword)) return child;
            Transform result = FindChildRecursive(child, keyword);
            if (result != null) return result;
        }
        return null;
    }
}
