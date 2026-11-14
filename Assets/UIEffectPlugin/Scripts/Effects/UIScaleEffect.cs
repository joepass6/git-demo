using UnityEngine;

namespace UIEffectPlugin
{
    public class UIScaleEffect : UITweenBase
    {
        [Header("Scale Settings")]
        [SerializeField] private Vector3 startScale = Vector3.zero;
        [SerializeField] private Vector3 endScale = Vector3.one;

        private RectTransform rectTransform;
        private Vector3 originalScale;
        private Vector3 tempVector = new Vector3();

        protected override void Init()
        {
            rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                enabled = false;
                return;
            }
            originalScale = rectTransform.localScale;
            rectTransform.localScale = startScale;
        }

        protected override void UpdateTween(float t)
        {
            tempVector.x = Mathf.Lerp(startScale.x, endScale.x, t);
            tempVector.y = Mathf.Lerp(startScale.y, endScale.y, t);
            tempVector.z = Mathf.Lerp(startScale.z, endScale.z, t);
            rectTransform.localScale = tempVector;
        }

        public void SetStartScale(Vector3 scale)
        {
            startScale = scale;
        }

        public void SetEndScale(Vector3 scale)
        {
            endScale = scale;
        }

        public void ResetToOriginal()
        {
            if (rectTransform != null)
            {
                rectTransform.localScale = originalScale;
            }
        }

        public static UIScaleEffect Create(GameObject target, Vector3 startScale, Vector3 endScale, float duration = 0.5f)
        {
            if (target == null) return null;
            
            UIScaleEffect effect = target.GetComponent<UIScaleEffect>();
            if (effect == null)
            {
                effect = target.AddComponent<UIScaleEffect>();
            }
            effect.SetStartScale(startScale);
            effect.SetEndScale(endScale);
            effect.DurationValue = duration;
            return effect;
        }
    }
}