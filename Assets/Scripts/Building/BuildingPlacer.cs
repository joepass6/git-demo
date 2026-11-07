using UnityEngine;

namespace BuildingSystem
{
    /// <summary>
    /// 建筑放置控制器 —— 基于 BuildingRegistry 的放置接口层
    ///
    /// 和现有 DragAndPlaceSystem 的关系：
    ///   BuildingPlacer 是更薄的一层接口，专注于"知道放什么"（查Registry → 拿数据 → 实例化）
    ///   DragAndPlaceSystem 是更完整的拖拽交互系统（UI拖拽、预览体、碰撞检测、石头容量等）
    ///
    /// 两种典型用法：
    ///   1. 代码一键放置：  BuildingPlacer.Instance.Place(buildingId, position);
    ///   2. 替换 DragAndPlaceSystem 的硬编码 prefab：让 DragAndPlaceSystem 从 Registry 查预制体
    /// </summary>
    public class BuildingPlacer : MonoBehaviour
    {
        // 简洁的静态入口，方便任何地方一行代码调 Place()
        public static BuildingPlacer Instance { get; private set; }

        [Header("技能默认目标（可选）")]
        [Tooltip("放置后触发技能时作用的目标物体（例如主石头）。可以留空让 Place 重载手动传 target")]
        public Transform defaultSkillTarget;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ==================== Place 重载族 ====================

        /// <summary>按建筑ID在指定位置放置（最常用入口）</summary>
        public GameObject Place(int buildingId, Vector3 position)
        {
            return Place(buildingId, position, Quaternion.identity, null);
        }

        /// <summary>按建筑ID在指定位置放置（带旋转）</summary>
        public GameObject Place(int buildingId, Vector3 position, Quaternion rotation)
        {
            return Place(buildingId, position, rotation, null);
        }

        /// <summary>按建筑ID放置，同时指定技能目标（如石头GameObject）</summary>
        public GameObject Place(int buildingId, Vector3 position, GameObject skillTarget)
        {
            return Place(buildingId, position, Quaternion.identity, skillTarget);
        }

        /// <summary>完整参数：按ID放置 + 旋转 + 技能目标</summary>
        public GameObject Place(int buildingId, Vector3 position, Quaternion rotation, GameObject skillTarget)
        {
            if (!EnsureRegistry()) return null;
            if (!BuildingRegistry.Instance.TryGetBuilding(buildingId, out var data))
            {
                Debug.LogError($"[BuildingPlacer] 找不到建筑ID={buildingId}！请检查 BuildingRegistry 是否已注册");
                return null;
            }
            return PlaceInternal(data, position, rotation, skillTarget);
        }

        /// <summary>直接传 BuildingData 放置（跳过 Registry 查找，适合高频调用或已经拿到 data 的场景）</summary>
        public GameObject Place(BuildingData data, Vector3 position, Quaternion rotation, GameObject skillTarget = null)
        {
            if (data == null) { Debug.LogError("[BuildingPlacer] BuildingData 为空"); return null; }
            return PlaceInternal(data, position, rotation, skillTarget);
        }

        // ==================== 内部实现 ====================

        GameObject PlaceInternal(BuildingData data, Vector3 position, Quaternion rotation, GameObject skillTarget)
        {
            if (data.prefab == null)
            {
                Debug.LogError($"[BuildingPlacer] 建筑 [{data.displayName}] 的 prefab 未配置！");
                return null;
            }

            GameObject instance = Instantiate(data.prefab, position, rotation);
            Debug.Log($"[BuildingPlacer] 放置成功: {data.displayName} (ID={data.buildingId}) @ {position}");

            // 触发技能（如果数据上配了 + 预制体上挂了 SkillCaster）
            if (data.skillDefinition != null)
            {
                var caster = instance.GetComponent<SkillSystem.SkillCaster>();
                if (caster != null)
                {
                    var target = skillTarget != null ? skillTarget
                        : defaultSkillTarget != null ? defaultSkillTarget.gameObject
                        : null;
                    if (target != null)
                    {
                        try { caster.Cast(target); }
                        catch (System.Exception e) { Debug.LogError($"[BuildingPlacer] 技能施放失败: {e.Message}"); }
                    }
                }
            }

            return instance;
        }

        /// <summary>检查 BuildingRegistry 是否就绪（所有 Place 重载的前置校验）</summary>
        bool EnsureRegistry()
        {
            if (BuildingRegistry.Instance == null)
            {
                Debug.LogError("[BuildingPlacer] BuildingRegistry 单例不存在！请确保场景里有 BuildingRegistry");
                return false;
            }
            return true;
        }
    }
}
