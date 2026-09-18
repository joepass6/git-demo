using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

/// <summary>
/// ObjectSpawner 的自定义 Inspector：spawnEntries 列表每行可直接编辑 kindId / prefab / weight
/// </summary>
[CustomEditor(typeof(ObjectSpawner))]
public class ObjectSpawnerEditor : Editor
{
    private SerializedProperty spawnEntriesProp;
    private ReorderableList reorderableList;

    private void OnEnable()
    {
        spawnEntriesProp = serializedObject.FindProperty("spawnEntries");

        reorderableList = new ReorderableList(serializedObject, spawnEntriesProp, true, true, true, true);

        reorderableList.drawHeaderCallback = (Rect rect) =>
        {
            EditorGUI.LabelField(rect, "spawnEntries");
        };

        // 每个元素：3 行（编号 / 预制件 / 权重），全部展开直接编辑
        reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            SerializedProperty element = spawnEntriesProp.GetArrayElementAtIndex(index);
            SerializedProperty idProp = element.FindPropertyRelative("kindId");
            SerializedProperty prefabProp = element.FindPropertyRelative("prefab");
            SerializedProperty weightProp = element.FindPropertyRelative("weight");

            float lineH = EditorGUIUtility.singleLineHeight;
            float gap = 2;
            float labelW = 70;

            rect.y += gap;

            // 行 1：kindId
            Rect r1 = new Rect(rect.x, rect.y, rect.width, lineH);
            EditorGUI.LabelField(new Rect(r1.x, r1.y, labelW, r1.height), "编号");
            EditorGUI.PropertyField(new Rect(r1.x + labelW, r1.y, r1.width - labelW, r1.height), idProp, GUIContent.none);
            rect.y += lineH + gap;

            // 行 2：prefab
            Rect r2 = new Rect(rect.x, rect.y, rect.width, lineH);
            EditorGUI.LabelField(new Rect(r2.x, r2.y, labelW, r2.height), "预制件");
            EditorGUI.PropertyField(new Rect(r2.x + labelW, r2.y, r2.width - labelW, r2.height), prefabProp, GUIContent.none);
            rect.y += lineH + gap;

            // 行 3：weight
            Rect r3 = new Rect(rect.x, rect.y, rect.width, lineH);
            EditorGUI.LabelField(new Rect(r3.x, r3.y, labelW, r3.height), "权重");
            EditorGUI.PropertyField(new Rect(r3.x + labelW, r3.y, r3.width - labelW, r3.height), weightProp, GUIContent.none);
        };

        reorderableList.elementHeightCallback = (int index) =>
        {
            return EditorGUIUtility.singleLineHeight * 3 + 8;
        };

        // 新增：自动分配 kindId = 当前最大 + 1
        reorderableList.onAddCallback = (ReorderableList list) =>
        {
            list.serializedProperty.InsertArrayElementAtIndex(list.serializedProperty.arraySize);
            serializedObject.ApplyModifiedProperties();

            SerializedProperty newEl = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1);
            SerializedProperty newId = newEl.FindPropertyRelative("kindId");
            int maxId = 0;
            for (int i = 0; i < list.serializedProperty.arraySize - 1; i++)
            {
                SerializedProperty id = list.serializedProperty.GetArrayElementAtIndex(i).FindPropertyRelative("kindId");
                if (id.intValue >= maxId) maxId = id.intValue + 1;
            }
            newId.intValue = maxId;
            serializedObject.ApplyModifiedProperties();
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "spawnEntries");
        EditorGUILayout.Space(4);
        reorderableList.DoLayoutList();
        EditorGUILayout.Space(2);
        int count = spawnEntriesProp != null ? spawnEntriesProp.arraySize : 0;
        EditorGUILayout.HelpBox($"共 {count} 种", MessageType.Info);
        serializedObject.ApplyModifiedProperties();
    }
}
