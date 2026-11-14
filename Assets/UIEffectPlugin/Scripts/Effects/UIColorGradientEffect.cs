using UnityEngine;
using UnityEngine.UI;

namespace UIEffectPlugin
{
    [RequireComponent(typeof(Graphic))]
    public class UIColorGradientEffect : UITweenBase
    {
        [Header("Gradient Settings")]
        [SerializeField] private Color startColor = Color.white;
        [SerializeField] private Color endColor = Color.red;

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
            graphic.color = startColor;
        }

        protected override void UpdateTween(float t)
        {
            tempColor.r = Mathf.Lerp(startColor.r, endColor.r, t);
            tempColor.g = Mathf.Lerp(startColor.g, endColor.g, t);
            tempColor.b = Mathf.Lerp(startColor.b, endColor.b, t);
            tempColor.a = Mathf.Lerp(startColor.a, endColor.a, t);
            graphic.color = tempColor;
        }

        public void SetStartColor(Color color)
        {
            startColor = color;
        }

        public void SetEndColor(Color color)
        {
            endColor = color;
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