using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace UIEffectPlugin
{
    public class UIParticleEffect : MonoBehaviour
    {
        [Header("Particle Settings")]
        [SerializeField] private Sprite particleSprite;
        [SerializeField] private int initialPoolSize = 20;
        [SerializeField] private float particleLifeTime = 1f;
        [SerializeField] private Vector2 particleSizeRange = new Vector2(10f, 30f);
        [SerializeField] private Vector2 emitRange = new Vector2(50f, 50f);
        [SerializeField] private Vector2 velocityRange = new Vector2(-100f, 100f);

        [Header("Color Settings")]
        [SerializeField] private Color startColor = Color.white;
        [SerializeField] private Color endColor = Color.clear;

        [Header("Emission Settings")]
        [SerializeField] private bool emitOnStart = false;
        [SerializeField] private float emitInterval = 0.1f;

        private RectTransform rectTransform;
        private List<Particle> activeParticles = new List<Particle>();
        private Stack<Particle> particlePool = new Stack<Particle>();
        private float emitTimer = 0f;
        private bool isEmitting = false;

        private struct Particle
        {
            public RectTransform transform;
            public Image image;
            public Vector2 velocity;
            public float lifeTime;
            public float maxLifeTime;
            public Color startCol;
            public Color endCol;
            public float size;
        }

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            InitializePool();
        }

        private void Start()
        {
            if (emitOnStart)
            {
                StartEmission();
            }
        }

        private void Update()
        {
            if (isEmitting)
            {
                emitTimer += Time.deltaTime;
                if (emitTimer >= emitInterval)
                {
                    Emit(1);
                    emitTimer = 0f;
                }
            }

            UpdateParticles();
        }

        private void InitializePool()
        {
            for (int i = 0; i < initialPoolSize; i++)
            {
                Particle p = CreatePooledParticle();
                particlePool.Push(p);
            }
        }

        private Particle CreatePooledParticle()
        {
            GameObject particleObj = new GameObject("UI_Particle");
            particleObj.transform.SetParent(transform, false);
            particleObj.SetActive(false);

            RectTransform particleRect = particleObj.AddComponent<RectTransform>();
            Image particleImage = particleObj.AddComponent<Image>();

            particleImage.sprite = particleSprite;

            return new Particle
            {
                transform = particleRect,
                image = particleImage
            };
        }

        public void Emit(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Particle p;
                if (particlePool.Count > 0)
                {
                    p = particlePool.Pop();
                }
                else
                {
                    p = CreatePooledParticle();
                }

                float size = Random.Range(particleSizeRange.x, particleSizeRange.y);
                p.transform.sizeDelta = new Vector2(size, size);
                p.size = size;

                Vector2 position = new Vector2(
                    Random.Range(-emitRange.x, emitRange.x),
                    Random.Range(-emitRange.y, emitRange.y)
                );
                p.transform.anchoredPosition = position;

                Vector2 velocity = new Vector2(
                    Random.Range(velocityRange.x, velocityRange.y),
                    Random.Range(velocityRange.x, velocityRange.y)
                );

                p.velocity = velocity;
                p.lifeTime = particleLifeTime;
                p.maxLifeTime = particleLifeTime;
                p.startCol = startColor;
                p.endCol = endColor;
                p.image.color = startColor;
                p.transform.localScale = Vector3.one;
                p.transform.gameObject.SetActive(true);

                activeParticles.Add(p);
            }
        }

        public void Burst(int count)
        {
            Emit(count);
        }

        public void StartEmission()
        {
            isEmitting = true;
        }

        public void StopEmission()
        {
            isEmitting = false;
        }

        public void ClearParticles()
        {
            for (int i = activeParticles.Count - 1; i >= 0; i--)
            {
                Particle p = activeParticles[i];
                p.transform.gameObject.SetActive(false);
                particlePool.Push(p);
                activeParticles.RemoveAt(i);
            }
        }

        private void UpdateParticles()
        {
            for (int i = activeParticles.Count - 1; i >= 0; i--)
            {
                Particle p = activeParticles[i];
                p.lifeTime -= Time.deltaTime;

                if (p.lifeTime <= 0f)
                {
                    p.transform.gameObject.SetActive(false);
                    particlePool.Push(p);
                    activeParticles.RemoveAt(i);
                    continue;
                }

                float t = 1f - (p.lifeTime / p.maxLifeTime);
                p.transform.anchoredPosition += p.velocity * Time.deltaTime;
                p.image.color = Color.Lerp(p.startCol, p.endCol, t);
                float scale = Mathf.Lerp(1f, 0f, t);
                p.transform.localScale = new Vector3(scale, scale, scale);

                activeParticles[i] = p;
            }
        }

        public void SetParticleSprite(Sprite sprite)
        {
            particleSprite = sprite;
            foreach (var p in activeParticles)
            {
                p.image.sprite = sprite;
            }
            foreach (var p in particlePool)
            {
                p.image.sprite = sprite;
            }
        }

        public void SetParticleLifeTime(float time)
        {
            particleLifeTime = Mathf.Max(time, 0.1f);
        }

        public void SetColors(Color start, Color end)
        {
            startColor = start;
            endColor = end;
        }

        public int ActiveParticleCount => activeParticles.Count;
        public int PooledParticleCount => particlePool.Count;
    }
}