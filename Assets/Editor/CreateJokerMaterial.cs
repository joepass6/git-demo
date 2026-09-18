using UnityEngine;
using UnityEditor;
using System.IO;

public class CreateJokerMaterial : EditorWindow
{
    [MenuItem("Tools/Create Neon Psychedelic 2D Material")]
    public static void ShowWindow()
    {
        GetWindow<CreateJokerMaterial>("Neon 2D Material");
    }

    private void OnGUI()
    {
        GUILayout.Label("Neon Psychedelic 2D Material Creator", EditorStyles.boldLabel);
        GUILayout.Space(10);

        if (GUILayout.Button("Create Material"))
        {
            CreateMaterial();
        }
    }

    private void CreateMaterial()
    {
        Shader shader = Shader.Find("Custom/NeonPsychedelic2D");
        
        if (shader == null)
        {
            EditorUtility.DisplayDialog("Error", "Shader not found! Make sure JokerCardShader.shader is in your project.", "OK");
            return;
        }

        Material material = new Material(shader);
        material.name = "NeonPsychedelic2DMaterial";

        string assetPath = "Assets/NeonPsychedelic2DMaterial.mat";
        
        if (File.Exists(assetPath))
        {
            bool overwrite = EditorUtility.DisplayDialog("Overwrite?", "Material already exists. Overwrite?", "Yes", "No");
            if (!overwrite) return;
        }

        AssetDatabase.CreateAsset(material, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", "Material created at: " + assetPath, "OK");
    }
}
