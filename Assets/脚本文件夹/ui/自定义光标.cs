using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 运行时自定义光标：从 StreamingAssets/光标/cursor.png 读图，按 光标配置.txt 缩放并用 Cursor.SetCursor 设置。
/// 好处：
///  - 绕开纹理导入类型/Read-Write 开关，构建版一定能拿到可用纹理
///  - 热点按“原图像素 → 输出尺寸”等比换算，图片被缩放时热点同步缩放
///  - 配置改完保存即可生效（每秒检查一次文件修改时间），便于微调
/// 配置缺失时什么都不做，退回 Player Settings 的默认光标。
/// </summary>
public static class 自定义光标
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void 启动()
    {
        GameObject go = new GameObject("[自定义光标]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<自定义光标运行>();
    }
}

public class 自定义光标运行 : MonoBehaviour
{
    Texture2D 原图;
    Texture2D 输出图;
    Vector2 热点原图 = new Vector2(287f, 288f); // 640x640 原图左上角像素坐标
    int 输出尺寸 = 64;
    bool 软件光标 = true;

    string 图片路径;
    string 配置路径;
    DateTime 配置时间;
    float 下次检查;

    void Start()
    {
        图片路径 = Path.Combine(Application.streamingAssetsPath, "光标", "cursor.png");
        配置路径 = Path.Combine(Application.streamingAssetsPath, "光标", "光标配置.txt");

        if (!File.Exists(图片路径))
        {
            Debug.LogWarning($"[光标] 未找到 {图片路径}，保持 Player Settings 默认光标");
            return;
        }

        读取配置();
        载入();
        Apply();
    }

    void Update()
    {
        if (原图 == null) return;
        if (Time.unscaledTime < 下次检查) return;
        下次检查 = Time.unscaledTime + 1f;

        try
        {
            if (File.Exists(配置路径))
            {
                DateTime t = File.GetLastWriteTimeUtc(配置路径);
                if (t != 配置时间)
                {
                    配置时间 = t;
                    读取配置();
                    载入();
                    Debug.Log("[光标] 配置已更新，重新应用");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[光标] 读取配置失败：{e.Message}");
        }

        Apply();
    }

    void 读取配置()
    {
        if (!File.Exists(配置路径)) return;
        try
        {
            string[] lines = File.ReadAllLines(配置路径);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (!float.TryParse(val, out float f)) continue;

                if (key == "x") 热点原图.x = f;
                else if (key == "y") 热点原图.y = f;
                else if (key == "size" || key == "尺寸") 输出尺寸 = Mathf.Clamp((int)f, 16, 256);
                else if (key == "software" || key == "软件光标") 软件光标 = f > 0.5f;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[光标] 解析配置失败：{e.Message}");
        }
    }

    void 载入()
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(图片路径);
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(bytes))
            {
                Debug.LogWarning("[光标] cursor.png 解码失败，保持默认光标");
                return;
            }
            src.filterMode = FilterMode.Point;

            if (原图 != null) Destroy(原图);
            if (输出图 != null) Destroy(输出图);
            原图 = src;
            输出图 = 缩放(原图, 输出尺寸);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[光标] 载入图片失败：{e.Message}");
        }
    }

    /// <summary>最近邻缩放（像素画保持硬边），输出贴图可直接交给 Cursor.SetCursor。</summary>
    Texture2D 缩放(Texture2D src, int size)
    {
        int sw = src.width, sh = src.height;
        Color32[] s = src.GetPixels32();
        Color32[] d = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            int sy = Mathf.Clamp(y * sh / size, 0, sh - 1);
            for (int x = 0; x < size; x++)
            {
                int sx = Mathf.Clamp(x * sw / size, 0, sw - 1);
                d[y * size + x] = s[sy * sw + sx];
            }
        }
        var dst = new Texture2D(size, size, TextureFormat.RGBA32, false);
        dst.filterMode = FilterMode.Point;
        dst.wrapMode = TextureWrapMode.Clamp;
        dst.SetPixels32(d);
        dst.Apply(false, false);
        return dst;
    }

    void Apply()
    {
        if (输出图 == null || 原图 == null) return;

        // 热点：原图左上角像素 → 输出像素（等比缩放）
        float k = (float)输出尺寸 / Mathf.Max(1, 原图.width);
        Vector2 热点 = new Vector2(
            Mathf.Clamp(热点原图.x * k, 0f, 输出尺寸 - 1),
            Mathf.Clamp(热点原图.y * k, 0f, 输出尺寸 - 1));

        Cursor.visible = true;
        Cursor.SetCursor(输出图, 热点, 软件光标 ? CursorMode.ForceSoftware : CursorMode.Auto);

        string 摘要 = $"{原图.width}x{原图.height}→{输出尺寸} 热点源=({热点原图.x:F0},{热点原图.y:F0}) 热点输出=({热点.x:F1},{热点.y:F1}) 模式={(软件光标 ? "ForceSoftware" : "Auto")}";
        if (摘要 != 上次打印)
        {
            上次打印 = 摘要;
            Debug.Log($"[光标] 已应用：{摘要}");
        }
    }

    string 上次打印;
}
