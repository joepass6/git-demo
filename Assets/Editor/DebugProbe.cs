using UnityEditor;
using UnityEngine;

public static class DebugProbe
{
    [InitializeOnLoadMethod]
    static void Probe()
    {
        Debug.Log("========== DEBUG PROBE: Assembly-CSharp-Editor 编译成功 ==========");
    }

    [MenuItem("Tools/探针_点我测试")]
    public static void Menu()
    {
        EditorUtility.DisplayDialog("探针成功", "Assembly-CSharp-Editor 编译完全正常！", "OK");
    }
}
