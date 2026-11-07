using UnityEngine;
using System.Collections.Generic;

namespace BuildingSystem
{
    /// <summary>
    /// 建筑注册中心 —— 管理所有 BuildingData 的单例
    ///
    /// 用途：
    ///   - 在游戏启动时自动从 Resources/建筑数据 加载所有 BuildingData 资产
    ///   - 提供按 ID、按名称查找建筑的统一接口
    ///   - BuildingPlacer、UI面板等所有需要查建筑数据的地方都走这里
    ///
    /// 使用：BuildingRegistry.Instance.TryGetBuilding(id, out data)
    /// </summary>
    public class BuildingRegistry : MonoBehaviour
    {
        public static BuildingRegistry Instance { get; private set; }

        [Header("自动扫描路径")]
        [Tooltip("运行时自动扫描 Resources 下这个路径的所有 BuildingData 资产。也可以手动填到下方列表")]
        public string resourcesScanPath = "建筑数据";

        [Header("手动注册（可选）")]
        [Tooltip("如果不想用 Resources 自动加载，可以手动把 BuildingData 拖进这里")]
        public List<BuildingData> manualEntries = new List<BuildingData>();

        // 内部索引
        readonly Dictionary<int, BuildingData> _byId = new Dictionary<int, BuildingData>();
        readonly Dictionary<string, BuildingData> _byName = new Dictionary<string, BuildingData>();

        /// <summary>已注册的所有建筑数据集合（只读）</summary>
        public IReadOnlyCollection<BuildingData> AllBuildings => _byId.Values;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ScanAndRegister();
        }

        /// <summary>
        /// 扫描 Resources + 手动列表，建立 ID/名称 索引
        /// </summary>
        void ScanAndRegister()
        {
            _byId.Clear();
            _byName.Clear();

            // 1. Resources 自动加载（如果 Resources 目录下有 buildingData 资产）
            if (!string.IsNullOrEmpty(resourcesScanPath))
            {
                var assets = Resources.LoadAll<BuildingData>(resourcesScanPath);
                foreach (var data in assets)
                {
                    if (data != null) Register(data);
                }
            }

            // 2. 手动列表补充
            foreach (var data in manualEntries)
            {
                if (data != null) Register(data);
            }

            Debug.Log($"[BuildingRegistry] 注册完成，共 {_byId.Count} 种建筑");
        }

        /// <summary>
        /// 运行时手动注册一个建筑数据（动态增删场景里的建筑时用）
        /// </summary>
        public void Register(BuildingData data)
        {
            if (data == null) return;

            if (_byId.ContainsKey(data.buildingId))
            {
                Debug.LogWarning($"[BuildingRegistry] 建筑ID重复: {data.buildingId} → {data.displayName}，跳过注册");
                return;
            }

            _byId[data.buildingId] = data;
            if (!string.IsNullOrEmpty(data.displayName))
                _byName[data.displayName] = data;
        }

        /// <summary>
        /// 运行时手动注销一个建筑数据
        /// </summary>
        public void Unregister(BuildingData data)
        {
            if (data == null) return;
            _byId.Remove(data.buildingId);
            if (!string.IsNullOrEmpty(data.displayName))
                _byName.Remove(data.displayName);
        }

        // ==================== 查找接口 ====================

        /// <summary>
        /// 按ID查找建筑，找不到返回 false（最常用的接口）
        /// 解决 BuildingPlacer.cs 之前 CS1061 报错的方法签名
        /// </summary>
        public bool TryGetBuilding(int id, out BuildingData building)
        {
            return _byId.TryGetValue(id, out building);
        }

        /// <summary>
        /// 按ID查找建筑，找不到返回 null（简写版）
        /// </summary>
        public BuildingData GetBuilding(int id)
        {
            _byId.TryGetValue(id, out var data);
            return data;
        }

        /// <summary>
        /// 按名称查找建筑，找不到返回 null
        /// </summary>
        public BuildingData GetBuildingByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _byName.TryGetValue(name, out var data);
            return data;
        }

        /// <summary>
        /// 判断某建筑ID是否已注册
        /// </summary>
        public bool IsRegistered(int id) => _byId.ContainsKey(id);
    }
}
