// Steam 云存档封装（ISteamRemoteStorage）。
// 云文件名与本地一致：save.json。Steam 客户端负责在多台电脑之间自动同步该文件。
// 策略（与 Steam AutoCloud 相同的「最后写入者胜」）：
//   启动时：云端文件比本地新（5 秒以上容差，规避本机与 Steam 服务器时钟差）才下载覆盖；
//   保存时：本地写盘成功后把同一份内容写到云端。
// Steam 不可用 / 玩家为该游戏关闭了云同步时，所有方法安全 no-op，游戏照常纯本地运行。
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using System;
using System.IO;
using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

public static class SteamCloudStorage
{
    const string CloudFileName = "save.json";

    // 本机时钟与 Steam 服务器时钟的最大假定偏差：小于这个差值不认为云端更新，
    // 避免同一台机器每次启动都用相同内容覆盖本地档
    const double NewerToleranceSeconds = 5d;

    static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static bool IsAvailable
    {
        get
        {
#if !DISABLESTEAMWORKS
            return SteamManager.Initialized
                && SteamRemoteStorage.IsCloudEnabledForAccount()
                && SteamRemoteStorage.IsCloudEnabledForApp();
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// 启动时调用：若云端存档比本地存档新，下载并覆盖本地路径。
    /// 返回 true 表示本地文件已被云端内容替换（调用方随后正常读本地档即可）。
    /// </summary>
    public static bool SyncToLocalIfNewer(string localPath)
    {
#if !DISABLESTEAMWORKS
        try
        {
            if (!IsAvailable || !SteamRemoteStorage.FileExists(CloudFileName)) return false;

            DateTime? cloudTime = GetCloudTimestampUtc();
            DateTime localTime = File.Exists(localPath)
                ? File.GetLastWriteTimeUtc(localPath)
                : DateTime.MinValue;

            if (cloudTime.HasValue && cloudTime.Value > localTime.AddSeconds(NewerToleranceSeconds))
            {
                if (DownloadToLocal(localPath))
                {
                    Debug.Log($"[Steam云存档] 云端存档更新（云:{cloudTime.Value:u} 本地:{localTime:u}），已下载覆盖本地");
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Steam云存档] 启动同步异常，保留本地档：{e.Message}");
        }
#endif
        return false;
    }

    /// <summary>清除存档时调用：同时删除云端文件，否则下次启动云端旧档会被重新下载回来。</summary>
    public static bool DeleteCloudSave()
    {
#if !DISABLESTEAMWORKS
        try
        {
            if (!IsAvailable || !SteamRemoteStorage.FileExists(CloudFileName)) return false;
            bool ok = SteamRemoteStorage.FileDelete(CloudFileName);
            Debug.Log(ok ? "[Steam云存档] 已删除云端存档" : "[Steam云存档] 删除云端存档失败");
            return ok;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Steam云存档] 删除异常：{e.Message}");
        }
#endif
        return false;
    }

    /// <summary>保存后调用：把本地存档字节写入 Steam 云（Steam 客户端随后自动上传）。</summary>
    public static bool UploadFromLocal(string localPath)
    {
#if !DISABLESTEAMWORKS
        try
        {
            if (!IsAvailable || !File.Exists(localPath)) return false;

            byte[] bytes = File.ReadAllBytes(localPath);
            if (!SteamRemoteStorage.FileWrite(CloudFileName, bytes, bytes.Length))
            {
                Debug.LogWarning("[Steam云存档] FileWrite 失败（超出云容量配额？）");
                return false;
            }
            Debug.Log($"[Steam云存档] 已写入云端，{bytes.Length} 字节");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Steam云存档] 上传异常：{e.Message}");
        }
#endif
        return false;
    }

#if !DISABLESTEAMWORKS
    static DateTime? GetCloudTimestampUtc()
    {
        // 返回 Unix UTC 秒数；文件不存在时返回 0
        long seconds = SteamRemoteStorage.GetFileTimestamp(CloudFileName);
        return seconds <= 0 ? (DateTime?)null : Epoch.AddSeconds(seconds);
    }

    static bool DownloadToLocal(string localPath)
    {
        int size = SteamRemoteStorage.GetFileSize(CloudFileName);
        if (size <= 0) return false;

        byte[] buffer = new byte[size];
        int read = SteamRemoteStorage.FileRead(CloudFileName, buffer, size);
        if (read != size)
        {
            Debug.LogWarning($"[Steam云存档] FileRead 不完整：{read}/{size}");
            return false;
        }

        string dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(localPath, buffer);
        return true;
    }
#endif
}
