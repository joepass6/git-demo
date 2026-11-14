# UI Effect Plugin

一个功能强大的Unity UI特效插件，提供多种常用的UI动画效果。

## 特性

### 基础特效
- **淡入淡出效果** (UIFadeEffect) - 控制UI元素的透明度变化
- **缩放效果** (UIScaleEffect) - 控制UI元素的缩放动画
- **位移效果** (UIMoveEffect) - 控制UI元素的位置移动
- **旋转效果** (UIRotateEffect) - 控制UI元素的旋转动画

### 高级特效
- **抖动效果** (UIShakeEffect) - 基于Perlin噪声的抖动动画
- **发光效果** (UIGlowEffect) - UI元素发光效果
- **打字机效果** (UITypewriterEffect) - 文字逐字显示效果
- **颜色渐变效果** (UIColorGradientEffect) - UI元素颜色过渡

### 粒子特效
- **UI粒子系统** (UIParticleEffect) - 自定义UI粒子发射器
- **粒子连线效果** (UIParticleLineEffect) - 粒子组成的连线效果

### 缓动函数
支持15种常用缓动函数：
- Linear
- EaseIn/Out/InOut Quad
- EaseIn/Out/InOut Cubic
- EaseIn/Out/InOut Back
- EaseIn/Out/InOut Bounce
- EaseIn/Out/InOut Elastic

## 快速开始

### 方式一：直接添加组件

1. 在Unity Inspector中选择UI对象
2. 添加所需的特效组件（如 UIFadeEffect）
3. 在Inspector中配置参数
4. 通过代码调用Play()方法启动特效

```csharp
using UIEffectPlugin;

public class MyScript : MonoBehaviour
{
    public UIFadeEffect fadeEffect;
    
    void Start()
    {
        fadeEffect.Play();
    }
}
```

### 方式二：使用扩展方法

```csharp
using UIEffectPlugin;
using UnityEngine.UI;

public class MyScript : MonoBehaviour
{
    public Image myImage;
    
    void Start()
    {
        // 添加并播放淡入效果
        myImage.gameObject.AddFadeEffect(0f, 1f, 0.5f).Play();
        
        // 使用便捷方法
        myImage.FadeIn();
        myImage.FadeOut();
        
        // 添加缩放效果
        myImage.gameObject.AddScaleEffect(Vector3.zero, Vector3.one, 0.5f).Play();
    }
}
```

### 方式三：使用特效管理器

```csharp
using UIEffectPlugin;

public class MyScript : MonoBehaviour
{
    public GameObject uiObject;
    
    void Start()
    {
        // 通过管理器播放特效
        UIEffectManager.Instance.PlayEffect(uiObject, TweenType.Fade);
        
        // 停止所有特效
        UIEffectManager.Instance.StopAllEffects();
    }
}
```

## 组件参数说明

### 通用参数
- **Duration**: 动画持续时间（秒）
- **Ease Type**: 缓动函数类型
- **Play On Awake**: 是否在启动时自动播放
- **Loop**: 是否循环播放
- **Ping Pong**: 是否往返播放（配合Loop使用）
- **Start Delay**: 延迟播放时间（秒）

### UIFadeEffect
- **Start Alpha**: 起始透明度
- **End Alpha**: 结束透明度

### UIScaleEffect
- **Start Scale**: 起始缩放
- **End Scale**: 结束缩放

### UIMoveEffect
- **Start Position**: 起始位置
- **End Position**: 结束位置
- **Use Local Position**: 是否使用本地坐标

### UIShakeEffect
- **Shake Amount**: 抖动幅度
- **Shake Frequency**: 抖动频率

### UITypewriterEffect
- **Full Text**: 完整文本内容
- **Chars Per Second**: 每秒显示字符数

### UIParticleEffect
- **Particle Sprite**: 粒子贴图
- **Particle Count**: 粒子数量
- **Particle Life Time**: 粒子生命周期
- **Start/End Color**: 粒子起始/结束颜色

## 事件回调

```csharp
UIFadeEffect effect = myImage.gameObject.AddFadeEffect(0f, 1f, 0.5f);
effect.OnStart += () => Debug.Log("特效开始");
effect.OnComplete += () => Debug.Log("特效完成");
effect.Play();
```

## 注意事项

1. UIFadeEffect、UIGlowEffect、UIColorGradientEffect 需要组件上有 Graphic 组件（如 Image、Text）
2. UITypewriterEffect 需要组件上有 Text 组件
3. UIParticleEffect 需要设置 Particle Sprite 才能正常显示
4. 所有特效组件都继承自 UITweenBase，具有统一的控制接口

## 许可证

MIT License
