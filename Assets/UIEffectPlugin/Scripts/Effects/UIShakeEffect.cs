using UnityEngine;

namespace UIEffectPlugin
{
    public class UIShakeEffect : UITweenBase
    {
        [Header("Shake Settings")]
        [SerializeField] private Vector2 shakeAmount = new Vector2(5f, 5f);
        [SerializeField] private int shakeFrequency = 10;
        [SerializeField] private float shakeSeed = 0f;

        private RectTransform rectTransform;
        private Vector2 originalPosition;
        private Vector2 tempVector = new Vector2();

        protected override void Init()
        {
            rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                enabled = false;
                return;
            }
            originalPosition = rectTransform.anchoredPosition;
            if (shakeSeed == 0f)
            {
                shakeSeed = Random.Range(0f, 10000f);
            }
        }

        protected override void UpdateTween(float t)
        {
            float shakeIntensity = Mathf.Lerp(1f, 0f, t);
            float shakeX = Mathf.PerlinNoise(t * shakeFrequency + shakeSeed, shakeSeed) * 2f - 1f;
            float shakeY = Mathf.PerlinNoise(shakeSeed, t * shakeFrequency + shakeSeed) * 2f - 1f;
            
            tempVector.x = originalPosition.x + shakeX * shakeAmount.x * shakeIntensity;
            tempVector.y = originalPosition.y + shakeY * shakeAmount.y * shakeIntensity;
            
            rectTransform.anchoredPosition = tempVector;
        }

        public void SetShakeAmount(Vector2 amount)
        {
            shakeAmount = amount;
        }

        public void SetShakeFrequency(int frequency)
        {
            shakeFrequency = frequency;
        }

        public void ResetToOriginal()
        {
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = originalPosition;
            }
        }
    }
}