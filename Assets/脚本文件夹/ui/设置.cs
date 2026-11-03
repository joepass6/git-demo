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
 }

 void OnTopMostChanged(bool isOn)
 {
     if (windowController != null)
         windowController.SetTopMost(isOn);
 }

 void OnAutoStartChanged(bool isOn)
 {
     RegistryHelper.SetAutoStart(isOn);
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

