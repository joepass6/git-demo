using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 鱼槽显示组件
/// 挂在"1框/2框/3框/4框"上
/// 显示ObjectSpawner中对应Spawn Entry的鱼预制体信息
/// （预制体上的鱼运动组件中的fishName、fishSprite等）
/// </summary>
public class FishSlotDisplay : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("框的索引：0=1框, 1=2框, 2=3框, 3=4框")]
    public int slotIndex;

    [Tooltip("ObjectSpawner物体（场景中的生成器）")]
    public ObjectSpawner objectSpawner;

    [Header("UI组件")]
    [Tooltip("显示鱼图片的Image组件")]
    public Image fishImage;

    [Tooltip("显示鱼名的Text组件")]
    public TextMeshProUGUI fishNameText;

    [Tooltip("显示鱼概率的Text组件（不填则概率显示在鱼名后）")]
    public TextMeshProUGUI probabilityText;

    private Sprite fallbackSprite;
    private float currentWeight;

    void Reset()
    {
        AutoFindComponents();
    }

    void OnEnable()
    {
        AutoFindComponents();
        RefreshDisplay();
    }

    void Start()
    {
        AutoFindComponents();
        RefreshDisplay();
    }

    /// <summary>
    /// 当ObjectSpawner改变时调用此方法刷新显示
    /// </summary>
    public void UpdateSpawner(ObjectSpawner newSpawner)
    {
        objectSpawner = newSpawner;
        RefreshDisplay();
    }

    /// <summary>
    /// 自动查找子物体上的UI组件
    /// </summary>
    void AutoFindComponents()
    {
        if (fishImage == null)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img.gameObject.name.Contains("Image") || img.gameObject.name.Contains("图片"))
                {
                    fishImage = img;
                    break;
                }
            }
            if (fishImage == null && images.Length > 0) fishImage = images[0];
        }

        if (fishNameText == null)
        {
            TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t.gameObject.name.Contains("Text") || t.gameObject.name.Contains("文字"))
                {
                    fishNameText = t;
                    break;
                }
            }
            if (fishNameText == null && texts.Length > 0) fishNameText = texts[0];
        }
    }

    /// <summary>
    /// 根据索引读取并显示对应鱼预制体的信息（按顺序对应，并显示概率）
    /// </summary>
    public void RefreshDisplay()
    {
        if (objectSpawner == null)
        {
            ClearUI();
            return;
        }

        // 获取Spawn Entries
        List<ObjectSpawner.SpawnEntry> entries = objectSpawner.GetSpawnEntries();

        if (slotIndex < 0 || slotIndex >= entries.Count)
        {
            ClearUI();
            return;
        }

        ObjectSpawner.SpawnEntry entry = entries[slotIndex];

        // 记录当前条目的权重，用于计算概率
        currentWeight = entry.weight;

        if (entry.prefab == null)
        {
            ClearUI();
            return;
        }

        // 从预制体上读取鱼运动组件
        鱼运动 fishData = entry.prefab.GetComponent<鱼运动>();

        if (fishData == null)
        {
            // 预制体上没有鱼运动组件，尝试从SpriteRenderer获取
            SpriteRenderer sr = entry.prefab.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                fallbackSprite = sr.sprite;
            }
            UpdateFallbackUI(entry.prefab.name);
        }
        else
        {
            UpdateUI(fishData);
        }

        // 显示概率
        UpdateProbability(entries);
    }

    /// <summary>
    /// 计算并显示当前鱼的概率（权重百分比）
    /// </summary>
    void UpdateProbability(List<ObjectSpawner.SpawnEntry> entries)
    {
        // 计算总权重
        float totalWeight = 0f;
        foreach (var e in entries)
        {
            if (e.prefab != null) totalWeight += e.weight;
        }

        float probability = 0f;
        if (totalWeight > 0f)
        {
            probability = currentWeight / totalWeight * 100f;
        }

        string probText = $"{Mathf.RoundToInt(probability)}%";

        if (probabilityText != null)
        {
            probabilityText.text = probText;
        }
        else if (fishNameText != null)
        {
            // 没有单独的概率文本，追加在鱼名后
            string name = fishNameText.text;
            // 避免重复追加
            if (!name.Contains("%"))
            {
                fishNameText.text = $"{name}  {probText}";
            }
            else
            {
                // 替换旧的概率
                int idx = name.IndexOf("  ");
                if (idx > 0) name = name.Substring(0, idx);
                fishNameText.text = $"{name}  {probText}";
            }
        }
    }

    /// <summary>
    /// 使用鱼运动组件数据更新UI
    /// </summary>
    void UpdateUI(鱼运动 fish)
    {
        if (fishImage != null)
        {
            fishImage.sprite = fish.fishSprite;
            fishImage.enabled = fish.fishSprite != null;
        }

        if (fishNameText != null)
        {
            fishNameText.text = fish.fishName;
        }
    }

    /// <summary>
    /// 无鱼运动组件时使用预制体名称显示
    /// </summary>
    void UpdateFallbackUI(string name)
    {
        if (fishImage != null)
        {
            fishImage.sprite = fallbackSprite;
            fishImage.enabled = fallbackSprite != null;
        }

        if (fishNameText != null)
        {
            fishNameText.text = name;
        }
    }

    /// <summary>
    /// 清空UI
    /// </summary>
    void ClearUI()
    {
        currentWeight = 0f;

        if (fishImage != null)
        {
            fishImage.sprite = null;
            fishImage.enabled = false;
        }

        if (fishNameText != null)
        {
            fishNameText.text = "";
        }

        if (probabilityText != null)
        {
            probabilityText.text = "";
        }
    }
}
