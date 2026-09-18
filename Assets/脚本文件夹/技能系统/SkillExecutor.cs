using UnityEngine;

namespace SkillSystem
{
    /// <summary>
    /// 技能效果执行器（无状态静态核心）—— 只负责"把一个效果槽真正作用到一个目标生成器上"。
    /// 不关心是谁施放、目标怎么找，与 Unity 组件完全解耦，方便单元测试和以后复用。
    /// </summary>
    public static class SkillExecutor
    {
        /// <summary>把单个效果槽应用到一个目标 ObjectSpawner</summary>
        public static void Apply(SkillEffectEntry entry, ObjectSpawner target, string skillName)
        {
            if (entry == null || !entry.IsValid || target == null) return;

            switch (entry.variable)
            {
                case SkillVariable.鱼的数量:
                {
                    int v = Mathf.RoundToInt(entry.amount);
                    int before = target.spawnCount;
                    target.spawnCount = Mathf.Max(0, before + v);
                    Log(skillName, entry, target, $"鱼数量 {before} → {target.spawnCount}");
                    break;
                }

                case SkillVariable.一号鱼概率:
                case SkillVariable.二号鱼概率:
                case SkillVariable.三号鱼概率:
                case SkillVariable.四号鱼概率:
                {
                    int kindId = (int)entry.variable - 1;   // 枚举 2~5 → 鱼编号 1~4
                    var spawnEntry = target.GetSpawnEntryById(kindId);
                    if (spawnEntry == null)
                    {
                        Debug.LogWarning($"[技能:{skillName}] {target.gameObject.name} 没有编号为{kindId}的鱼条目");
                        return;
                    }
                    float beforeW = spawnEntry.weight;
                    spawnEntry.weight = Mathf.Max(0f, beforeW + entry.amount);
                    Log(skillName, entry, target, $"{kindId}号鱼权重 {beforeW} → {spawnEntry.weight}");
                    break;
                }

                case SkillVariable.鱼的存在时间:
                {
                    // 永久修正：之后生成/对象池复用的鱼按 基础值+修正
                    target.bonusDestroyTime += entry.amount;
                    // 即时生效：场上已有的鱼立刻加/减寿命（减到过期会马上回池）
                    var aliveFish = target.GetSpawnedFish();
                    foreach (var fish in aliveFish)
                        if (fish != null) fish.ExtendLife(entry.amount);
                    Log(skillName, entry, target, $"寿命修正累计 {target.bonusDestroyTime}秒，影响 {aliveFish.Count} 条存活鱼");
                    break;
                }

                case SkillVariable.掉落物数量:
                {
                    int v = Mathf.RoundToInt(entry.amount);
                    // 永久修正
                    target.bonusDropCount = Mathf.Max(0, target.bonusDropCount + v);
                    // 即时生效
                    var aliveFish = target.GetSpawnedFish();
                    foreach (var fish in aliveFish)
                        if (fish != null) fish.dropCount = Mathf.Max(0, fish.dropCount + v);
                    Log(skillName, entry, target, $"掉落数修正累计 {target.bonusDropCount}，影响 {aliveFish.Count} 条存活鱼");
                    break;
                }
            }
        }

        /// <summary>目标匹配用名：用 GameObject 名字（预制体名去掉 Clone 后缀后包含匹配）</summary>
        public static string GetTargetName(ObjectSpawner spawner)
        {
            if (spawner == null) return "";
            return spawner.gameObject.name;
        }

        static void Log(string skillName, SkillEffectEntry entry, ObjectSpawner target, string result)
        {
            Debug.Log($"[技能:{skillName}] {entry.variable} {entry.amount:+0;-0;0} → {target.gameObject.name}：{result}");
        }
    }
}
