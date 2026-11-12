 using System.Collections;
 using System.Collections.Generic;
 using UnityEngine;
 using UnityEngine.UI;

 public class 设置 : MonoBehaviour
 {
 public GameObject panel; // 拖入要关闭的面板
 public GameObject 分辨率; // 拖入"设置"物体

 [Header("音量 - 可选：在设置面板里放 Slider 并拖进来")]
 public Slider BGM音量滑条;
 public Slider SE音量滑条;

 [Header("窗口行为 - 可选：设置面板里放 Toggle 并拖进来")]
 public Toggle 置顶Toggle;
 public Toggle 开机自启动Toggle;
 [Tooltip("控制神明显隐的 Toggle；留空则按名字（含\"神明\"）自动查找")]
 public Toggle 神明Toggle;

 [Header("窗口控制器")]
 public DesktopWindowController windowController;

 IEnumerator Start()
 {
     // 等 AudioManager 初始化好（它挂在另一物体上，执行顺序不确定）
     while (AudioManager.Instance == null) yield return null;

     // BGM 滑条绑定
     if (BGM音量滑条 != null)
     {
         BGM音量滑条.value = AudioManager.Instance.获取BGM音量();
         BGM音量滑条.onValueChanged.AddListener(AudioManager.Instance.设置BGM音量);
     }
     // SE 滑条绑定
     if (SE音量滑条 != null)
     {
         SE音量滑条.value = AudioManager.Instance.获取SE音量();
         SE音量滑条.onValueChanged.AddListener(AudioManager.Instance.设置SE音量);
     }

     // 找窗口控制器（如果没手动拖）
     if (windowController == null)
         windowController = FindObjectOfType<DesktopWindowController>();

     // 置顶 Toggle 初始化 + 绑定
     if (置顶Toggle != null)
     {
         // 先关掉 listener 防止初始化时触发回调
         置顶Toggle.onValueChanged.RemoveAllListeners();
         置顶Toggle.isOn = windowController != null && windowController.IsTopMost();
         置顶Toggle.onValueChanged.AddListener(OnTopMostChanged);
     }

     // 开机自启动 Toggle 初始化 + 绑定
     if (开机自启动Toggle != null)
     {
         开机自启动Toggle.onValueChanged.RemoveAllListeners();
         开机自启动Toggle.isOn = RegistryHelper.IsAutoStartEnabled();
         开机自启动Toggle.onValueChanged.AddListener(OnAutoStartChanged);
     }

     // 神明开关：场景里新加的 Toggle 没拖也能用，按名字自动查找
     if (神明Toggle == null)
     {
         foreach (Toggle t in FindObjectsOfType<Toggle>(true))
         {
             if (t != null && t.name.Contains("神明")) { 神明Toggle = t; break; }
         }
     }
     if (神明Toggle != null)
     {
         神明Toggle.onValueChanged.RemoveAllListeners();
         神明Toggle.isOn = windowController != null && windowController.IsGodVisible();
         神明Toggle.onValueChanged.AddListener(OnGodVisibleChanged);
         Debug.Log($"[设置] 神明开关已绑定: {神明Toggle.name}, controller={(windowController != null ? "有" : "空")}, 初始 isOn={神明Toggle.isOn}");
     }
     else
     {
         Debug.LogWarning("[设置] 没找到名字含“神明”的 Toggle，神明开关不会生效");
     }

     // 兜底本地化：设置面板里没挂 LocalizedText 的文本（含 Legacy Text）按当前文字自动认领 key
     if (LocalizationManager.Instance != null)
     {
         if (分辨率 != null) LocalizationManager.Instance.LocalizeTree(分辨率.transform);
         if (panel != null) LocalizationManager.Instance.LocalizeTree(panel.transform);
     }
 }

 void OnTopMostChanged(bool isOn)
 {
     // 兜底：Start 里没找到控制器（执行顺序/物体未激活）时，点开关再找一次，
     // 否则点了只会翻转按钮、SetTopMost 根本没被调用，看起来就是“没反应”。
     if (windowController == null)
     {
         windowController = FindObjectOfType<DesktopWindowController>();
         if (windowController == null)
         {
             Debug.LogError("[设置] 没找到 DesktopWindowController，置顶/神明开关无法生效");
             return;
         }
     }
     Debug.Log($"[设置] 置顶开关 -> {isOn}（编辑器内 ApplyTopMost 是空实现，只能用打包版验证）");
     windowController.SetTopMost(isOn);
 }

 void OnAutoStartChanged(bool isOn)
 {
     RegistryHelper.SetAutoStart(isOn);
 }

 void OnGodVisibleChanged(bool isOn)
 {
     if (windowController != null)
         windowController.SetGodVisible(isOn);
 }

 public void Click_加qq玩家群()
 {
    Application.OpenURL("https://qm.qq.com/q/9aFiHqswn0");
 }
public void Click_加x()
 {
    Application.OpenURL("点击链接加入群聊【人生要有planb】：https://qm.qq.com/q/9aFiHqswn0");
 }
 public void Click_加小红书()
 {
    Application.OpenURL("点击链接加入群聊【人生要有planb】：https://qm.qq.com/q/9aFiHqswn0");
 }
 public void Click_加()
 {
    Application.OpenURL("点击链接加入群聊【人生要有planb】：https://qm.qq.com/q/9aFiHqswn0");
 }

public void Click_关闭游戏()
 {
    Application.Quit();
    
 }

public void ClosePanel()
{
  panel.SetActive(false);
}

public void Click_分辨率()
{
  分辨率.SetActive(true);
}
}

