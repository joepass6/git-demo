using UnityEngine;
using System.IO;

/// <summary>
/// 清除所有存档：SaveSystem 存档 + PlayerPrefs + DropItem 物品数量
/// 快捷键：Ctrl+Delete 清除
/// </summary>
public class ClearSaveSystem : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Delete))
        {
            ClearAll();
        }
    }

    public void ClearAll()
    {
        // 1. 删除 SaveSystem 存档文件
        string savePath = Path.Combine(Application.persistentDataPath, "save.json");
        if (File.Exists(savePath))
        {
            File.Delete(savePath);
            Debug.Log("[清除存档] 已删除 save.json");
        }

        // 2. 清除 PlayerPrefs
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[清除存档] 已清除 PlayerPrefs");

        // 3. 清除 DropItem 物品数量
        var countsField = typeof(DropItem).GetField("_counts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (countsField != null)
        {
            var dict = countsField.GetValue(null) as System.Collections.Generic.Dictionary<string, int>;
            if (dict != null)
            {
                dict.Clear();
                Debug.Log("[清除存档] 已清除物品数量");
            }
        }

        Debug.Log("[清除存档] 全部清除完成，请重启游戏生效");
    }
}
