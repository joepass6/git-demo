using UnityEditor;
using UnityEngine;

namespace DeskPet
{
    /// <summary>
    /// 桌宠外观资源。
    /// 默认形象是代码生成的占位图（不依赖任何下载素材）；
    /// 用户在桌宠窗口里指定一个 Sprite / PNG 后，就改用用户的贴图。
    /// </summary>
    internal static class DeskPetArt
    {
        static Texture2D _placeholder;

        static string _cachedGuid;
        static Texture2D _cachedTexture;
        static Rect _cachedCoords = new Rect(0f, 0f, 1f, 1f);
        static UnityEngine.Object _cachedObject;

        /// <summary>代码绘制的占位形象。</summary>
        public static Texture2D Placeholder
        {
            get
            {
                if (_placeholder == null) _placeholder = BuildPlaceholder();
                return _placeholder;
            }
        }

        /// <summary>贴图槽当前显示的对象（Sprite 或 Texture2D），供 ObjectField 使用。</summary>
        public static Object GetCustomObject(string guid)
        {
            EnsureLoaded(guid);
            return _cachedObject;
        }

        /// <summary>取用户自定义贴图；返回 false 表示应退回占位图。</summary>
        public static bool TryGetCustom(string guid, out Texture2D texture, out Rect coords)
        {
            EnsureLoaded(guid);
            texture = _cachedTexture;
            coords = _cachedCoords;
            return texture != null;
        }

        static void EnsureLoaded(string guid)
        {
            if (guid == _cachedGuid) return;

            _cachedGuid = guid;
            _cachedTexture = null;
            _cachedObject = null;
            _cachedCoords = new Rect(0f, 0f, 1f, 1f);

            if (string.IsNullOrEmpty(guid)) return;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;

            // 优先用 Sprite（单图模式导入的 PNG 会自动生成一个 Sprite 子资源）
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null && sprite.texture != null)
            {
                _cachedTexture = sprite.texture;
                _cachedObject = sprite;
                Rect r = sprite.rect; // 像素坐标，不会像 textureRect 那样在打包后抛异常
                _cachedCoords = new Rect(
                    r.x / _cachedTexture.width,
                    r.y / _cachedTexture.height,
                    r.width / _cachedTexture.width,
                    r.height / _cachedTexture.height);
                return;
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
            {
                _cachedTexture = tex;
                _cachedObject = tex;
            }
        }

        // ---------------- 占位形象（纯代码绘制） ----------------

        static Texture2D BuildPlaceholder()
        {
            const int size = 64;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "DeskPetPlaceholder",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color body = new Color(0.98f, 0.56f, 0.32f);
            Color bodyShade = new Color(0.85f, 0.42f, 0.21f);
            Color belly = new Color(1f, 0.88f, 0.78f);
            Color cheek = new Color(1f, 0.60f, 0.55f, 0.75f);
            Color ink = new Color(0.17f, 0.13f, 0.12f);
            Color glint = Color.white;

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color accum = Color.clear;
                    int hits = 0;

                    // 2x2 超采样，让边缘有一点抗锯齿
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float nx = ((x + (sx + 0.5f) * 0.5f) / size) * 2f - 1f;
                            float ny = ((y + (sy + 0.5f) * 0.5f) / size) * 2f - 1f;

                            if (!(InBody(nx, ny) || InEar(nx, ny) || InTail(nx, ny))) continue;

                            Color c = body;
                            if (InShade(ny)) c = bodyShade;
                            if (InBelly(nx, ny)) c = belly;
                            if (InCheek(nx, ny)) c = cheek;
                            if (InInnerEar(nx, ny)) c = bodyShade;
                            if (InMouth(nx, ny)) c = ink;
                            if (InEye(nx, ny)) c = ink;
                            if (InGlint(nx, ny)) c = glint;

                            accum += c;
                            hits++;
                        }
                    }

                    if (hits == 0)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    Color avg = accum / hits;
                    avg.a = hits / 4f;
                    pixels[y * size + x] = avg;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        static bool InBody(float x, float y)
        {
            float ax = Mathf.Abs(x / 0.74f);
            float ay = Mathf.Abs((y + 0.04f) / 0.68f);
            return Mathf.Pow(ax, 2.6f) + Mathf.Pow(ay, 2.6f) <= 1f;
        }

        static bool InEar(float x, float y)
        {
            float dy = y - 0.58f;
            return (x - 0.44f) * (x - 0.44f) + dy * dy <= 0.0289f
                || (x + 0.44f) * (x + 0.44f) + dy * dy <= 0.0289f;
        }

        static bool InInnerEar(float x, float y)
        {
            float dy = y - 0.56f;
            return (x - 0.44f) * (x - 0.44f) + dy * dy <= 0.0072f
                || (x + 0.44f) * (x + 0.44f) + dy * dy <= 0.0072f;
        }

        static bool InTail(float x, float y)
        {
            return (x + 0.72f) * (x + 0.72f) + (y + 0.58f) * (y + 0.58f) <= 0.0169f;
        }

        static bool InBelly(float x, float y)
        {
            float dx = x / 0.40f;
            float dy = (y + 0.34f) / 0.30f;
            return dx * dx + dy * dy <= 1f;
        }

        static bool InShade(float y)
        {
            return y < -0.50f;
        }

        static bool InCheek(float x, float y)
        {
            float dy = y + 0.08f;
            return (x - 0.44f) * (x - 0.44f) + dy * dy <= 0.0049f
                || (x + 0.44f) * (x + 0.44f) + dy * dy <= 0.0049f;
        }

        static bool InEye(float x, float y)
        {
            float dx = (Mathf.Abs(x) - 0.27f) / 0.105f;
            float dy = (y - 0.10f) / 0.125f;
            return dx * dx + dy * dy <= 1f;
        }

        static bool InGlint(float x, float y)
        {
            float dx = Mathf.Abs(x) - 0.24f;
            float dy = y - 0.15f;
            return dx * dx + dy * dy <= 0.0009f;
        }

        static bool InMouth(float x, float y)
        {
            if (Mathf.Abs(x) > 0.085f) return false;
            return Mathf.Abs(y - (-0.17f + 1.2f * x * x)) <= 0.018f;
        }
    }
}
