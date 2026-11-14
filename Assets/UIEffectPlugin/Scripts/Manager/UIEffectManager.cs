using UnityEngine;
using System.Collections.Generic;

namespace UIEffectPlugin
{
    public class UIEffectManager : MonoBehaviour
    {
        private static UIEffectManager instance;
        private static readonly object lockObj = new object();

        public static UIEffectManager Instance
        {
            get
            {
                lock (lockObj)
                {
                    if (instance == null)
                    {
                        GameObject managerObj = new GameObject("UIEffectManager");
                        instance = managerObj.AddComponent<UIEffectManager>();
                        DontDestroyOnLoad(managerObj);
                    }
                    return instance;
                }
            }
        }

        private List<UITweenBase> activeEffects = new List<UITweenBase>();

        private void Awake()
        {
            lock (lockObj)
            {
                if (instance == null)
                {
                    instance = this;
                    DontDestroyOnLoad(gameObject);
                }
                else if (instance != this)
                {
                    Destroy(gameObject);
                }
            }
        }

        public void RegisterEffect(UITweenBase effect)
        {
            if (effect == null) return;
            if (!activeEffects.Contains(effect))
            {
                activeEffects.Add(effect);
            }
        }

        public void UnregisterEffect(UITweenBase effect)
        {
            if (effect == null) return;
            if (activeEffects.Contains(effect))
            {
                activeEffects.Remove(effect);
            }
        }

        public void PlayEffect(UITweenBase effect)
        {
            if (effect == null) return;
            RegisterEffect(effect);
            effect.Play(() => UnregisterEffect(effect));
        }

        public void PlayEffect(GameObject target, TweenType tweenType)
        {
            if (target == null) return;
            UITweenBase effect = GetEffect(target, tweenType);
            if (effect != null)
            {
                PlayEffect(effect);
            }
        }

        public UITweenBase GetEffect(GameObject target, TweenType tweenType)
        {
            if (target == null) return null;
            
            switch (tweenType)
            {
                case TweenType.Fade:
                    return target.GetComponent<UIFadeEffect>();
                case TweenType.Scale:
                    return target.GetComponent<UIScaleEffect>();
                case TweenType.Move:
                    return target.GetComponent<UIMoveEffect>();
                case TweenType.Rotate:
                    return target.GetComponent<UIRotateEffect>();
                case TweenType.Shake:
                    return target.GetComponent<UIShakeEffect>();
                case TweenType.Glow:
                    return target.GetComponent<UIGlowEffect>();
                case TweenType.ColorGradient:
                    return target.GetComponent<UIColorGradientEffect>();
                case TweenType.Typewriter:
                    return target.GetComponent<UITypewriterEffect>();
                default:
                    return null;
            }
        }

        public void StopAllEffects()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                activeEffects[i].Stop();
            }
            activeEffects.Clear();
        }

        public void StopEffect(GameObject target)
        {
            if (target == null) return;
            
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                if (activeEffects[i].gameObject == target)
                {
                    activeEffects[i].Stop();
                    activeEffects.RemoveAt(i);
                }
            }
        }

        public int ActiveEffectCount => activeEffects.Count;
    }
}