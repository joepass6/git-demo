using UnityEngine;
using System;

namespace UIEffectPlugin
{
    public abstract class UITweenBase : MonoBehaviour
    {
        [Header("Tween Settings")]
        [SerializeField] protected float duration = 0.5f;
        [SerializeField] protected EaseType easeType = EaseType.EaseOutQuad;
        [SerializeField] protected bool playOnAwake = false;
        [SerializeField] protected bool loop = false;
        [SerializeField] protected bool pingPong = false;

        [Header("Delay Settings")]
        [SerializeField] protected float startDelay = 0f;

        protected bool isPlaying = false;
        protected float currentTime = 0f;
        protected float delayTime = 0f;
        protected bool isReversed = false;
        protected bool isInitialized = false;

        public event Action OnComplete;
        public event Action OnStart;

        private static readonly Func<float, float>[] easeFunctions =
        {
            t => t,
            t => t * t,
            t => t * (2f - t),
            t => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t,
            t => t * t * t,
            t => (--t) * t * t + 1f,
            t => t < 0.5f ? 4f * t * t * t : (t - 1f) * (2f * t - 2f) * (2f * t - 2f) + 1f,
            t => t * t * (3f * t - 2f),
            t => (--t) * t * (3f * t + 2f) + 1f,
            GetEaseInOutBack,
            GetEaseInBounce,
            GetEaseOutBounce,
            GetEaseInOutBounce,
            GetEaseInElastic,
            GetEaseOutElastic,
            GetEaseInOutElastic
        };

        protected virtual void Awake()
        {
        }

        protected virtual void Start()
        {
            if (playOnAwake)
            {
                Play();
            }
        }

        protected virtual void Update()
        {
            if (!isPlaying) return;

            if (delayTime < startDelay)
            {
                delayTime += Time.deltaTime;
                return;
            }

            currentTime += Time.deltaTime;

            float t = Mathf.Clamp01(currentTime / duration);
            t = easeFunctions[(int)easeType](t);

            if (pingPong && isReversed)
            {
                t = 1f - t;
            }

            UpdateTween(t);

            if (currentTime >= duration)
            {
                if (loop)
                {
                    if (pingPong)
                    {
                        isReversed = !isReversed;
                    }
                    currentTime = 0f;
                }
                else
                {
                    Complete();
                }
            }
        }

        protected abstract void Init();
        protected abstract void UpdateTween(float t);

        protected virtual void ResetTween()
        {
            currentTime = 0f;
            delayTime = 0f;
            isReversed = false;
        }

        public virtual void Play()
        {
            if (!isInitialized)
            {
                Init();
                isInitialized = true;
            }
            ResetTween();
            isPlaying = true;
            OnStart?.Invoke();
        }

        public virtual void Play(Action onComplete)
        {
            OnComplete += onComplete;
            Play();
        }

        public virtual void Pause()
        {
            isPlaying = false;
        }

        public virtual void Resume()
        {
            isPlaying = true;
        }

        public virtual void Stop()
        {
            isPlaying = false;
            ResetTween();
        }

        protected virtual void Complete()
        {
            isPlaying = false;
            UpdateTween(isReversed ? 0f : 1f);
            OnComplete?.Invoke();
        }

        private static float GetEaseInOutBack(float t)
        {
            float s = 1.70158f;
            return t < 0.5f ? 0.5f * (2f * t) * (2f * t) * ((s + 1f) * 2f * t - s) : 0.5f * ((2f * t - 2f) * (2f * t - 2f) * ((s + 1f) * (t * 2f - 2f) + s) + 2f);
        }

        private static float GetEaseOutBounce(float t)
        {
            float n1 = 7.5625f;
            float d1 = 2.75f;

            if (t < 1f / d1)
                return n1 * t * t;
            if (t < 2f / d1)
                return n1 * (t -= 1.5f / d1) * t + 0.75f;
            if (t < 2.5f / d1)
                return n1 * (t -= 2.25f / d1) * t + 0.9375f;
            return n1 * (t -= 2.625f / d1) * t + 0.984375f;
        }

        private static float GetEaseInBounce(float t)
        {
            return 1f - GetEaseOutBounce(1f - t);
        }

        private static float GetEaseInOutBounce(float t)
        {
            return t < 0.5f ? 0.5f * (1f - GetEaseOutBounce(1f - 2f * t)) : 0.5f * (1f + GetEaseOutBounce(2f * t - 1f));
        }

        private static float GetEaseOutElastic(float t)
        {
            if (t == 0f || t == 1f) return t;

            float p = 0.3f;
            float s = p / 4f;

            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - s) * (2f * Mathf.PI) / p) + 1f;
        }

        private static float GetEaseInElastic(float t)
        {
            return 1f - GetEaseOutElastic(1f - t);
        }

        private static float GetEaseInOutElastic(float t)
        {
            return t < 0.5f ? 0.5f * (1f - GetEaseOutElastic(1f - 2f * t)) : 0.5f * (1f + GetEaseOutElastic(2f * t - 1f));
        }

        public bool IsPlaying => isPlaying;
        public float Duration => duration;
        public EaseType EaseType => easeType;
        public bool Loop => loop;
        public bool PingPong => pingPong;

        public float DurationValue
        {
            get => duration;
            set => duration = value;
        }

        public EaseType EaseTypeValue
        {
            get => easeType;
            set => easeType = value;
        }

        public bool LoopValue
        {
            get => loop;
            set => loop = value;
        }

        public bool PingPongValue
        {
            get => pingPong;
            set => pingPong = value;
        }
    }
}