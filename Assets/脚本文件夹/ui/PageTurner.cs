using UnityEngine;
using UnityEngine.UI;

public class PageTurner : MonoBehaviour
{
    [Header("旋转设置")]
    public float rotationAngle = 90f; // 每次旋转的角度

    [Header("页面设置")]
    // 这里需要手动把 PageContainer 拖进来，或者在代码里自动找
    public GameObject[] pages; 

    private int currentPageIndex = 0;// 当前显示的页面索引，默认是0，因为第一个页面是默认显示的，所以索引是0

    void Start()
    {
        // 1. 获取按钮组件
        Button button = GetComponent<Button>();// 获取按钮组件
        if (button != null)
        {
            button.onClick.AddListener(TurnPage);// 点击按钮时，调用 TurnPage 方法切换页面
        }

        // 2. 自动查找页面（如果 Inspector 里没赋值）
        // 逻辑：自动找同级的 PageContainer 下的所有子物体
        if (pages == null || pages.Length == 0)
        {
            // 尝试找名为 "PageContainer" 的兄弟节点
            Transform container = transform.parent?.Find("PageContainer");
            
            // 如果没找到或者没命名，就默认找父级下的第二个子物体（假设第一个是按钮自己）
            if (container == null && transform.parent != null)
            {
                 if(transform.parent.childCount > 1)
                 {
                     container = transform.parent.GetChild(1);
                 }
            }

            if (container != null)// 如果找到了 PageContainer 容器，就继续执行
            {
                pages = new GameObject[container.childCount];// 初始化 pages 数组，大小为 PageContainer 下的子物体数量
                for (int i = 0; i < container.childCount; i++)// 遍历 PageContainer 下的所有子物体
                {
                    pages[i] = container.GetChild(i).gameObject;
                }
            }
        }

        // 初始化显示
        ShowCurrentPage();
    }

    void TurnPage()// 点击按钮时，调用 TurnPage 方法切换页面
    {
        // ⭐ 核心逻辑 1：让自己（按钮）旋转
        transform.Rotate(0, 0, rotationAngle);

        // ⭐ 核心逻辑 2：切换页面索引
        currentPageIndex++;
        if (currentPageIndex >= pages.Length)
        {
            currentPageIndex = 0;
        }

        // 更新页面显示
        ShowCurrentPage();
    }

    void ShowCurrentPage()// 更新页面显示
    {
        // 隐藏所有页面
        if (pages != null)
        {
            foreach (GameObject page in pages)
            {
                if(page != null) page.SetActive(false);
            }

            // 显示当前页面
            if (currentPageIndex < pages.Length && pages[currentPageIndex] != null)
            {
                pages[currentPageIndex].SetActive(true);
            }
        }
    }
}