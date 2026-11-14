using UnityEngine;
using System.Collections;
using System;

namespace UIEffectPlugin
{
    public static class CoroutineHelper
    {
        private static MonoBehaviour coroutineRunner;
        private static readonly object lockObj = new object();

        private static MonoBehaviour CoroutineRunner
        {
            get
            {
                lock (lockObj)
                {
                    if (coroutineRunner == null)
                    {
                        GameObject runnerObj = new GameObject("CoroutineRunner");
                        coroutineRunner = runnerObj.AddComponent<CoroutineRunnerBehaviour>();
                        GameObject.DontDestroyOnLoad(runnerObj);
                    }
                    return coroutineRunner;
                }
            }
        }

        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null) return null;
            return CoroutineRunner.StartCoroutine(routine);
        }

        public static void StopCoroutine(Coroutine routine)
        {
            if (routine == null) return;
            CoroutineRunner.StopCoroutine(routine);
        }

        public static Coroutine WaitForSeconds(float seconds, Action callback)
        {
            if (callback == null) return null;
            return StartCoroutine(WaitForSecondsCoroutine(seconds, callback));
        }

        private static IEnumerator WaitForSecondsCoroutine(float seconds, Action callback)
        {
            yield return new WaitForSeconds(seconds);
            callback();
        }

        public static Coroutine WaitForFrames(int frames, Action callback)
        {
            if (callback == null) return null;
            return StartCoroutine(WaitForFramesCoroutine(frames, callback));
        }

        private static IEnumerator WaitForFramesCoroutine(int frames, Action callback)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
            callback();
        }

        public static Coroutine LerpFloat(float start, float end, float duration, Action<float> onUpdate, Action onComplete = null)
        {
            if (onUpdate == null) return null;
            return StartCoroutine(LerpFloatCoroutine(start, end, duration, onUpdate, onComplete));
        }

        private static IEnumerator LerpFloatCoroutine(float start, float end, float duration, Action<float> onUpdate, Action onComplete)
        {
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);
                onUpdate(Mathf.Lerp(start, end, t));
                yield return null;
            }
            onUpdate(end);
            onComplete?.Invoke();
        }

        public static Coroutine LerpVector2(Vector2 start, Vector2 end, float duration, Action<Vector2> onUpdate, Action onComplete = null)
        {
            if (onUpdate == null) return null;
            return StartCoroutine(LerpVector2Coroutine(start, end, duration, onUpdate, onComplete));
        }

        private static IEnumerator LerpVector2Coroutine(Vector2 start, Vector2 end, float duration, Action<Vector2> onUpdate, Action onComplete)
        {
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);
                onUpdate(Vector2.Lerp(start, end, t));
                yield return null;
            }
            onUpdate(end);
            onComplete?.Invoke();
        }

        public static Coroutine LerpVector3(Vector3 start, Vector3 end, float duration, Action<Vector3> onUpdate, Action onComplete = null)
        {
            if (onUpdate == null) return null;
            return StartCoroutine(LerpVector3Coroutine(start, end, duration, onUpdate, onComplete));
        }

        private static IEnumerator LerpVector3Coroutine(Vector3 start, Vector3 end, float duration, Action<Vector3> onUpdate, Action onComplete)
        {
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);
                onUpdate(Vector3.Lerp(start, end, t));
                yield return null;
            }
            onUpdate(end);
            onComplete?.Invoke();
        }

        public static Coroutine LerpColor(Color start, Color end, float duration, Action<Color> onUpdate, Action onComplete = null)
        {
            if (onUpdate == null) return null;
            return StartCoroutine(LerpColorCoroutine(start, end, duration, onUpdate, onComplete));
        }

        private static IEnumerator LerpColorCoroutine(Color start, Color end, float duration, Action<Color> onUpdate, Action onComplete)
        {
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);
                onUpdate(Color.Lerp(start, end, t));
                yield return null;
            }
            onUpdate(end);
            onComplete?.Invoke();
        }

        private class CoroutineRunnerBehaviour : MonoBehaviour
        {
        }
    }
}