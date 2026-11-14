using System;
using UnityEditor;
using UnityEngine;

namespace DeskPet
{
    /// <summary>
    /// 桌宠外观资源。
    /// 默认形象是内置的猫猫逐帧动画（由用户提供的 cat spring2.gif 转成逐帧像素后打包）；
    /// 用户在桌宠窗口里指定一个 Sprite / PNG 后，就改用用户的单帧贴图；
    /// 都不可用时退回代码绘制的占位形象。
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
        public static UnityEngine.Object GetCustomObject(string guid)
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

        // ---------------- 给原生分层窗口用的逐帧像素 ----------------

        internal class PetFrame
        {
            public Color32[] Pixels;   // 行序自下而上，与 Texture2D.GetPixels32 一致
            public int Width;
            public int Height;
            public float Delay;        // 秒；<= 0 视为静态帧
        }

        // 内置动画资源：cat spring2.gif 的 51 帧按比例缩到 344x192 后用 PNG 打包
        const string BuiltinAssetPath = "Assets/Editor/DeskPet/Art/DeskPetCat.bytes";
        const byte BuiltinVersion = 1;

        static PetFrame[] _frames;
        static string _framesKey;
        static bool _framesIsCustom;
        static bool _framesIsPlaceholder;

        /// <summary>
        /// 取宠物动画帧：贴图槽为空时用内置猫猫动画，指定了 Sprite/PNG 时用单帧静态图。
        /// isPlaceholder = true 表示连内置动画都读不到，正在用代码绘制的占位形象。
        /// </summary>
        public static PetFrame[] GetFrames(string guid, out bool isCustom, out bool isPlaceholder)
        {
            string key = guid ?? string.Empty;
            if (_frames == null || key != _framesKey)
            {
                _framesKey = key;
                BuildFrames(guid);
            }

            isCustom = _framesIsCustom;
            isPlaceholder = _framesIsPlaceholder;
            return _frames;
        }

        static void BuildFrames(string guid)
        {
            _frames = null;
            _framesIsCustom = false;
            _framesIsPlaceholder = false;

            if (!string.IsNullOrEmpty(guid))
            {
                PetFrame single = LoadSingle(guid);
                if (single != null)
                {
                    _frames = new[] { single };
                    _framesIsCustom = true;
                    return;
                }
            }
            else
            {
                PetFrame[] builtin = LoadBuiltinFrames();
                if (builtin != null && builtin.Length > 0)
                {
                    _frames = builtin;
                    return;
                }
            }

            Texture2D placeholder = Placeholder;
            _frames = new[]
            {
                new PetFrame
                {
                    Pixels = placeholder.GetPixels32(),
                    Width = placeholder.width,
                    Height = placeholder.height,
                    Delay = 0f
                }
            };
            _framesIsPlaceholder = true;
        }

        /// <summary>用户指定的一张 Sprite / PNG，作为单帧静态形象。</summary>
        static PetFrame LoadSingle(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;

            // 优先 Sprite：按 sprite.rect 从图集里裁出属于它的那块
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null && sprite.texture != null)
            {
                Color32[] all = ReadPixels(sprite.texture);
                if (all != null)
                {
                    Color32[] crop;
                    int w, h;
                    CropBottomUp(all, sprite.texture.width, sprite.texture.height, sprite.rect, out crop, out w, out h);
                    if (crop != null) return new PetFrame { Pixels = crop, Width = w, Height = h, Delay = 0f };
                }
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
            {
                Color32[] all = ReadPixels(tex);
                if (all != null) return new PetFrame { Pixels = all, Width = tex.width, Height = tex.height, Delay = 0f };
            }

            return null;
        }

        /// <summary>
        /// 读内置动画包（.bytes）：帧里存的是 PNG，运行时用 Texture2D.LoadImage 解出像素，
        /// 这样完全绕开贴图导入设置（压缩/Mipmap 都不会把像素搞糊）。
        /// 格式：magic(4) ver(1) flags(1) count(2) w(2) h(2) | count*delayMs(2) | count*(len(4)+png)
        /// </summary>
        static PetFrame[] LoadBuiltinFrames()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BuiltinAssetPath);
            if (asset == null) return null;

            byte[] data = asset.bytes;
            if (data == null || data.Length < 12) return null;
            if (data[0] != (byte)'D' || data[1] != (byte)'G' || data[2] != (byte)'P' || data[3] != (byte)'F') return null;
            if (data[4] != BuiltinVersion) return null;

            int count = data[6] | (data[7] << 8);
            int width = data[8] | (data[9] << 8);
            int height = data[10] | (data[11] << 8);
            if (count <= 0 || width <= 0 || height <= 0) return null;

            int offset = 12;
            if (data.Length < offset + count * 2) return null;

            var delays = new float[count];
            for (int i = 0; i < count; i++)
            {
                int ms = data[offset + i * 2] | (data[offset + i * 2 + 1] << 8);
                delays[i] = Mathf.Max(0.02f, ms / 1000f);
            }
            offset += count * 2;

            var frames = new PetFrame[count];
            for (int i = 0; i < count; i++)
            {
                if (offset + 4 > data.Length) return null;
                int len = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
                offset += 4;
                if (len <= 0 || offset + len > data.Length) return null;

                var png = new byte[len];
                Buffer.BlockCopy(data, offset, png, 0, len);
                offset += len;

                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    if (!tex.LoadImage(png)) return null;
                    frames[i] = new PetFrame
                    {
                        Pixels = tex.GetPixels32(),
                        Width = tex.width,
                        Height = tex.height,
                        Delay = delays[i]
                    };
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }

            return frames;
        }

        /// <summary>读整张贴图像素；贴图不可读时用 Graphics.Blit 拷一份可读的。</summary>
        static Color32[] ReadPixels(Texture2D source)
        {
            if (source == null) return null;
            if (source.isReadable) return source.GetPixels32();

            RenderTexture rt = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                copy.Apply();
                return copy.GetPixels32();
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        /// <summary>按 Sprite 的像素矩形裁剪（坐标原点在左下，行序自下而上）。</summary>
        static void CropBottomUp(Color32[] all, int texW, int texH, Rect r,
            out Color32[] result, out int w, out int h)
        {
            result = null;
            w = 0;
            h = 0;
            if (all == null || texW <= 0 || texH <= 0) return;

            int x0 = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, texW - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, texH - 1);
            int cw = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, texW - x0);
            int ch = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, texH - y0);

            var crop = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
            {
                int srcRow = (y0 + y) * texW;
                int dstRow = y * cw;
                for (int x = 0; x < cw; x++)
                {
                    crop[dstRow + x] = all[srcRow + x0 + x];
                }
            }

            result = crop;
            w = cw;
            h = ch;
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
