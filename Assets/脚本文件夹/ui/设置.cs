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

