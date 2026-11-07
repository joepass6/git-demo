using UnityEngine;
using SkillSystem;

namespace BuildingSystem
{
    /// <summary>
    /// 建筑数据 —— 建筑系统的核心配置 ScriptableObject
    ///
    /// 用途：
    ///   - 存储一种建筑（珊瑚/海葵/教堂/沉船等）的静态配置
    ///   - 与预制体解耦：同一个 BuildingData 可以被多个预制体复用
    ///   - 可以关联 SkillDefinition 来复用现有技能系统
    ///
    /// 创建：Project窗口右键 → 创建 → 建筑系统 → 建筑数据
    /// </summary>
    [CreateAssetMenu(fileName = "建筑数据_", menuName = "建筑系统/建筑数据")]
    public class BuildingData : ScriptableObject
    {
        [Header("基础信息")]
        [Tooltip("建筑唯一ID（BuildingRegistry 用来查找的主键，不能重复）")]
        public int buildingId;

        [Tooltip("建筑显示名称（中文，仅日志/调试用）")]
        public string displayName = "";

        [Tooltip("建筑预制体（拖拽放置时实例化的那个）")]
        public GameObject prefab;

        [Header("消耗配置")]
        [Tooltip("放置这个建筑需要消耗的珊瑚/道具数量")]
        public int cost = 1;

        [Header("技能关联（可选）")]
        [Tooltip("关联的技能定义（放置时自动触发，不填则不触发技能）")]
        public SkillDefinition skillDefinition;
    }
}
