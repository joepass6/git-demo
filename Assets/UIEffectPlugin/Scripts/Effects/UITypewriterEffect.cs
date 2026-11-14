using UnityEngine;
using UnityEngine.UI;

namespace UIEffectPlugin
{
    [RequireComponent(typeof(Text))]
    public class UITypewriterEffect : UITweenBase
    {
        [Header("Typewriter Settings")]
        [SerializeField] private string fullText = "";
        [SerializeField] private float charsPerSecond = 10f;

        private Text textComponent;
        private string originalText;
        private char[] charBuffer;

        protected override void Init()
        {
            textComponent = GetComponent<Text>();
            if (textComponent == null)
            {
                enabled = false;
                return;
            }
            originalText = textComponent.text;
            
            if (!string.IsNullOrEmpty(fullText))
            {
                textComponent.text = "";
            }
            else
            {
                fullText = originalText;
                textComponent.text = "";
            }
            
            charBuffer = fullText.ToCharArray();
        }

        protected override void UpdateTween(float t)
        {
            if (charBuffer == null || charBuffer.Length == 0) return;
            
            int targetLength = Mathf.Clamp(Mathf.FloorToInt(t * charBuffer.Length), 0, charBuffer.Length);
            
            if (targetLength > 0)
            {
                textComponent.text = new string(charBuffer, 0, targetLength);
            }
            else
            {
                textComponent.text = "";
            }
        }

        public void SetFullText(string text)
        {
            fullText = text;
            if (!string.IsNullOrEmpty(text))
            {
                charBuffer = text.ToCharArray();
            }
        }

        public void SetCharsPerSecond(float chars)
        {
            charsPerSecond = Mathf.Max(chars, 1f);
        }

        public void ResetToOriginal()
        {
            if (textComponent != null)
            {
                textComponent.text = originalText;
            }
        }

        public void SkipToEnd()
        {
            if (textComponent != null)
            {
                textComponent.text = fullText;
            }
            Complete();
        }
    }
}