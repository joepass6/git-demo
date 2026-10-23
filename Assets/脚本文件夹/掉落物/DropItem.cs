using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class DropItem : MonoBehaviour
{
    /// <summary>掉落物被玩家拾取时触发（教程用）</summary>
    public static event System.Action<DropItem> OnItemCollected;

    [Header("物品设置")]
    public string itemType = "珊瑚";
    public int stackCount = 1;
    public int maxStack = 99;
    public TextMeshProUGUI countText;

    [Header("性能设置")]
    [Tooltip("这个掉落物自动销毁时间（秒），0=永不自动销毁")]
    public float lifetime = 30f;

    [Tooltip("全局最大掉落物数量（所有种类共享），超过自动销毁最旧的")]
    public static int maxGlobalDropCount = 50;

    // 按生成顺序的全局队列
    private static Queue<DropItem> aliveDrops = new Queue<DropItem>();
    private bool inQueue = false;

    // ========== 多宝物计数静态 ==========
    private static Dictionary<string, int> _counts = new Dictionary<string, int>();
    private static Dictionary<string, List<TextMeshProUGUI>> _counters = new Dictionary<string, List<TextMeshProUGUI>>();

    public static void RegisterCounter(string type, TextMeshProUGUI text)
    {
        RegisterCounter(type, text, null);
    }

    public static void RegisterCounter(string type, TextMeshProUGUI text, string format)
    {
        if (string.IsNullOrEmpty(type) || text == null) return;
        if (!_counters.ContainsKey(type)) _counters[type] = new List<TextMeshProUGUI>();
        if (!_counters[type].Contains(text)) _counters[type].Add(text);
        UpdateUIText(type);
    }

    public static void UnregisterCounter(string type, TextMeshProUGUI text)
    {
        if (string.IsNullOrEmpty(type) || text == null) return;
        if (_counters.ContainsKey(type)) _counters[type].Remove(text);
    }

    public static int GetCount(string type) { return _counts.ContainsKey(type) ? _counts[type] : 0; }
    public static void SetCount(string type, int value) { _counts[type] = Mathf.Max(0, value); UpdateUIText(type); }
    public static void AddCount(string type, int add) { SetCount(type, GetCount(type) + add); }

    static void UpdateUIText(string type)
    {
        if (!_counts.ContainsKey(type)) _counts[type] = 0;
        if (!_counters.ContainsKey(type)) return;
        foreach (var text in _counters[type])
        {
            if (text != null) text.text = _counts[type].ToString();
        }
    }

    // ========== 实例逻辑 ==========
    private float spawnTime;
    private Collider2D col;
    private Camera cachedCam;

    private void Awake()
    {
        spawnTime = Time.time;
        col = GetComponent<Collider2D>();

        if (lifetime > 0) Destroy(gameObject, lifetime);

        // ===== 全局掉落物数量上限 =====
        aliveDrops.Enqueue(this);
        inQueue = true;

        // 顺便清理队列中已销毁的引用（长时间运行后队列不会无限膨胀）
        PurgeDestroyedDrops();

        while (aliveDrops.Count > maxGlobalDropCount && aliveDrops.Count > 0)
        {
            DropItem oldest = aliveDrops.Dequeue();
            if (oldest != null && oldest != this && oldest.gameObject != null)
            {
                oldest.inQueue = false;
                Destroy(oldest.gameObject);
            }
        }
    }

    // 清理队列中已被 Destroy 的 DropItem 引用
    private static void PurgeDestroyedDrops()
    {
        int n = aliveDrops.Count;
        for (int i = 0; i < n; i++)
        {
            DropItem d = aliveDrops.Dequeue();
            if (d != null && d.gameObject != null) aliveDrops.Enqueue(d);
        }
    }

    private void OnDestroy()
    {
        // 标记已出队，下次 Purge 时会被清理
        inQueue = false;
    }

    // ================= 碰撞合并 =================
    private void OnCollisionEnter2D(Collision2D collision)
    {
        Collider2D other = collision.collider;
        if (other == null) return;

        DropItem otherItem = other.GetComponent<DropItem>();
        if (otherItem != null && otherItem.itemType == itemType)
        {
            if (otherItem.stackCount < maxStack && spawnTime < otherItem.spawnTime)
            {
                int available = maxStack - stackCount;
                int moved = Mathf.Min(otherItem.stackCount, available);
                stackCount += moved;
                otherItem.stackCount -= moved;
                UpdateCount();
                if (otherItem.stackCount <= 0) Destroy(otherItem.gameObject);
            }
            if (col != null) Physics2D.IgnoreCollision(col, other, true);
            return;
        }

        if (!other.CompareTag("Obstacle") && col != null)
        {
            Physics2D.IgnoreCollision(col, other, true);
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.collider != null && !collision.collider.CompareTag("Obstacle") && col != null)
        {
            Physics2D.IgnoreCollision(col, collision.collider, true);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;
        DropItem otherItem = other.GetComponent<DropItem>();
        if (!otherItem || otherItem.itemType != itemType) return;
        if (otherItem.stackCount >= maxStack) return;
        if (spawnTime >= otherItem.spawnTime) return;

        int available = maxStack - stackCount;
        int moved = Mathf.Min(otherItem.stackCount, available);
        stackCount += moved;
        otherItem.stackCount -= moved;
        UpdateCount();
        if (otherItem.stackCount <= 0) Destroy(otherItem.gameObject);
    }

    private void UpdateCount()
    {
        if (countText != null) countText.text = stackCount > 1 ? $"x{stackCount}" : "";
    }

    // ================= 点击拾取 =================
    private void Update()
    {
        if (MoveSystem.isInMoveMode) return; // ===== 搬家模式下禁止拾取 =====
        if (Input.GetMouseButtonDown(0))
        {
            if (IsPointerOverUI()) return;
            if (col == null) col = GetComponent<Collider2D>();
            if (col == null) return;

            if (cachedCam == null) cachedCam = Camera.main;
            if (cachedCam == null) return;

            Vector2 mousePos = cachedCam.ScreenToWorldPoint(Input.mousePosition);
            if (col.OverlapPoint(mousePos)) OnClickCollect();
        }
    }

    private void OnMouseDown()
    {
        if (MoveSystem.isInMoveMode) return; // ===== 搬家模式下禁止拾取 =====
        if (IsPointerOverUI()) return;
        OnClickCollect();
    }

    bool IsPointerOverUI()
    {
        if (UnityEngine.EventSystems.EventSystem.current == null) return false;
        return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    void OnClickCollect()
    {
        AddCount(itemType, stackCount);
        Debug.Log($"[DropItem] 收集 {itemType} x{stackCount}, 总数={GetCount(itemType)}");
        Destroy(gameObject);
    }
}