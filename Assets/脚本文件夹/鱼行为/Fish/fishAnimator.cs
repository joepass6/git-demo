using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class fishAnimator : MonoBehaviour
{
    [Header("动画设置")]
    public float timer;//计时器，用来统计动物的闲置时间（当计时器满后，我们就会让动物进入移动状态）
    public bool 移动中 = true;//是闲置吗？如果这值是true那么它就是闲置状态，如果它是false时，那它就是移动状态，它的默认值就是true
    public float timekey = 0;//命名一个变量，需要一个值来配合计时器的工作，将它命名为“时间键”用来代表当前的时间进度，默认值设置为0
    private Animator ani;//private变量是私有变量，其他脚本无法看到，类型是Animator(动画控制器)
    private int lastMoveState = -1;//上次写入 Animator 的状态（-1 未初始化，0=闲置，1=移动），状态没变就不重复写
    // Start is called before the first frame update
    void Start()
    {
        //首先需要定义这个timer的值
        timer = 3;//初始的话就是3秒钟改变一次行为
                  //在start中获取ani的值
        ani = GetComponent<Animator>();
        //GetComponent是获取组件的意思，GetComponent<Animator>();是获取<Animator>(动画控制器)这个组件的意思  
    }

    // Update is called once per frame
    void Update()
    {
        timekey += Time.deltaTime;//我们需要让timekey增加每帧过去的时间点,这个值默认等于0.02，差不多就是我们现实当中的每一帧过去的时间
        if (timekey >= timer)//如果这个值大于或者等于timer的时候
        {
            timekey = 0;
            timer = Random.Range(2, 5);
            移动中 = !移动中;//切换闲置/移动状态
        }
        //只在状态真正变化时才写 Animator，避免每条鱼每帧一次原生调用
        int state = 移动中 ? 0 : 1;
        if (state != lastMoveState)
        {
            lastMoveState = state;
            if (ani != null) ani.SetBool("移动中", state == 1);//移动中=true 时 SetBool(false)，反之亦然
        }
    }
}
