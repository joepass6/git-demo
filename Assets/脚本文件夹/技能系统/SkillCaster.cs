using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem
{
    /// <summary>
    /// 技能施放器 —— 技能系统唯一需要挂在物体上的组件。
    /// 任何 GameObject（珊瑚、道具、机关……）挂上它并指定一个技能定义资产，就拥有该技能。
    ///
    /// 用法：
    ///   1. Project 右键 → 创建 → 珊瑚牧歌 → 技能定义，配好两个效果槽
    ///   2. 珊瑚预制体 Add Component → Skill Caster（技能施放器），把资产拖进 Skill
    ///   3. 业务代码（如珊瑚放置）在合适时机调用 Cast(石头对象)
    /// </summary>
    public class SkillCaster : MonoBehaviour
    {
        [Tooltip("要施放的技能定义资产（右键创建：珊瑚牧歌/技能定义）")]
        public SkillDefinition skill;

        [Tooltip("只影响同石头上带此标签的物体；留空=不按标签筛选")]
        public string coralTag = "coral";

        /// <summary>
        /// 施放技能
        /// </summary>
        /// <param name="rock">技能作用的石头/区域（用来圈定同区域目标）</param>
        public void Cast(GameObject rock)
        {
            if (skill == null)
            {
                Debug.LogWarning($"[技能] {gameObject.name} 的 SkillCaster 没有指定技能定义");
                return;
            }

            var self = GetComponent<ObjectSpawner>();
            var candidates = ResolveTargets(rock, self);

            foreach (var entry in skill.Effects)
            {
                foreach (var target in candidates)
                {
                    if (!MatchTarget(entry, target, self)) continue;
                    SkillExecutor.Apply(entry, target, skill.skillName);
                }
            }
        }

        // ==================== 目标解析 ====================

        /// <summary>同石头候选：碰撞体重叠 + 标签筛选，始终包含自己</summary>
        List<ObjectSpawner> ResolveTargets(GameObject rock, ObjectSpawner self)
        {
            var result = new List<ObjectSpawner>();
            if (rock == null)
            {
                if (self != null) result.Add(self);
                return result;
            }

            Collider2D rockCol = rock.GetComponent<Collider2D>();
            if (rockCol == null) rockCol = rock.GetComponentInChildren<Collider2D>();

            foreach (var sp in FindObjectsOfType<ObjectSpawner>())
            {
                if (sp == null) continue;
                if (!string.IsNullOrEmpty(coralTag) && sp.gameObject.tag != coralTag) continue;

                if (rockCol != null)
                {
                    Collider2D c = sp.GetComponent<Collider2D>();
                    if (c == null) c = sp.GetComponentInChildren<Collider2D>();
                    // 自己或与石头重叠的才算同区域
                    bool onRock = (self != null && sp == self) || (c != null && c.bounds.Intersects(rockCol.bounds));
                    if (!onRock) continue;
                }

                if (!result.Contains(sp)) result.Add(sp);
            }

            if (self != null && !result.Contains(self)) result.Add(self);
            return result;
        }

        bool MatchTarget(SkillEffectEntry entry, ObjectSpawner candidate, ObjectSpawner self)
        {
            switch (entry.target)
            {
                case SkillTargetType.只作用于自己:
                    return candidate == self;
                case SkillTargetType.指定名称的珊瑚:
                    return SkillExecutor.GetTargetName(candidate).Contains(entry.targetName ?? "");
                case SkillTargetType.同石头所有珊瑚:
                default:
                    return true;
            }
        }
    }
}
