using UnityEngine;
using UnityEngine.UI;

namespace UIEffectPlugin
{
    public static class EffectExtensions
    {
        public static UIFadeEffect AddFadeEffect(this GameObject obj, float startAlpha = 0f, float endAlpha = 1f, float duration = 0.5f)
        {
            if (obj == null) return null;
            
            UIFadeEffect effect = obj.GetComponent<UIFadeEffect>();
            if (effect == null)
            {
                effect = obj.AddComponent<UIFadeEffect>();
            }
            effect.SetStartAlpha(startAlpha);
            effect.SetEndAlpha(endAlpha);
            effect.DurationValue = duration;
            return effect;
        }

        public static UIScaleEffect AddScaleEffect(this GameObject obj, Vector3 startScale, Vector3 endScale, float duration = 0.5f)
        {
            if (obj == null) return null;
            
            UIScaleEffect effect = obj.GetComponent<UIScaleEffect>();
            if (effect == null)
            {
                effect = obj.AddComponent<UIScaleEffect>();
            }
            effect.SetStartScale(startScale);
            effect.SetEndScale(endScale);
            effect.DurationValue = duration;
            return effect;
        }

        public static UIMoveEffect AddMoveEffect(this GameObject obj, Vector2 startPos, Vector2 endPos, float duration = 0.5f, bool useLocalPosition = true)
        {
            if (obj == null) return null;
            
            UIMoveEffect effect = obj.GetComponent<UIMoveEffect>();
            if (effect == null)
            {
                effect = obj.AddComponent<UIMoveEffect>();
            }
            effect.SetStartPosition(startPos);
            effect.SetEndPosition(endPos);
            effect.DurationValue = duration;
            effect.UseLocalPosition = useLocalPosition;
            return effect;
        }

        public static UIRotateEffect AddRotateEffect(this GameObject obj, Vector3 startRotation, Vector3 endRotation, float duration = 1f)
        {
            if (obj == null) return null;
            
            UIRotateEffect effect = obj.GetComponent<UIRotateEffect>();
            if (effect == null)
            {
                effect = obj.AddComponent<UIRotateEffect>();
            }
            effect.SetStartRotation(startRotation);
            effect.SetEndRotation(endRotation);
            effect.DurationValue = duration;
            return effect;
        }

        public static UIShakeEffect AddShakeEffect(this GameObject obj, Vector2 shakeAmount, int frequency = 10, float duration = 0.5f)
        {
            if (obj == null) return null;
            
            UIShakeEffect effect = obj.GetComponent<UIShakeEffect>();
            if (effect == null)
            {
                effect = obj.AddComponent<UIShakeEffect>();
            }
            effect.SetShakeAmount(shakeAmount);
            effect.SetShakeFrequency(frequency);
            effect.DurationValue = duration;
            return effect;
        }

        public static UIGlowEffect AddGlowEffect(this GameObject obj, Color glowColor, float minIntensity = 0f, float maxIntensity = 1f, float duration = 0.5f)
        {
            if (obj == null) return null;
            
            UIGlowEffect effect = obj.GetComponent<UIGlowEffect>();
            if (effect == null)
            {
                effect = obj.AddComponent<UIGlowEffect>();
            }
            effect.SetGlowColor(glowColor);
            effect.SetGlowIntensity(minIntensity, maxIntensity);
            effect.DurationValue = duration;
            return effect;
        }

        public static UIColorGradientEffect AddColorGradientEffect(this GameObject obj, Color startColor, Color endColor, float duration = 1f)
        {
            if (obj == null) return null;
            
            UIColorGradientEffect effect = obj.GetComponent<UIColorGradientEffect>();
            if (effect == null)
            {
                effect = obj.AddComponent<UIColorGradientEffect>();
            }
            effect.SetStartColor(startColor);
            effect.SetEndColor(endColor);
            effect.DurationValue = duration;
            return effect;
        }

        public static UITypewriterEffect AddTypewriterEffect(this GameObject obj, string text, float charsPerSecond = 10f)
        {
            if (obj == null) return null;
            
            UITypewriterEffect effect = obj.GetComponent<UITypewriterEffect>();
            if (effect == null)
            {
                effect = obj.AddComponent<UITypewriterEffect>();
            }
            effect.SetFullText(text);
            effect.SetCharsPerSecond(charsPerSecond);
            return effect;
        }

        public static void FadeIn(this Graphic graphic, float duration = 0.5f)
        {
            if (graphic == null) return;
            UIFadeEffect effect = graphic.gameObject.AddFadeEffect(0f, 1f, duration);
            effect?.Play();
        }

        public static void FadeOut(this Graphic graphic, float duration = 0.5f)
        {
            if (graphic == null) return;
            UIFadeEffect effect = graphic.gameObject.AddFadeEffect(1f, 0f, duration);
            effect?.Play();
        }

        public static void ScaleIn(this RectTransform rectTransform, float duration = 0.5f)
        {
            if (rectTransform == null) return;
            UIScaleEffect effect = rectTransform.gameObject.AddScaleEffect(Vector3.zero, Vector3.one, duration);
            effect?.Play();
        }

        public static void ScaleOut(this RectTransform rectTransform, float duration = 0.5f)
        {
            if (rectTransform == null) return;
            UIScaleEffect effect = rectTransform.gameObject.AddScaleEffect(Vector3.one, Vector3.zero, duration);
            effect?.Play();
        }
    }
}