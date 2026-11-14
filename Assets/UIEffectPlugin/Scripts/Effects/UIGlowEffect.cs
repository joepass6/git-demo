using UnityEngine;
using UnityEngine.UI;

namespace UIEffectPlugin
{
    [RequireComponent(typeof(Graphic))]
    public class UIGlowEffect : UITweenBase
    {
        [Header("Glow Settings")]
        [SerializeField] private Color glowColor = Color.yellow;
        [SerializeField] private float minGlowIntensity = 0f;
        [SerializeField] private float maxGlowIntensity = 1f;

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
            tempColor.a = originalColor.a;
        }

        protected override void UpdateTween(float t)
        {
            float intensity = Mathf.Lerp(minGlowIntensity, maxGlowIntensity, t);
            tempColor.r = Mathf.Lerp(originalColor.r, glowColor.r, intensity);
            tempColor.g = Mathf.Lerp(originalColor.g, glowColor.g, intensity);
            tempColor.b = Mathf.Lerp(originalColor.b, glowColor.b, intensity);
            graphic.color = tempColor;
        }

        public void SetGlowColor(Color color)
        {
            glowColor = color;
        }

        public void SetGlowIntensity(float min, float max)
        {
            minGlowIntensity = Mathf.Clamp01(min);
            maxGlowIntensity = Mathf.Clamp01(max);
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