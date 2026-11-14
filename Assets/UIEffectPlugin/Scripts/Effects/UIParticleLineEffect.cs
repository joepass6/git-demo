using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace UIEffectPlugin
{
    public class UIParticleLineEffect : MonoBehaviour
    {
        [Header("Line Settings")]
        [SerializeField] private Sprite particleSprite;
        [SerializeField] private int particlesPerLine = 10;
        [SerializeField] private float particleSpacing = 20f;
        [SerializeField] private float fadeSpeed = 2f;

        [Header("Color Settings")]
        [SerializeField] private Color particleColor = Color.white;

        private RectTransform rectTransform;
        private Image[] particles;
        private float[] targetAlpha;
        private float[] currentAlpha;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        private void Start()
        {
            CreateParticles();
        }

        private void Update()
        {
            UpdateLine();
        }

        public void CreateParticles()
        {
            if (particles != null)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] != null)
                    {
                        Destroy(particles[i].gameObject);
                    }
                }
            }

            particles = new Image[particlesPerLine];
            targetAlpha = new float[particlesPerLine];
            currentAlpha = new float[particlesPerLine];

            for (int i = 0; i < particlesPerLine; i++)
            {
                GameObject particleObj = new GameObject("Line_Particle_" + i);
                particleObj.transform.SetParent(transform, false);

                RectTransform particleRect = particleObj.AddComponent<RectTransform>();
                Image particleImage = particleObj.AddComponent<Image>();

                particleImage.sprite = particleSprite;
                particleImage.color = new Color(particleColor.r, particleColor.g, particleColor.b, 0f);

                particleRect.sizeDelta = new Vector2(10f, 10f);
                particleRect.anchoredPosition = new Vector2(-(particlesPerLine - 1) * particleSpacing / 2f + i * particleSpacing, 0f);

                particles[i] = particleImage;
                targetAlpha[i] = 0f;
                currentAlpha[i] = 0f;
            }
        }

        public void ActivateLine()
        {
            for (int i = 0; i < particlesPerLine; i++)
            {
                targetAlpha[i] = 1f;
            }
        }

        public void ActivateLineFromLeft()
        {
            for (int i = 0; i < particlesPerLine; i++)
            {
                targetAlpha[i] = Mathf.Lerp(0f, 1f, (float)i / particlesPerLine);
            }
        }

        public void DeactivateLine()
        {
            for (int i = 0; i < particlesPerLine; i++)
            {
                targetAlpha[i] = 0f;
            }
        }

        private void UpdateLine()
        {
            for (int i = 0; i < particles.Length; i++)
            {
                currentAlpha[i] = Mathf.Lerp(currentAlpha[i], targetAlpha[i], fadeSpeed * Time.deltaTime);
                Color color = particles[i].color;
                color.a = currentAlpha[i];
                particles[i].color = color;
            }
        }

        public void SetParticleColor(Color color)
        {
            particleColor = color;
            for (int i = 0; i < particles.Length; i++)
            {
                Color c = particles[i].color;
                c.r = color.r;
                c.g = color.g;
                c.b = color.b;
                particles[i].color = c;
            }
        }

        public void SetFadeSpeed(float speed)
        {
            fadeSpeed = Mathf.Max(speed, 0.1f);
        }

        private void OnDestroy()
        {
            if (particles != null)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] != null)
                    {
                        Destroy(particles[i].gameObject);
                    }
                }
            }
        }
    }
}