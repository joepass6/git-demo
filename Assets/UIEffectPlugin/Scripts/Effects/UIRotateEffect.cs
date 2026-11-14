using UnityEngine;

namespace UIEffectPlugin
{
    public class UIRotateEffect : UITweenBase
    {
        [Header("Rotate Settings")]
        [SerializeField] private Vector3 startRotation = Vector3.zero;
        [SerializeField] private Vector3 endRotation = new Vector3(0f, 0f, 360f);

        private RectTransform rectTransform;
        private Vector3 originalRotation;
        private Vector3 tempVector = new Vector3();

        protected override void Init()
        {
            rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                enabled = false;
                return;
            }
            originalRotation = rectTransform.localEulerAngles;
            rectTransform.localEulerAngles = startRotation;
        }

        protected override void UpdateTween(float t)
        {
            tempVector.x = Mathf.Lerp(startRotation.x, endRotation.x, t);
            tempVector.y = Mathf.Lerp(startRotation.y, endRotation.y, t);
            tempVector.z = Mathf.Lerp(startRotation.z, endRotation.z, t);
            rectTransform.localEulerAngles = tempVector;
        }

        public void SetStartRotation(Vector3 rotation)
        {
            startRotation = rotation;
        }

        public void SetEndRotation(Vector3 rotation)
        {
            endRotation = rotation;
        }

        public void ResetToOriginal()
        {
            if (rectTransform != null)
            {
                rectTransform.localEulerAngles = originalRotation;
            }
        }
    }
}