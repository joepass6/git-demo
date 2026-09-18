using UnityEngine;

namespace SkillSystem
{
    /// <summary>
    /// 技能能修改的 7 个变量（对应珊瑚技能表格的列）
    /// </summary>
    public enum SkillVariable
    {
        无 = 0,
        鱼的数量 = 1,
        一号鱼概率 = 2,
        二号鱼概率 = 3,
        三号鱼概率 = 4,
        四号鱼概率 = 5,
        鱼的存在时间 = 6,   // 单位：秒
        掉落物数量 = 7
    }

    /// <summary>
    /// 一个技能效果作用的目标范围
    /// </summary>
    public enum SkillTargetType
    {
        同石头所有珊瑚 = 0,
        只作用于自己 = 1,
        指定名称的珊瑚 = 2   // 匹配目标物体名字（包含即可）
    }

    /// <summary>
    /// 一个效果槽：改哪个变量 + 作用于谁 + 变化多少。
    /// 全是普通可序列化字段，Inspector 里就是下拉框和数字框。
    /// </summary>
    [System.Serializable]
    public class SkillEffectEntry
    {
        [Tooltip("要修改的变量，选'无'则此槽不生效")]
        public SkillVariable variable = SkillVariable.无;

        [Tooltip("作用于哪些珊瑚")]
        public SkillTargetType target = SkillTargetType.同石头所有珊瑚;

        [Tooltip("仅当目标=指定名称的珊瑚时使用，填珊瑚名字（包含即可，如：澳大利亚沙群海葵）")]
        public string targetName = "";

        [Tooltip("变化量：正数=增加，负数=降低。\n鱼数量/概率权重/掉落物填整数（1、-1）；存在时间填秒（60、-60）")]
        public float amount = 0f;

        /// <summary>这个槽是否有效</summary>
        public bool IsValid => variable != SkillVariable.无;
    }

    /// <summary>
    /// 技能定义（ScriptableObject 资产）—— 技能系统的配置核心，与预制体解耦。
    ///
    /// 创建：Project 窗口右键 → 创建 → 珊瑚牧歌 → 技能定义
    /// 用法：技能施放器(SkillCaster) 组件引用一个技能定义；同一个定义可被多个物体复用。
    /// </summary>
    [CreateAssetMenu(fileName = "技能定义_", menuName = "珊瑚牧歌/技能定义")]
    public class SkillDefinition : ScriptableObject
    {
        [Tooltip("技能名称（仅日志显示用）")]
        public string skillName = "";

        [Space(6)]
        [Tooltip("放置/触发时的第 1 个变量变化")]
        public SkillEffectEntry effect1 = new SkillEffectEntry();

        [Tooltip("放置/触发时的第 2 个变量变化")]
        public SkillEffectEntry effect2 = new SkillEffectEntry();

        /// <summary>遍历全部有效效果槽</summary>
        public System.Collections.Generic.IEnumerable<SkillEffectEntry> Effects
        {
            get
            {
                if (effect1 != null && effect1.IsValid) yield return effect1;
                if (effect2 != null && effect2.IsValid) yield return effect2;
            }
        }
    }
}
