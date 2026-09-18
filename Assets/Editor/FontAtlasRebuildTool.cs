#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 字体图集一键重建工具（SDF 高清版，绕过 Font Asset Creator 窗口）
/// 菜单：工具/字体/重建字体图集（SDF高清版）
/// 方案：用官方 CreateFontAsset 全新生成（36px SDFAA / padding3 / 2048图集，
/// 1104个汉字也能装下），再原位覆盖回 VonwaonBitmap SDF.asset，
/// 保留原 .meta(GUID)，场景与预制体里的字体引用不会断。
/// </summary>
public static class FontAtlasRebuildTool
{
    const string TtfPath = "Assets/素材/UI/字体/VonwaonBitmap.ttf";
    const string SdfPath = "Assets/素材/UI/字体/VonwaonBitmap SDF.asset";
    const string CharListPath = "Assets/字体字符清单.txt";
    const string BackupPath = "VonwaonBitmap SDF 旧版备份.asset"; // 放在项目根目录(Assets外)，不会被导入

    const int SamplingSize = 36;   // SDF 采样字号：12~48 的 UI 字号都清晰
    const int Padding = 3;
    const int AtlasSize = 2048;    // 1104 字 × 36px 约占图集一半，留足余量

    [MenuItem("工具/字体/重建字体图集（SDF高清版）")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
        if (font == null)
        {
            EditorUtility.DisplayDialog("字体重建失败", $"找不到源字体：\n{TtfPath}", "确定");
            return;
        }

        string charsPath = Path.Combine(Application.dataPath, "字体字符清单.txt");
        if (!File.Exists(charsPath))
        {
            EditorUtility.DisplayDialog("字体重建失败", "找不到字符清单：Assets/字体字符清单.txt", "确定");
            return;
        }
        string characters = File.ReadAllText(charsPath).Trim();

        // ---------- 1. 反射拿到 GlyphRenderMode.SDFAA（按类型名全局查找，不猜命名空间） ----------
        Type grmType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types ?? Type.EmptyTypes; }
            Type found = types.FirstOrDefault(t => t != null && t.Name == "GlyphRenderMode" && t.IsEnum);
            if (found != null) { grmType = found; break; }
        }
        if (grmType == null)
        {
            EditorUtility.DisplayDialog("字体重建失败", "找不到 GlyphRenderMode 类型（TextCore 模块异常）。", "确定");
            return;
        }
        Debug.Log($"[字体重建] GlyphRenderMode 全名：{grmType.FullName}");
        object sdfaa = Enum.Parse(grmType, "SDFAA");

        // 按参数形状找 8 参数的 CreateFontAsset
        MethodInfo createMi = typeof(TMP_FontAsset).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "CreateFontAsset"
                && m.GetParameters().Length == 8
                && m.GetParameters()[3].ParameterType == grmType);
        if (createMi == null)
        {
            EditorUtility.DisplayDialog("字体重建失败", "找不到 TMP_FontAsset.CreateFontAsset 方法签名。", "确定");
            return;
        }

        // ---------- 2. 官方管线全新生成（度量/材质/图集全部正确） ----------
        var newAsset = (TMP_FontAsset)createMi.Invoke(null,
            new object[] { font, SamplingSize, Padding, sdfaa, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, false });
        if (newAsset == null)
        {
            EditorUtility.DisplayDialog("字体重建失败",
                "CreateFontAsset 返回 null。请选中 VonwaonBitmap.ttf，在 Inspector 勾选 Include Font Data 后 Apply。", "确定");
            return;
        }

        // ---------- 3. 烘焙清单全部字符 ----------
        bool ok = newAsset.TryAddCharacters(characters, out string missing);

        // ---------- 4. 材质换回桌面版 SDF 着色器（外观与之前一致） ----------
        Shader sdfShader = Shader.Find("TextMeshPro/Distance Field");
        Material mat = newAsset.material;
        if (sdfShader != null && mat != null && mat.shader != sdfShader)
            mat.shader = sdfShader;
        if (mat != null && newAsset.atlasTextures[0] != null)
            mat.SetTexture(ShaderUtilities.ID_MainTex, newAsset.atlasTextures[0]);

        // 命名（Inspector 里整洁）
        newAsset.name = "VonwaonBitmap SDF";
        if (newAsset.atlasTextures[0] != null) newAsset.atlasTextures[0].name = "VonwaonBitmap Atlas";
        if (mat != null) mat.name = "VonwaonBitmap Atlas Material";

        // ---------- 5. 备份旧资产（仅内容备份到 Assets 外） ----------
        string absSdf = Path.Combine(Application.dataPath, SdfPath.Substring("Assets/".Length));
        string absBackup = Path.Combine(Application.dataPath, "..", BackupPath);
        try { File.Copy(absSdf, absBackup, true); }
        catch (Exception e) { Debug.LogWarning("[字体重建] 旧版备份失败（不影响重建）：" + e.Message); }

        // ---------- 6. 原位覆盖：只删 .asset，保留 .meta(GUID)，再同路径创建 ----------
        File.Delete(absSdf);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        AssetDatabase.CreateAsset(newAsset, SdfPath);
        AssetDatabase.AddObjectToAsset(newAsset.atlasTextures[0], newAsset);
        AssetDatabase.AddObjectToAsset(mat, newAsset);
        EditorUtility.SetDirty(newAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ---------- 7. 重新载入并构建运行时查找表 ----------
        var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SdfPath);
        saved?.ReadFontAssetDefinition();

        int missingCount = string.IsNullOrEmpty(missing) ? 0 : missing.Length;
        Debug.Log($"[字体重建] 完成 SDF高清版。成功={ok}，清单字符={characters.Length}，缺失跳过={missingCount}" +
                  (missingCount > 0 ? $"\n缺失字符：{missing}" : "") +
                  $"\n旧版备份：{absBackup}");

        EditorUtility.DisplayDialog("字体重建完成",
            $"SDF 高清烘焙结束。\n采样：{SamplingSize}px  图集：{AtlasSize}×{AtlasSize}\n" +
            $"清单字符：{characters.Length}\n字体缺失被跳过：{missingCount}\n\n" +
            "请回场景查看文字效果（应恢复正常，且任意字号都平滑）。", "确定");
    }
}
#endif
