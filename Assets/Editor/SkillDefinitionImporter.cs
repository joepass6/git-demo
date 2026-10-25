using System;
using System.Collections.Generic;
using System.IO;
using SkillSystem;
using UnityEditor;
using UnityEngine;

public static class SkillDefinitionImporter
{
    [MenuItem("Tools/导入珊瑚技能定义", priority = 100)]
    public static void Import()
    {
        Debug.Log("=== 技能导入器开始 ===");
        Debug.Log("SkillDefinition 类型: " + typeof(SkillDefinition));
        Debug.Log("SkillVariable 枚举: " + Enum.GetNames(typeof(SkillVariable)));
        
        string jsonPath = "Assets/Editor/skill_import_data.json";
        if (!File.Exists(jsonPath))
        {
            EditorUtility.DisplayDialog("错误", "找不到 " + jsonPath, "OK");
            return;
        }

        string json = File.ReadAllText(jsonPath);
        RecordListWrapper wrapper = JsonUtility.FromJson<RecordListWrapper>("{\"items\":" + json + "}");
        if (wrapper == null || wrapper.items == null || wrapper.items.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "JSON 解析失败或为空", "OK");
            return;
        }

        Debug.Log("读取到 " + wrapper.items.Count + " 条记录");

        string outputFolder = "Assets/ScriptableObjects/技能定义";
        EnsureFolderExists(outputFolder);

        int created = 0;
        foreach (Record r in wrapper.items)
        {
            if (r.effect1 == null && r.effect2 == null) continue;

            SkillDefinition skill = ScriptableObject.CreateInstance<SkillDefinition>();
            skill.skillName = r.skillName;

            if (r.effect1 != null)
            {
                SkillVariable v = ParseVar(r.effect1.variable);
                if (v != SkillVariable.无)
                {
                    skill.effect1.variable = v;
                    skill.effect1.amount = r.effect1.amount;
                    skill.effect1.target = SkillTargetType.同石头所有珊瑚;
                    skill.effect1.targetName = "";
                }
            }

            if (r.effect2 != null)
            {
                SkillVariable v = ParseVar(r.effect2.variable);
                if (v != SkillVariable.无)
                {
                    skill.effect2.variable = v;
                    skill.effect2.amount = r.effect2.amount;
                    skill.effect2.target = SkillTargetType.同石头所有珊瑚;
                    skill.effect2.targetName = "";
                }
            }

            string path = outputFolder + "/技能定义_" + r.coralName + ".asset";
            AssetDatabase.CreateAsset(skill, path);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("=== 完成: 创建 " + created + " 个 SkillDefinition ===");
        EditorUtility.DisplayDialog("完成", "已创建 " + created + " 个技能定义\n保存在 " + outputFolder, "OK");
    }

    static SkillVariable ParseVar(string name)
    {
        if (string.IsNullOrEmpty(name)) return SkillVariable.无;
        switch (name)
        {
            case "鱼的数量": return SkillVariable.鱼的数量;
            case "一号鱼概率": return SkillVariable.一号鱼概率;
            case "二号鱼概率": return SkillVariable.二号鱼概率;
            case "三号鱼概率": return SkillVariable.三号鱼概率;
            case "四号鱼概率": return SkillVariable.四号鱼概率;
            case "鱼的存在时间": return SkillVariable.鱼的存在时间;
            case "掉落物数量": return SkillVariable.掉落物数量;
            default: return SkillVariable.无;
        }
    }

    static void EnsureFolderExists(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;
        string[] parts = assetPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    [Serializable] class Record {
        public string coralName;
        public string skillName;
        public EffectData effect1;
        public EffectData effect2;
    }

    [Serializable] class EffectData {
        public string variable;
        public float amount;
    }

    [Serializable] class RecordListWrapper {
        public List<Record> items;
    }
}
