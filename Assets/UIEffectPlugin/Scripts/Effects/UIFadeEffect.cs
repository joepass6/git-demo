using UnityEngine;
using UnityEngine.UI;

namespace UIEffectPlugin
{
    [RequireComponent(typeof(Graphic))]
    public class UIFadeEffect : UITweenBase
    {
        [Header("Fade Settings")]
        [SerializeField] private float startAlpha = 0f;
        [SerializeField] private float endAlpha = 1f;

        private Graphic graphic;
        private Color originalColor;
        private Color tempColor = new Color();

        protected override void Init()
        {
            graphic = GetComponent<Graphic>();
            if (graphic == null)
            {
                enabled = false;
                return;
            }
            originalColor = graphic.color;
            tempColor.r = originalColor.r;
            tempColor.g = originalColor.g;
            tempColor.b = originalColor.b;
            tempColor.a = startAlpha;
            graphic.color = tempColor;
        }

        protected override void UpdateTween(float t)
        {
            tempColor.a = Mathf.Lerp(startAlpha, endAlpha, t);
            graphic.color = tempColor;
        }

        public void SetStartAlpha(float alpha)
        {
            startAlpha = Mathf.Clamp01(alpha);
        }

        public void SetEndAlpha(float alpha)
        {
            endAlpha = Mathf.Clamp01(alpha);
        }

        public void ResetToOriginal()
        {
            if (graphic != null)
            {
                graphic.color = originalColor;
            }
        }
    }
}