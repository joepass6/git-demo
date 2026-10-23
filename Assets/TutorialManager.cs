using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

/// <summary>
/// 海洞项目专用教程管理器 —— 按玩家描述的真实流程
///
/// 步骤：
///   0 欢迎
///   1 玩家点工具栏【指定按钮】 → 打开珊瑚UI面板 （监听 LocalTimeManager.OnButtonClicked）
///   2 玩家拖【指定可放置珊瑚图标】到场景石头上 （监听 DragAndPlaceSystem.OnCoralPlaced）
///   3 等待鱼产宝 （监听 鱼运动.OnDropProduced）
///   4 玩家点击拾取掉落物 （监听 DropItem.OnItemCollected）
///   5 玩家点工具栏按钮 → 再次打开珊瑚UI （监听 LocalTimeManager.OnButtonClicked）
///   6 鼠标悬停气泡上的珊瑚图像 → 看数字增加 （监听 HoverTooltip.OnHoverEnter）
///   7 完成
///
/// UI 由代码自动生成，挂到任意常驻物体即可
/// 首次启动自动播放，之后用 PlayerPrefs 跳过
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    // ============= 用户配置 =============

    [Header("哪个按钮是珊瑚UI开关")]
    [Tooltip("LocalTimeManager.buttonPairs 数组里哪个按钮是打开'珊瑚UI'的")]
    public int coralButtonIndex = 0;

    [Header("存档")]
    public string saveKey = "haidong_tutorial_done_v2";

    [Header("自动启动")]
    public bool autoStart = true;

    // ============= 运行时 =============

    private int currentStep = -1;
    private bool isPlaying;
    private bool stepContinuePressed;

    // 事件标志
    private bool evtButtonClicked, evtCoralPlaced, evtDropProduced;
    private bool evtDropCollected, evtHoverEntered;

    // UI
    private Canvas canvas;
    private Image darkenOverlay;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI descText;
    private Button continueBtn;
    private Button skipBtn;
    private RectTransform highlightSpot;

    // ============= 生命周期 =============

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (!autoStart) return;
        if (PlayerPrefs.GetInt(saveKey, 0) == 1)
        {
            Debug.Log("[TutorialManager] 已完成，跳过");
            enabled = false;
            return;
        }

        BuildUI();
        SubscribeEvents();
        StartCoroutine(DelayedStart());
    }

    IEnumerator DelayedStart() { yield return new WaitForSeconds(1f); StartTutorial(); }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeEvents();
    }

    // ============= 公开 API =============

    public void StartTutorial()
    {
        if (isPlaying) return;
        isPlaying = true;
        currentStep = -1;
        ShowNextStep();
    }

    public void ForceRestart()
    {
        PlayerPrefs.DeleteKey(saveKey);
        PlayerPrefs.Save();
        if (!isPlaying) { BuildUI(); SubscribeEvents(); }
        StartTutorial();
    }

    public bool IsPlaying => isPlaying;

    // ============= 事件桥接 =============

    void SubscribeEvents()
    {
        LocalTimeManager.OnButtonClicked += idx => evtButtonClicked = true;
        DragAndPlaceSystem.OnCoralPlaced += _ => evtCoralPlaced = true;
        鱼运动.OnDropProduced += _ => evtDropProduced = true;
        DropItem.OnItemCollected += _ => evtDropCollected = true;
        HoverTooltip.OnHoverEnter += _ => evtHoverEntered = true;
    }

    void UnsubscribeEvents()
    {
        LocalTimeManager.OnButtonClicked -= idx => evtButtonClicked = true;
        DragAndPlaceSystem.OnCoralPlaced -= _ => evtCoralPlaced = true;
        鱼运动.OnDropProduced -= _ => evtDropProduced = true;
        DropItem.OnItemCollected -= _ => evtDropCollected = true;
        HoverTooltip.OnHoverEnter -= _ => evtHoverEntered = true;
    }

    // ============= 流程 =============

    void ShowNextStep()
    {
        currentStep++;
        if (currentStep > 7) { Finish(); return; }
        StartCoroutine(RunStep(currentStep));
    }

    void Finish()
    {
        isPlaying = false;
        if (canvas != null) canvas.gameObject.SetActive(false);
        PlayerPrefs.SetInt(saveKey, 1);
        PlayerPrefs.Save();
        Debug.Log("[TutorialManager] 教程完成");
    }

    void SkipAll()
    {
        isPlaying = false;
        if (canvas != null) canvas.gameObject.SetActive(false);
        UnsubscribeEvents();
        Debug.Log("[TutorialManager] 已跳过");
    }

    IEnumerator RunStep(int stepIdx)
    {
        // 重置事件标志
        evtButtonClicked = evtCoralPlaced = evtDropProduced = false;
        evtDropCollected = evtHoverEntered = false;
        stepContinuePressed = false;

        if (canvas != null) canvas.gameObject.SetActive(true);

        switch (stepIdx)
        {
            case 0: yield return StepWelcome(); break;
            case 1: yield return StepClickToolbarButton(); break;
            case 2: yield return StepDragCoral(); break;
            case 3: yield return StepWaitForDrop(); break;
            case 4: yield return StepCollectDrop(); break;
            case 5: yield return StepClickToolbarButtonAgain(); break;
            case 6: yield return StepHoverBubble(); break;
            case 7: yield return StepFinish(); break;
        }

        ShowNextStep();
    }

    // —— 每一步 ——

    IEnumerator StepWelcome()
    {
        ShowUI("欢迎来到海洞 🐠",
            "在这里你可以养鱼、装饰你的海底世界。\n\n让我带你认识最基本的操作吧～",
            showContinue: true);
        yield return WaitContinue();
    }

    // ========== 第 1 步：点工具栏按钮 ==========
    IEnumerator StepClickToolbarButton()
    {
        ShowUI("第 1 步：打开珊瑚面板",
            "点击屏幕上方/下方工具栏上的珊瑚图标按钮，\n打开珊瑚放置面板。",
            showContinue: false);

        yield return new WaitUntil(() => evtButtonClicked);

        ShowUI("面板打开了！",
            "这就是珊瑚放置面板。\n接下来让我们往场景里放一株珊瑚～",
            showContinue: true);
        yield return WaitContinue();
    }

    // ========== 第 2 步：拖珊瑚到场景石头 ==========
    IEnumerator StepDragCoral()
    {
        ShowUI("第 2 步：拖拽珊瑚到场景",
            "在面板里点住珊瑚图标不松手，\n拖到场景底部带橙色边框的石头上松开。\n\n注意：要在石头范围内松手才能放成功！",
            showContinue: false);

        yield return new WaitUntil(() => evtCoralPlaced);

        ShowUI("放成功了！",
            "新珊瑚已经种下去了！\n等鱼群慢慢游过来，它们会开始产出宝物～",
            showContinue: true);
        yield return WaitContinue();
    }

    // ========== 第 3 步：等鱼产宝 ==========
    IEnumerator StepWaitForDrop()
    {
        ShowUI("第 3 步：等待鱼产宝",
            "鱼会定时产出宝物（珊瑚、珍珠、贝壳等），\n宝物从鱼身上掉到水底。\n\n耐心等一下～",
            showContinue: false);

        float start = Time.time;
        yield return new WaitUntil(() => evtDropProduced || Time.time - start > 90f);

        ShowUI("宝物掉下来了！",
            "看到场景里掉落的宝物了吗？\n快去捡起来吧～",
            showContinue: true);
        yield return WaitContinue();
    }

    // ========== 第 4 步：点击拾取 ==========
    IEnumerator StepCollectDrop()
    {
        ShowUI("第 4 步：点击拾取宝物",
            "直接点击场景里漂浮或掉落的宝物，\n就能把它加入你的收藏。\n\n试试看！",
            showContinue: false);

        yield return new WaitUntil(() => evtDropCollected);

        ShowUI("捡到了！",
            "宝物已经加进你的库存啦！\n现在让我们再次打开珊瑚面板看看～",
            showContinue: true);
        yield return WaitContinue();
    }

    // ========== 第 5 步：再次点工具栏按钮 ==========
    IEnumerator StepClickToolbarButtonAgain()
    {
        ShowUI("第 5 步：再次打开珊瑚面板",
            "再点一次工具栏上的珊瑚图标按钮，\n打开珊瑚放置面板。",
            showContinue: false);

        yield return new WaitUntil(() => evtButtonClicked);

        ShowUI("面板又打开了！",
            "最后一步，让我们看看你的收藏数量在哪里显示～",
            showContinue: true);
        yield return WaitContinue();
    }

    // ========== 第 6 步：悬停气泡看数字 ==========
    IEnumerator StepHoverBubble()
    {
        ShowUI("第 6 步：看看收藏数量",
            "把鼠标移到面板里（或场景顶部）\n显示珊瑚/珍珠/贝壳收藏数量的那个图标上。\n\n你刚才捡到的宝物数量就显示在这里！",
            showContinue: false);

        yield return new WaitUntil(() => evtHoverEntered);

        ShowUI("看到数字了吗？",
            "这就是你刚才捡到的宝物数量！\n每次鱼掉宝你都能在这里看到它变多～",
            showContinue: true);
        yield return WaitContinue();
    }

    // ========== 第 7 步：完成 ==========
    IEnumerator StepFinish()
    {
        ShowUI("🎉 教程完成！",
            "恭喜你学会了海洞的所有基本操作。\n尽情养鱼、装饰你的海底世界吧！",
            showContinue: true);
        yield return WaitContinue();
    }

    // ============= 辅助 =============

    IEnumerator WaitContinue()
    {
        continueBtn.gameObject.SetActive(true);
        stepContinuePressed = false;
        yield return new WaitUntil(() => stepContinuePressed);
        continueBtn.gameObject.SetActive(false);
    }

    void ShowUI(string title, string desc, bool showContinue)
    {
        titleText.text = title ?? "";
        descText.text = desc ?? "";
        titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
        descText.gameObject.SetActive(!string.IsNullOrEmpty(desc));
        continueBtn.gameObject.SetActive(showContinue);
    }

    // ============= UI 构建 =============

    void BuildUI()
    {
        var canvasGO = new GameObject("TutorialCanvas");
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var canvasRT = canvas.GetComponent<RectTransform>();
        canvasRT.anchorMin = Vector2.zero; canvasRT.anchorMax = Vector2.one;
        canvasRT.offsetMin = Vector2.zero; canvasRT.offsetMax = Vector2.zero;

        // 遮罩
        var darken = CreateImage(canvasGO.transform, "Darken", new Color(0, 0, 0, 0.7f));
        StretchFull(darken.rectTransform);

        // 标题
        titleText = CreateTMP(canvasGO.transform, "Title", "");
        titleText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        titleText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        titleText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        titleText.rectTransform.anchoredPosition = new Vector2(0, -140);
        titleText.rectTransform.sizeDelta = new Vector2(1600, 100);
        titleText.fontSize = 56;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;

        // 描述
        descText = CreateTMP(canvasGO.transform, "Description", "");
        descText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        descText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        descText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        descText.rectTransform.anchoredPosition = new Vector2(0, 40);
        descText.rectTransform.sizeDelta = new Vector2(1300, 320);
        descText.fontSize = 30;
        descText.alignment = TextAlignmentOptions.Center;
        descText.color = Color.white;

        // 继续按钮
        continueBtn = CreateButton(canvasGO.transform, "ContinueBtn", "继续", out var continueRT);
        continueRT.anchorMin = new Vector2(0.5f, 0f);
        continueRT.anchorMax = new Vector2(0.5f, 0f);
        continueRT.pivot = new Vector2(0.5f, 0f);
        continueRT.anchoredPosition = new Vector2(0, 240);
        continueRT.sizeDelta = new Vector2(220, 72);
        continueBtn.onClick.AddListener(() => stepContinuePressed = true);

        // 跳过按钮
        skipBtn = CreateButton(canvasGO.transform, "SkipBtn", "跳过教程", out var skipRT);
        skipRT.anchorMin = new Vector2(1f, 1f);
        skipRT.anchorMax = new Vector2(1f, 1f);
        skipRT.pivot = new Vector2(1f, 1f);
        skipRT.anchoredPosition = new Vector2(-32, -32);
        skipRT.sizeDelta = new Vector2(160, 56);
        skipBtn.onClick.AddListener(() => SkipAll());

        // 高亮聚光灯
        highlightSpot = CreateImage(canvasGO.transform, "Highlight", new Color(1f, 1f, 1f, 0.15f)).rectTransform;
        highlightSpot.anchorMin = new Vector2(0.5f, 0.5f);
        highlightSpot.anchorMax = new Vector2(0.5f, 0.5f);
        highlightSpot.pivot = new Vector2(0.5f, 0.5f);
        highlightSpot.sizeDelta = new Vector2(240, 240);
        highlightSpot.gameObject.SetActive(false);

        canvasGO.SetActive(false);
    }

    Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    TextMeshProUGUI CreateTMP(Transform parent, string name, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        return tmp;
    }

    Button CreateButton(Transform parent, string name, string label, out RectTransform rtOut)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.16f, 0.55f, 0.88f, 1f);
        var btn = go.AddComponent<Button>();

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 28;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        var lrt = tmp.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

        rtOut = btn.GetComponent<RectTransform>();
        return btn;
    }

    void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
