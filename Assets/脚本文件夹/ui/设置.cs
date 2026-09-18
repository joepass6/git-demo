using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class 设置 : MonoBehaviour
{
public GameObject panel; // 拖入要关闭的面板
 public GameObject 分辨率; // 拖入“设置”物体
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

