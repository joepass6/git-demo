using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 自动扫描场景中 Tag 为 "fish" 的物体，将鱼信息显示在指定 UI 面板上。
/// 使用克隆模板方式生成 UI 条目。
/// 单鱼横向翻页模式：每次只显示一条鱼，通过按钮切换。
/// 没有鱼时自动隐藏面板。
/// </summary>
public class FishTagScanner : MonoBehaviour
{
    public static FishTagScanner Instance { get; private set; }

    public static FishTagScanner EnsureInstance()
    {
        if (Instance != null) return Instance;
        Instance = FindObjectOfType<FishTagScanner>();
        if (Instance != null) return Instance;
        foreach (var s in Resources.FindObjectsOfTypeAll<FishTagScanner>())
        {
            if (s != null && s.gameObject.scene.IsValid()) { Instance = s; break; }
        }
        return Instance;
    }

    [Header("扫描设置")]
    [Tooltip("要扫描的 Tag 名称")]
    public string fishTag = "fish";

    [Tooltip("扫描间隔（秒）")]
    public float scanInterval = 2f;

    [Tooltip("鱼物体上的组件类型名（用于获取鱼数据）")]
    public string fishComponentTypeName = "鱼运动";

    [Header("UI 设置")]
    [Tooltip("面板根物体（没有鱼时会被 SetActive(false)）")]
    public GameObject panelRoot;

    [Tooltip("鱼信息条目的父容器")]
    public RectTransform contentContainer;

    [Tooltip("鱼信息条目模板（会被克隆）")]
    public GameObject itemTemplate;

    [Tooltip("每个条目的宽度（横向模式）")]
    public float itemWidth = 300f;

    [Tooltip("条目之间的间距")]
    public float itemSpacing = 20f;

    [Header("位置设置")]
    [Tooltip("生成位置偏移 X")]
    public float offsetX = 0f;

    [Tooltip("生成位置偏移 Y")]
    public float offsetY = 0f;

    [Header("页面切换按钮")]
    [Tooltip("上一个按钮（切换上一条鱼）")]
    public Button prevButton;

    [Tooltip("下一个按钮（切换下一条鱼）")]
    public Button nextButton;

    [Header("扩展信息组件（可拖拽指定，留空则按名字自动查找）")]
    [Tooltip("鱼图像的 Image 组件（拖入模板中的 Image）— 显示鱼图片")]
    public Image fishImage;

    [Tooltip("掉落物图片的 Image 组件（拖入模板中的 Image）")]
    public Image dropItemImage;

    [Tooltip("珊瑚名称的 TMP 文本组件（拖入模板中的 TMP）— 显示来自哪个 ObjectSpawner")]
    public TextMeshProUGUI coralNameText;

    [Tooltip("珊瑚图片的 Image 组件（拖入模板中的 Image）— 显示来自哪个珊瑚的图片")]
    public Image coralImage;

    [Tooltip("存在时间的 TMP 文本组件（拖入模板中的 TMP）— 显示存活/剩余时间")]
    public TextMeshProUGUI existenceTimeText;

    [Tooltip("掉落倒计时的 TMP 文本组件（拖入模板中的 TMP）— 显示下次掉落倒计时")]
    public TextMeshProUGUI dropCountdownText;

    [Header("图片尺寸设置")]
    [Tooltip("是否保持鱼图片原始比例（不被拉伸变形）")]
    public bool preserveFishImageAspect = true;

    [Tooltip("鱼图片自定义宽度（0=使用原始尺寸）")]
    public float fishImageWidth = 100f;

    [Tooltip("鱼图片自定义高度（0=使用原始尺寸）")]
    public float fishImageHeight = 100f;

    [Tooltip("是否保持掉落物图片原始比例")]
    public bool preserveDropItemAspect = true;

    [Tooltip("掉落物图片自定义宽度（0=使用原始尺寸）")]
    public float dropItemImageWidth = 80f;

    [Tooltip("掉落物图片自定义高度（0=使用原始尺寸）")]
    public float dropItemImageHeight = 80f;

    [Tooltip("是否保持珊瑚图片原始比例")]
    public bool preserveCoralAspect = true;

    [Tooltip("珊瑚图片自定义宽度（0=使用原始尺寸）")]
    public float coralImageWidth = 80f;

    [Tooltip("珊瑚图片自定义高度（0=使用原始尺寸）")]
    public float coralImageHeight = 80f;

    // 缓存变量
    private RectTransform cc;
    private GameObject it;
    private Button pb;
    private Button nb;
    private float scanTimer;
    private float refreshTimer;  // 刷新当前鱼时间显示的计时器

    private List<鱼运动> cachedFishList = new List<鱼运动>();
    private int currentFishIndex = 0;
    private GameObject currentItemObj;

    void Awake()
    {
        // 设置静态实例（即使物体未激活，Instance 仍然可用）
        Instance = this;

        cc = (object)contentContainer != null ? contentContainer : null;
        it = (object)itemTemplate != null ? itemTemplate : null;
        pb = (object)prevButton != null ? prevButton : null;
        nb = (object)nextButton != null ? nextButton : null;

        AutoFindReferences();
        BindPageButtons();

        Debug.Log($"[FishTagScanner] 初始化: cc={((object)cc != null ? cc.name : "null")}, it={((object)it != null ? it.name : "null")}");
    }

    void AutoFindReferences()
    {
        if ((object)panelRoot == null)
        {
            panelRoot = gameObject;
        }

        if (cc == null)
        {
            Transform ct = FindChildByName(transform, "Content");
            if (ct == null) ct = FindChildByName(transform, "Viewport");
            if (ct == null) ct = FindChildByName(transform, "容器");
            if (ct == null) ct = FindChildByName(transform, "页面");
            if (ct == null) ct = FindChildByName(transform, "鱼页面");
            if (ct != null)
            {
                cc = ct as RectTransform;
                contentContainer = cc;
                Debug.Log($"[FishTagScanner] 自动找到contentContainer: {ct.name}");
            }
            else
                Debug.LogWarning("[FishTagScanner] 找不到contentContainer！请命名子物体为Content或页面");
        }

        if (it == null)
        {
            Transform tm = FindChildByName(transform, "Template");
            if (tm == null) tm = FindChildByName(transform, "ItemTemplate");
            if (tm == null) tm = FindChildByName(transform, "条目");
            if (tm == null) tm = FindChildByName(transform, "Item");
            if (tm == null)
            {
                FishInfoItem[] items = GetComponentsInChildren<FishInfoItem>(true);
                if (items.Length > 0) tm = items[0].transform;
            }
            if (tm != null)
            {
                it = tm.gameObject;
                itemTemplate = it;
                it.SetActive(false);
                Debug.Log($"[FishTagScanner] 自动找到itemTemplate: {tm.name}");
            }
            else
                Debug.LogWarning("[FishTagScanner] 找不到itemTemplate！请命名子物体为Template或Item");
        }

        if (pb == null)
        {
            Transform bt = FindChildByName(transform, "上一个");
            if (bt == null) bt = FindChildByName(transform, "Prev");
            if (bt != null)
            {
                pb = bt.GetComponent<Button>();
                if (pb == null) pb = bt.gameObject.AddComponent<Button>();
                prevButton = pb;
            }
        }

        if (nb == null)
        {
            Transform bt = FindChildByName(transform, "下一个");
            if (bt == null) bt = FindChildByName(transform, "Next");
            if (bt != null)
            {
                nb = bt.GetComponent<Button>();
                if (nb == null) nb = bt.gameObject.AddComponent<Button>();
                nextButton = nb;
            }
        }
    }

    void BindPageButtons()
    {
        if (pb != null)
        {
            pb.onClick = new Button.ButtonClickedEvent();
            pb.onClick.AddListener(OnPrevFish);
        }
        if (nb != null)
        {
            nb.onClick = new Button.ButtonClickedEvent();
            nb.onClick.AddListener(OnNextFish);
        }

        // 绑定"关闭"按钮
        Transform closeBtn = FindChildByName(transform, "关闭");
        if (closeBtn != null)
        {
            Button close = closeBtn.GetComponent<Button>();
            if (close == null) close = closeBtn.gameObject.AddComponent<Button>();
            close.onClick = new Button.ButtonClickedEvent();
            close.onClick.AddListener(ClosePanel);
        }
    }

    Transform FindChildByName(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.Contains(name)) return child;
            Transform result = FindChildByName(child, name);
            if (result != null) return result;
        }
        return null;
    }

    void OnEnable()
    {
        // 不立即扫描，让 Update 按周期扫描，避免没鱼时立刻 HidePanel 把自己关掉
        scanTimer = scanInterval;  // 触发下一帧立即扫描一次
    }

    void Update()
    {
        // 无论 panelRoot 是否激活，都扫描（用 CanvasGroup 隐藏时脚本仍能运行）
        // 如果 panelRoot 是其他物体且被 SetActive(false)，但 FishTagScanner 不挂在上面，脚本仍能运行
        scanTimer += Time.deltaTime;
        if (scanTimer >= scanInterval)
        {
            scanTimer = 0f;
            ScanAndUpdate();
        }

        // 每 1 秒刷新当前鱼的时间显示
        if (currentItemObj != null)
        {
            refreshTimer += Time.deltaTime;
            if (refreshTimer >= 1f)
            {
                refreshTimer = 0f;
                RefreshCurrentFishTime();
            }
        }
    }

    /// <summary>
    /// 刷新当前显示鱼的时间相关数据（存在时间、掉落倒计时等）
    /// </summary>
    void RefreshCurrentFishTime()
    {
        if (currentItemObj == null) return;
        if (cachedFishList.Count == 0 || currentFishIndex >= cachedFishList.Count) return;

        鱼运动 fish = cachedFishList[currentFishIndex];
        if (fish == null) return;

        // 刷新存在时间
        SetExistenceTime(fish);
        // 刷新掉落倒计时
        SetDropTimeInfo(fish);

        // 同时让 FishInfoItem 自己刷新（更新 _value 字段）
        FishInfoItem item = currentItemObj.GetComponent<FishInfoItem>();
        if (item != null) item.Refresh();
    }

    /// <summary>
    /// 手动扫描：有鱼显示面板，没鱼隐藏面板
    /// </summary>
    public void ManualScan()
    {
        ScanAndUpdate();
    }

    /// <summary>
    /// 关闭面板（由用户主动关闭时调用）
    /// </summary>
    public void ClosePanel()
    {
        if (panelRoot != null && panelRoot != gameObject) panelRoot.SetActive(false);
        ClearCurrentItem();
        UpdateButtonState();
    }

    void OnPrevFish()
    {
        if (cachedFishList.Count <= 1) return;
        currentFishIndex = currentFishIndex <= 0 ? cachedFishList.Count - 1 : currentFishIndex - 1;
        ShowCurrentFish();
    }

    void OnNextFish()
    {
        if (cachedFishList.Count <= 1) return;
        currentFishIndex = currentFishIndex >= cachedFishList.Count - 1 ? 0 : currentFishIndex + 1;
        ShowCurrentFish();
    }

    void ScanAndUpdate()
    {
        List<鱼运动> currentFishList = ScanFishObjects();

        bool changed = false;
        if (currentFishList.Count != cachedFishList.Count)
        {
            changed = true;
        }
        else
        {
            for (int i = 0; i < currentFishList.Count; i++)
            {
                if (currentFishList[i] != cachedFishList[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed && cachedFishList.Count > 0 && currentItemObj != null)
        {
            return;
        }

        cachedFishList = currentFishList;

        Debug.Log($"[FishTagScanner] 扫描完成: 找到 {currentFishList.Count} 条鱼");

        if (currentFishList.Count == 0)
        {
            // 没有鱼：隐藏面板
            HidePanel();
        }
        else
        {
            // 有鱼：显示主面板
            ShowPanel();
            currentFishIndex = Mathf.Clamp(currentFishIndex, 0, currentFishList.Count - 1);
            ShowCurrentFish();
        }
    }

    void ShowCurrentFish()
    {
        UpdateButtonState();

        if (cachedFishList.Count == 0)
        {
            ClearCurrentItem();
            return;
        }

        if (cc == null || it == null)
        {
            Debug.LogError("[FishTagScanner] contentContainer 或 itemTemplate 为空！");
            return;
        }

        ClearCurrentItem();

        鱼运动 fish = cachedFishList[currentFishIndex];
        if (fish != null)
        {
            currentItemObj = Instantiate(it, cc);
            currentItemObj.SetActive(true);
            currentItemObj.name = "FishItem_" + fish.fishName;

            FishInfoItem item = currentItemObj.GetComponent<FishInfoItem>();
            if (item == null) item = currentItemObj.AddComponent<FishInfoItem>();
            item.AutoFindComponents();
            item.Bind(fish);

            // ====== 扩展信息：鱼图片、掉落物图片、珊瑚来源、珊瑚图片、存在时间、掉落倒计时 ======
            SetFishImage(fish);
            SetDropItemImage(fish);
            SetCoralSource(fish);
            SetCoralImage(fish);
            SetExistenceTime(fish);
            SetDropTimeInfo(fish);

            ReLayoutCurrentItem();
            Debug.Log($"[FishTagScanner] 显示鱼: {fish.fishName}, 索引={currentFishIndex}/{cachedFishList.Count}");
        }
    }

    /// <summary>
    /// 设置鱼图片，并应用尺寸/比例设置
    /// 使用 Inspector 中拖入的 fishImage 字段，留空则按名字自动查找
    /// </summary>
    void SetFishImage(鱼运动 fish)
    {
        if (currentItemObj == null || fish == null) return;

        // 从克隆体找到对应组件
        Image fImg = FindCloneComponent(fishImage);

        // 如果 Inspector 没拖入，按名字自动查找
        if (fImg == null)
        {
            Transform fishImgTf = FindChildByName(currentItemObj.transform, "鱼图像");
            if (fishImgTf == null) fishImgTf = FindChildByName(currentItemObj.transform, "fishImage");
            if (fishImgTf == null) fishImgTf = FindChildByName(currentItemObj.transform, "FishImage");
            if (fishImgTf == null) return;
            fImg = fishImgTf.GetComponent<Image>();
        }

        if (fImg == null) return;

        // 设置 Sprite
        Sprite fishSprite = fish.fishSprite;
        if (fishSprite != null)
        {
            fImg.sprite = fishSprite;
            fImg.enabled = true;
            ApplyImageSize(fImg, fishSprite, preserveFishImageAspect, fishImageWidth, fishImageHeight);
            Debug.Log($"[FishTagScanner] 设置鱼图片: {fishSprite.name}");
        }
        else
        {
            fImg.enabled = false;
        }
    }

    /// <summary>
    /// 应用图片尺寸设置：保持原始比例 + 自定义尺寸
    /// </summary>
    void ApplyImageSize(Image img, Sprite sprite, bool preserveAspect, float targetWidth, float targetHeight)
    {
        if (img == null || sprite == null) return;

        RectTransform rt = img.GetComponent<RectTransform>();
        if (rt == null) return;

        // 先设置 Image 的 preserveAspect 属性
        img.preserveAspect = preserveAspect;

        float spriteWidth = sprite.rect.width;
        float spriteHeight = sprite.rect.height;

        if (targetWidth <= 0 && targetHeight <= 0)
        {
            // 0 = 使用原始尺寸
            rt.sizeDelta = new Vector2(spriteWidth, spriteHeight);
        }
        else if (preserveAspect)
        {
            // 保持比例缩放到目标尺寸
            float aspectRatio = spriteWidth / spriteHeight;
            if (targetWidth > 0 && targetHeight > 0)
            {
                // 都指定了：按宽度计算高度，或按高度计算宽度
                float wByH = targetHeight * aspectRatio;
                if (wByH <= targetWidth)
                {
                    rt.sizeDelta = new Vector2(wByH, targetHeight);
                }
                else
                {
                    rt.sizeDelta = new Vector2(targetWidth, targetWidth / aspectRatio);
                }
            }
            else if (targetWidth > 0)
            {
                rt.sizeDelta = new Vector2(targetWidth, targetWidth / aspectRatio);
            }
            else
            {
                rt.sizeDelta = new Vector2(targetHeight * aspectRatio, targetHeight);
            }
        }
        else
        {
            // 不保持比例，直接使用目标尺寸
            if (targetWidth > 0 && targetHeight > 0)
            {
                rt.sizeDelta = new Vector2(targetWidth, targetHeight);
            }
        }
    }

    /// <summary>
    /// 设置所属珊瑚图片：从 ObjectSpawner 上提取 Sprite 显示
    /// 使用 Inspector 中拖入的 coralImage 字段，留空则按名字自动查找
    /// </summary>
    void SetCoralImage(鱼运动 fish)
    {
        if (currentItemObj == null) return;

        // 从克隆体找到对应组件
        Image cImg = FindCloneComponent(coralImage);

        // 如果 Inspector 没拖入，按名字自动查找
        if (cImg == null)
        {
            Transform coralImgTf = FindChildByName(currentItemObj.transform, "珊瑚图片");
            if (coralImgTf == null) coralImgTf = FindChildByName(currentItemObj.transform, "coralImage");
            if (coralImgTf == null) coralImgTf = FindChildByName(currentItemObj.transform, "所属珊瑚");
            if (coralImgTf == null) return;
            cImg = coralImgTf.GetComponent<Image>();
        }

        if (cImg == null) return;

        // 找到鱼所属的 ObjectSpawner
        ObjectSpawner spawner = FindSpawnerForFish(fish);
        if (spawner == null)
        {
            cImg.enabled = false;
            return;
        }

        // 从 ObjectSpawner 上提取 Sprite
        Sprite coralSprite = GetSpriteFromObject(spawner.gameObject);
        if (coralSprite != null)
        {
            cImg.sprite = coralSprite;
            cImg.enabled = true;
            ApplyImageSize(cImg, coralSprite, preserveCoralAspect, coralImageWidth, coralImageHeight);
            Debug.Log($"[FishTagScanner] 设置珊瑚图片: {coralSprite.name}");
        }
        else
        {
            cImg.enabled = false;
            Debug.LogWarning($"[FishTagScanner] ObjectSpawner {spawner.gameObject.name} 上没有 Sprite");
        }
    }

    /// <summary>
    /// 从 GameObject 上提取 Sprite（优先 SpriteRenderer，其次 UI Image）
    /// </summary>
    Sprite GetSpriteFromObject(GameObject obj)
    {
        if (obj == null) return null;

        // 1. SpriteRenderer
        SpriteRenderer sr = obj.GetComponent<SpriteRenderer>();
        if (sr == null) sr = obj.GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null && sr.sprite != null) return sr.sprite;

        // 2. UI Image
        Image img = obj.GetComponent<Image>();
        if (img == null) img = obj.GetComponentInChildren<Image>(true);
        if (img != null && img.sprite != null) return img.sprite;

        return null;
    }

    /// <summary>
    /// 在克隆出的条目上，根据模板字段找到对应的克隆组件
    /// 模板字段（dropItemImage 等）指向模板中的组件，这里通过名字匹配在克隆体上找到对应组件
    /// </summary>
    T FindCloneComponent<T>(T templateField) where T : Component
    {
        if (templateField == null || currentItemObj == null) return null;

        // 通过 GameObject 名字在克隆体上查找对应组件
        string targetName = templateField.gameObject.name;
        Transform found = FindChildByName(currentItemObj.transform, targetName);
        if (found != null)
        {
            T comp = found.GetComponent<T>();
            if (comp != null) return comp;
        }

        // 如果名字找不到，尝试按组件类型在克隆体直接找
        return currentItemObj.GetComponentInChildren<T>(true);
    }

    /// <summary>
    /// 设置掉落物图片：从鱼运动.itemPrefab 中提取 Sprite
    /// 使用 Inspector 中拖入的 dropItemImage 字段，留空则按名字自动查找
    /// </summary>
    void SetDropItemImage(鱼运动 fish)
    {
        if (currentItemObj == null || fish.itemPrefab == null) return;

        // 从克隆体找到对应组件
        Image dropImage = FindCloneComponent(dropItemImage);

        // 如果 Inspector 没拖入，按名字自动查找
        if (dropImage == null)
        {
            Transform dropImgTf = FindChildByName(currentItemObj.transform, "掉落物图片");
            if (dropImgTf == null) dropImgTf = FindChildByName(currentItemObj.transform, "dropItemImage");
            if (dropImgTf == null) dropImgTf = FindChildByName(currentItemObj.transform, "dropImage");
            if (dropImgTf == null) return;
            dropImage = dropImgTf.GetComponent<Image>();
        }

        if (dropImage == null) return;

        // 从 itemPrefab 提取 Sprite
        Sprite dropSprite = GetSpriteFromPrefab(fish.itemPrefab);
        if (dropSprite != null)
        {
            dropImage.sprite = dropSprite;
            dropImage.enabled = true;
            ApplyImageSize(dropImage, dropSprite, preserveDropItemAspect, dropItemImageWidth, dropItemImageHeight);
            Debug.Log($"[FishTagScanner] 设置掉落物图片: {dropSprite.name}");
        }
        else
        {
            dropImage.enabled = false;
            Debug.LogWarning($"[FishTagScanner] 掉落物预制件 {fish.itemPrefab.name} 上没有 Sprite");
        }
    }

    /// <summary>
    /// 从预制件中提取 Sprite（优先 SpriteRenderer，其次 UI Image）
    /// </summary>
    Sprite GetSpriteFromPrefab(GameObject prefab)
    {
        if (prefab == null) return null;

        // 1. 先找 SpriteRenderer
        SpriteRenderer sr = prefab.GetComponent<SpriteRenderer>();
        if (sr == null) sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null && sr.sprite != null) return sr.sprite;

        // 2. 找 UI Image
        Image img = prefab.GetComponent<Image>();
        if (img == null) img = prefab.GetComponentInChildren<Image>(true);
        if (img != null && img.sprite != null) return img.sprite;

        return null;
    }

    /// <summary>
    /// 设置珊瑚来源：显示来自场景中哪个 ObjectSpawner 预制体
    /// 使用 Inspector 中拖入的 coralNameText 字段，留空则按名字自动查找
    /// </summary>
    void SetCoralSource(鱼运动 fish)
    {
        if (currentItemObj == null) return;

        // 从克隆体找到对应组件
        TextMeshProUGUI coralText = FindCloneComponent(coralNameText);

        // 如果 Inspector 没拖入，按名字自动查找
        if (coralText == null)
        {
            Transform coralTf = FindChildByName(currentItemObj.transform, "珊瑚名称");
            if (coralTf == null) coralTf = FindChildByName(currentItemObj.transform, "来自珊瑚");
            if (coralTf == null) coralTf = FindChildByName(currentItemObj.transform, "coralName");
            if (coralTf == null) return;
            coralText = coralTf.GetComponent<TextMeshProUGUI>();
        }

        if (coralText == null) return;

        ObjectSpawner spawner = FindSpawnerForFish(fish);
        if (spawner != null)
        {
            string spawnerName = spawner.gameObject.name;

            // 编辑器环境：尝试获取 ObjectSpawner 预制体信息
            #if UNITY_EDITOR
            GameObject source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(spawner.gameObject);
            if (source != null)
            {
                string prefabName = source.name;
                coralText.text = $"{spawnerName} (预制体: {prefabName})";
                Debug.Log($"[FishTagScanner] 鱼来自珊瑚: {spawnerName}, 预制体: {prefabName}");
                return;
            }
            #endif

            coralText.text = spawnerName;
            Debug.Log($"[FishTagScanner] 鱼来自珊瑚: {spawnerName}");
        }
        else
        {
            coralText.text = "未知珊瑚";
            Debug.LogWarning($"[FishTagScanner] 找不到鱼 {fish.fishName} 所属的 ObjectSpawner");
        }
    }

    /// <summary>
    /// 查找鱼所属的 ObjectSpawner（珊瑚）
    /// 由于鱼生成后 SetParent(null)，不能用 GetComponentInParent
    /// 使用 GetSpawnedFish() 反向查找 + 距离最近兜底
    /// </summary>
    ObjectSpawner FindSpawnerForFish(鱼运动 fish)
    {
        if (fish == null) return null;

        // 1. 从 GetSpawnedFish 反向查找：遍历所有 ObjectSpawner 看谁生成了这条鱼
        ObjectSpawner[] allSpawners = Resources.FindObjectsOfTypeAll<ObjectSpawner>();
        foreach (var s in allSpawners)
        {
            if (s == null) continue;
            if (!s.gameObject.scene.IsValid()) continue;

            List<鱼运动> spawnedFish = s.GetSpawnedFish();
            if (spawnedFish != null && spawnedFish.Contains(fish))
            {
                Debug.Log($"[FishTagScanner] 通过 GetSpawnedFish 反向匹配到 ObjectSpawner: {s.gameObject.name}");
                return s;
            }
        }

        // 2. 检查鱼的父级
        ObjectSpawner parentSpawner = fish.GetComponentInParent<ObjectSpawner>();
        if (parentSpawner != null && parentSpawner.gameObject.scene.IsValid())
        {
            Debug.Log($"[FishTagScanner] 通过父级找到 ObjectSpawner: {parentSpawner.gameObject.name}");
            return parentSpawner;
        }

        // 3. 距离最近的 ObjectSpawner（兜底）
        float minDist = float.MaxValue;
        ObjectSpawner nearest = null;
        foreach (var s in allSpawners)
        {
            if (s == null) continue;
            if (!s.gameObject.scene.IsValid()) continue;
            if (!s.gameObject.activeInHierarchy) continue;

            float dist = Vector3.Distance(fish.transform.position, s.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = s;
            }
        }

        if (nearest != null)
        {
            Debug.Log($"[FishTagScanner] 兜底使用最近 ObjectSpawner: {nearest.gameObject.name}, 距离={minDist:F1}");
        }
        return nearest;
    }

    /// <summary>
    /// 设置存在时间（倒计时：剩余时间）
    /// 使用 Inspector 中拖入的 existenceTimeText 字段
    /// </summary>
    void SetExistenceTime(鱼运动 fish)
    {
        if (currentItemObj == null) return;

        TextMeshProUGUI existText = FindCloneComponent(existenceTimeText);
        if (existText == null)
        {
            Transform existTf = FindChildByName(currentItemObj.transform, "存在时间");
            if (existTf == null) existTf = FindChildByName(currentItemObj.transform, "existenceTime");
            if (existTf == null) existTf = FindChildByName(currentItemObj.transform, "aliveTime");
            if (existTf == null) return;
            existText = existTf.GetComponent<TextMeshProUGUI>();
        }

        if (existText == null) return;
        float remaining = Mathf.Max(0f, fish.destroyTime - fish.GetAliveTime());
        existText.text = $"{remaining:F0}";
    }

    /// <summary>
    /// 设置掉落倒计时
    /// 使用 Inspector 中拖入的 dropCountdownText 字段
    /// </summary>
    void SetDropTimeInfo(鱼运动 fish)
    {
        if (currentItemObj == null) return;

        // ====== 掉落倒计时 ======
        TextMeshProUGUI countdown = FindCloneComponent(dropCountdownText);
        if (countdown == null)
        {
            Transform cdTf = FindChildByName(currentItemObj.transform, "掉落倒计时");
            if (cdTf == null) cdTf = FindChildByName(currentItemObj.transform, "dropCountdown");
            if (cdTf == null) cdTf = FindChildByName(currentItemObj.transform, "countdown");
            if (cdTf == null) cdTf = FindChildByName(currentItemObj.transform, "掉落时间");
            if (cdTf == null) cdTf = FindChildByName(currentItemObj.transform, "dropTime");
            if (cdTf != null) countdown = cdTf.GetComponent<TextMeshProUGUI>();
        }
        if (countdown != null)
        {
            countdown.text = $"{fish.GetDropCountdown():F0}";
        }
    }

    void ClearCurrentItem()
    {
        if (currentItemObj != null)
        {
            Destroy(currentItemObj);
            currentItemObj = null;
        }
    }

    void UpdateButtonState()
    {
        // 只在 1 条鱼时禁用按钮（无需切换），其他情况保持可点击
        bool interactable = cachedFishList.Count != 1;
        if (pb != null) pb.interactable = interactable;
        if (nb != null) nb.interactable = interactable;
    }

    List<鱼运动> ScanFishObjects()
    {
        GameObject[] fishObjects = GameObject.FindGameObjectsWithTag(fishTag);
        List<鱼运动> result = new List<鱼运动>(fishObjects.Length);  // 预分配避免扩容GC
        HashSet<鱼运动> set = new HashSet<鱼运动>();  // 代替 result.Contains() 的 O(n²) 去重

        for (int i = 0; i < fishObjects.Length; i++)
        {
            var go = fishObjects[i];
            if (go == null || !go.activeInHierarchy) continue;

            // 先快速尝试三种常见路径（直接 GetComponent / GetComponentInParent / 子物体 GetComponent）
            鱼运动 fish = go.GetComponent<鱼运动>();
            if (fish == null) fish = go.GetComponentInParent<鱼运动>();
            if (fish == null)
            {
                // 只找直接子物体的 Component（不调用 GetComponentsInChildren 拖回所有 MonoBehaviour）
                for (int c = 0; c < go.transform.childCount; c++)
                {
                    Transform child = go.transform.GetChild(c);
                    if (child == null) continue;
                    fish = child.GetComponent<鱼运动>();
                    if (fish != null) break;
                }
            }

            if (fish != null && set.Add(fish))  // HashSet.Add 去重 = 没有重复才返回 true
            {
                result.Add(fish);
            }
        }

        Debug.Log($"[FishTagScanner] 扫描: tag={fishTag} 的物体 {fishObjects.Length} 个, 有效鱼 {result.Count} 条");
        return result;
    }

    void ShowPanel()
    {
        if (panelRoot != null && panelRoot != gameObject && !panelRoot.activeSelf)
        {
            panelRoot.SetActive(true);
        }
        else if (panelRoot == gameObject)
        {
            // 恢复 CanvasGroup
            CanvasGroup cg = GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            // 恢复子物体（跳过模板）
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i).gameObject;
                if (child != it) child.SetActive(true);
            }
        }
        Debug.Log("[FishTagScanner] 显示面板");
    }

    void HidePanel()
    {
        if (panelRoot != null && panelRoot != gameObject)
        {
            // panelRoot 是其他物体：直接 SetActive(false)
            panelRoot.SetActive(false);
            Debug.Log("[FishTagScanner] 隐藏面板（没有鱼）");
        }
        else if (panelRoot == gameObject)
        {
            // panelRoot 是自身：用 CanvasGroup 隐藏，保持脚本运行
            CanvasGroup cg = GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
            else
            {
                // 没有 CanvasGroup：隐藏所有直接子物体
                for (int i = 0; i < transform.childCount; i++)
                {
                    transform.GetChild(i).gameObject.SetActive(false);
                }
            }
            Debug.Log("[FishTagScanner] 隐藏面板（没有鱼）");
        }
        ClearCurrentItem();
        UpdateButtonState();
    }

    void ReLayoutCurrentItem()
    {
        if (currentItemObj == null || cc == null) return;

        RectTransform rt = currentItemObj.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchoredPosition = new Vector2(offsetX, offsetY);
        // 单鱼模式下不改容器宽度，避免影响布局
    }

    public int GetFishCount() => cachedFishList.Count;
    public int GetCurrentFishIndex() => currentFishIndex;
    public 鱼运动 GetCurrentFish() => cachedFishList.Count > 0 ? cachedFishList[currentFishIndex] : null;
    public List<鱼运动> GetCurrentFishList() => cachedFishList;
}
