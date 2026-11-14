using UnityEngine;

namespace UIEffectPlugin
{
    public class UIMoveEffect : UITweenBase
    {
        [Header("Move Settings")]
        [SerializeField] private Vector2 startPosition;
        [SerializeField] private Vector2 endPosition;
        [SerializeField] private bool useLocalPosition = true;

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
            if (useLocalPosition)
            {
                originalPosition = rectTransform.localPosition;
                rectTransform.localPosition = startPosition;
            }
            else
            {
                originalPosition = rectTransform.anchoredPosition;
                rectTransform.anchoredPosition = startPosition;
            }
        }

        protected override void UpdateTween(float t)
        {
            tempVector.x = Mathf.Lerp(startPosition.x, endPosition.x, t);
            tempVector.y = Mathf.Lerp(startPosition.y, endPosition.y, t);
            
            if (useLocalPosition)
            {
                rectTransform.localPosition = tempVector;
            }
            else
            {
                rectTransform.anchoredPosition = tempVector;
            }
        }

        public void SetStartPosition(Vector2 position)
        {
            startPosition = position;
        }

        public void SetEndPosition(Vector2 position)
        {
            endPosition = position;
        }

        public void ResetToOriginal()
        {
            if (rectTransform == null) return;
            
            if (useLocalPosition)
            {
                rectTransform.localPosition = originalPosition;
            }
            else
            {
                rectTransform.anchoredPosition = originalPosition;
            }
        }

        public bool UseLocalPosition
        {
            get { return useLocalPosition; }
            set { useLocalPosition = value; }
        }
    }
}