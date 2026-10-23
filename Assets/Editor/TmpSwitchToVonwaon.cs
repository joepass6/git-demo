#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TmpSwitchToVonwaon
{
    [MenuItem("工具/字体/把所有场景内 TMP 文本切到 VonwaonBitmap")]
    public static void DoSwitch()
    {
        string scenePath = EditorSceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(scenePath)) { Debug.LogError("先打开一个场景"); return; }

        var vonwaon = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/素材/UI/字体/VonwaonBitmap SDF.asset");
        if (vonwaon == null) { Debug.LogError("找不到 VonwaonBitmap SDF.asset"); return; }

        var all = UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshProUGUI>(includeInactive: true);
        int changed = 0, same = 0;
        foreach (var t in all)
        {
            if (t == null) continue;
            if (t.font == vonwaon) { same++; continue; }
            t.font = vonwaon;
            EditorUtility.SetDirty(t);
            changed++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log($"=== 切换完成：changed={changed} already_ok={same} ===");
        Debug.Log("场景已保存，请进 Play 测试汉字显示。");
    }
}
#endif
