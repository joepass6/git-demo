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

        // 1b. 同时删除 Steam 云端存档（否则下次启动云端旧档会被重新下载回来）
        SteamCloudStorage.DeleteCloudSave();

        // 2. 清除 PlayerPrefs
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[清除存档] 已清除 PlayerPrefs");

        // 3. 清除 DropItem 物品数量
        DropItem.ClearAllCounts();
        Debug.Log("[清除存档] 已清除物品数量");

        // 4. 重置 GameStats 进度（已发现鱼、图鉴、放置/卖出计数）
        GameStats.Reset();
        Debug.Log("[清除存档] 已重置游戏统计进度");

        Debug.Log("[清除存档] 全部清除完成");
    }
}
