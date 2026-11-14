using UnityEngine;
using UnityEngine.UI;

namespace UIEffectPlugin.Examples
{
    public class ExampleUsage : MonoBehaviour
    {
        [Header("UI Elements")]
        public Image fadeImage;
        public Image scaleImage;
        public Image moveImage;
        public Image rotateImage;
        public Image shakeImage;
        public Image glowImage;
        public Image gradientImage;
        public Text typewriterText;
        public UIParticleEffect particleEffect;

        [Header("Buttons")]
        public Button fadeButton;
        public Button scaleButton;
        public Button moveButton;
        public Button rotateButton;
        public Button shakeButton;
        public Button glowButton;
        public Button gradientButton;
        public Button typewriterButton;
        public Button particleButton;

        private void Start()
        {
            RegisterButtonListeners();
        }

        private void RegisterButtonListeners()
        {
            if (fadeButton != null) fadeButton.onClick.AddListener(PlayFadeEffect);
            if (scaleButton != null) scaleButton.onClick.AddListener(PlayScaleEffect);
            if (moveButton != null) moveButton.onClick.AddListener(PlayMoveEffect);
            if (rotateButton != null) rotateButton.onClick.AddListener(PlayRotateEffect);
            if (shakeButton != null) shakeButton.onClick.AddListener(PlayShakeEffect);
            if (glowButton != null) glowButton.onClick.AddListener(PlayGlowEffect);
            if (gradientButton != null) gradientButton.onClick.AddListener(PlayGradientEffect);
            if (typewriterButton != null) typewriterButton.onClick.AddListener(PlayTypewriterEffect);
            if (particleButton != null) particleButton.onClick.AddListener(PlayParticleEffect);
        }

        private void PlayFadeEffect()
        {
            if (fadeImage == null) return;
            
            UIFadeEffect effect = fadeImage.gameObject.AddFadeEffect(1f, 0f, 0.5f);
            effect.EaseTypeValue = EaseType.EaseOutQuad;
            effect.Play(() =>
            {
                fadeImage.gameObject.AddFadeEffect(0f, 1f, 0.5f)?.Play();
            });
        }

        private void PlayScaleEffect()
        {
            if (scaleImage == null) return;
            
            UIScaleEffect effect = scaleImage.gameObject.AddScaleEffect(Vector3.one, new Vector3(1.5f, 1.5f, 1.5f), 0.5f);
            effect.EaseTypeValue = EaseType.EaseOutBack;
            effect.LoopValue = true;
            effect.PingPongValue = true;
            effect.Play();
        }

        private void PlayMoveEffect()
        {
            if (moveImage == null) return;
            
            Vector2 startPos = moveImage.rectTransform.anchoredPosition;
            Vector2 endPos = startPos + new Vector2(100f, 0f);
            UIMoveEffect effect = moveImage.gameObject.AddMoveEffect(startPos, endPos, 1f, false);
            effect.EaseTypeValue = EaseType.EaseInOutQuad;
            effect.LoopValue = true;
            effect.PingPongValue = true;
            effect.Play();
        }

        private void PlayRotateEffect()
        {
            if (rotateImage == null) return;
            
            UIRotateEffect effect = rotateImage.gameObject.AddRotateEffect(Vector3.zero, new Vector3(0f, 0f, 360f), 2f);
            effect.EaseTypeValue = EaseType.Linear;
            effect.LoopValue = true;
            effect.Play();
        }

        private void PlayShakeEffect()
        {
            if (shakeImage == null) return;
            
            UIShakeEffect effect = shakeImage.gameObject.AddShakeEffect(new Vector2(10f, 10f), 15, 0.5f);
            effect.Play();
        }

        private void PlayGlowEffect()
        {
            if (glowImage == null) return;
            
            UIGlowEffect effect = glowImage.gameObject.AddGlowEffect(Color.yellow, 0f, 1f, 1f);
            effect.LoopValue = true;
            effect.PingPongValue = true;
            effect.Play();
        }

        private void PlayGradientEffect()
        {
            if (gradientImage == null) return;
            
            UIColorGradientEffect effect = gradientImage.gameObject.AddColorGradientEffect(Color.red, Color.blue, 2f);
            effect.LoopValue = true;
            effect.PingPongValue = true;
            effect.Play();
        }

        private void PlayTypewriterEffect()
        {
            if (typewriterText == null) return;
            
            UITypewriterEffect effect = typewriterText.gameObject.AddTypewriterEffect("欢迎使用UI特效插件！\n这是一个打字机效果演示。", 15f);
            effect.Play(() => Debug.Log("打字机效果完成！"));
        }

        private void PlayParticleEffect()
        {
            if (particleEffect == null) return;
            particleEffect.Burst(20);
        }
    }
}