using UnityEditor;
using UnityEngine;

public static class MenuTest
{
    [MenuItem("Tools/测试菜单 (请点我)")]
    public static void Test()
    {
        EditorUtility.DisplayDialog("成功", "菜单能看到说明编辑器脚本编译通过了！", "OK");
    }
}
