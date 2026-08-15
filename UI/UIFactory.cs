using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScientificCalculatorMod.UI
{
    /// <summary>Helper functions to build UI purely from code (no external assets/prefabs needed).</summary>
    internal static class UIFactory
    {
        private static readonly Dictionary<int, Sprite> _roundedSpriteCache = new Dictionary<int, Sprite>();

        private static TMP_FontAsset _uiFont;
        private static bool _uiFontLoadAttempted;

        /// <summary>
        /// Loads the "Microsoft Sans Serif" font directly from the player's OS at runtime
        /// (no font file is bundled with the mod — this just asks Windows for a font it
        /// already has installed) and wraps it as a TMP font asset. Falls back to the
        /// game's default TMP font if the OS font can't be found for any reason.
        /// </summary>
        public static TMP_FontAsset GetUiFont()
        {
            if (_uiFontLoadAttempted) return _uiFont;
            _uiFontLoadAttempted = true;
            try
            {
                Font osFont = Font.CreateDynamicFontFromOSFont("Microsoft Sans Serif", 64);
                if (osFont != null)
                    _uiFont = TMP_FontAsset.CreateFontAsset(osFont);
            }
            catch
            {
                _uiFont = null; // Falls back to TMP's default font asset.
            }
            return _uiFont;
        }

        /// <summary>Generates (and caches) a rounded-rectangle sprite that can be 9-sliced onto panels of any size.</summary>
        public static Sprite GetRoundedSprite(int radius)
        {
            if (_roundedSpriteCache.TryGetValue(radius, out Sprite cached)) return cached;

            int size = radius * 2 + 4;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Vector2 c1 = new Vector2(radius, radius);
            Vector2 c2 = new Vector2(size - radius, radius);
            Vector2 c3 = new Vector2(radius, size - radius);
            Vector2 c4 = new Vector2(size - radius, size - radius);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 1f;
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inCornerZoneX = x < radius || x > size - radius;
                    bool inCornerZoneY = y < radius || y > size - radius;
                    if (inCornerZoneX && inCornerZoneY)
                    {
                        Vector2 corner =
                            (x < radius && y < radius) ? c1 :
                            (x >= size - radius && y < radius) ? c2 :
                            (x < radius && y >= size - radius) ? c3 : c4;
                        float dist = Vector2.Distance(p, corner);
                        alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    }
                    tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            }
            tex.Apply(false);

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.name = "RoundedRect_" + radius;
            _roundedSpriteCache[radius] = sprite;
            return sprite;
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color, int cornerRadius = 0)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = color;
            if (cornerRadius > 0)
            {
                img.sprite = GetRoundedSprite(cornerRadius);
                img.type = Image.Type.Sliced;
            }
            img.raycastTarget = true;
            return go.GetComponent<RectTransform>();
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name, string text, float size, TextAlignmentOptions align, Color color, FontStyles style = FontStyles.Normal)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.enableWordWrapping = true;
            TMP_FontAsset uiFont = GetUiFont();
            if (uiFont != null) tmp.font = uiFont;
            RectTransform rt = go.GetComponent<RectTransform>();
            StretchFull(rt);
            return tmp;
        }

        public static Button CreateButton(Transform parent, string name, string label, float fontSize, Color bg, Color fg, Action onClick, int cornerRadius = 10)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = bg;
            if (cornerRadius > 0)
            {
                img.sprite = GetRoundedSprite(cornerRadius);
                img.type = Image.Type.Sliced;
            }

            Button btn = go.GetComponent<Button>();
            // Unity's ColorTint transition REPLACES the graphic's color with these
            // values (it does not multiply on top of img.color), so normalColor
            // must match the background we just assigned or the button would
            // flash to white as soon as it becomes interactable.
            ColorBlock cb = btn.colors;
            cb.normalColor = bg;
            cb.highlightedColor = Color.Lerp(bg, Color.white, 0.25f);
            cb.pressedColor = Color.Lerp(bg, Color.black, 0.2f);
            cb.selectedColor = bg;
            cb.disabledColor = bg;
            cb.fadeDuration = 0.06f;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            TextMeshProUGUI txt = CreateText(go.transform, "Label", label, fontSize, TextAlignmentOptions.Center, fg);
            txt.raycastTarget = false;
            return btn;
        }

        private static Sprite _gearIconSprite;

        /// <summary>
        /// Procedurally draws a small gear/cog icon into a Texture2D and wraps it
        /// in a Sprite, cached after the first call. This is drawn as pure geometry
        /// (radial "teeth" via a cosine wave, anti-aliased edges via smoothstep on
        /// distance-to-target-radius) rather than picked from a font glyph — a
        /// Unicode gear character (⚙, U+2699) depends on the game's TMP font atlas
        /// actually containing that glyph, and if it doesn't, TMP just renders the
        /// "missing glyph" tofu box, which is what happened before. A hand-drawn
        /// icon has no such dependency.
        /// </summary>
        public static Sprite GetGearIconSprite(int size = 64)
        {
            if (_gearIconSprite != null) return _gearIconSprite;

            const int teeth = 8;
            const float outerRadius = 0.92f;
            const float bodyRadius = 0.64f;
            const float holeRadius = 0.30f;
            const float toothSharpness = 4f;
            float edge = 1.4f / size; // ~1 texel of soft anti-aliasing

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float theta = Mathf.Atan2(ny, nx);

                    // Smooth radial "teeth": a cosine wave raised to a power gives
                    // rounded-but-toothed bumps around the circumference, without
                    // needing separate angular anti-aliasing logic.
                    float toothMask = Mathf.Pow(Mathf.Max(0f, 0.5f + 0.5f * Mathf.Cos(theta * teeth)), toothSharpness);
                    float targetR = bodyRadius + (outerRadius - bodyRadius) * toothMask;

                    float alphaOuter = 1f - SmoothStepEdge(targetR - edge, targetR + edge, r);
                    float alphaHole = SmoothStepEdge(holeRadius - edge, holeRadius + edge, r);
                    float alpha = Mathf.Clamp01(alphaOuter * alphaHole);

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false);

            _gearIconSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _gearIconSprite;
        }

        private static float SmoothStepEdge(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Same as CreateButton, but with a small icon sprite to the left of the label.</summary>
        public static Button CreateIconButton(Transform parent, string name, Sprite icon, string label, float fontSize, Color bg, Color fg, Action onClick, int cornerRadius = 10)
        {
            Button btn = CreateButton(parent, name, label, fontSize, bg, fg, onClick, cornerRadius);

            TextMeshProUGUI txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            RectTransform txtRt = txt.rectTransform;
            txtRt.offsetMin = new Vector2(24, txtRt.offsetMin.y);

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(btn.transform, false);
            iconGo.transform.SetAsFirstSibling();
            Image iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = icon;
            iconImg.color = fg;
            iconImg.raycastTarget = false;
            RectTransform iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0, 0.5f);
            iconRt.anchorMax = new Vector2(0, 0.5f);
            iconRt.sizeDelta = new Vector2(15, 15);
            iconRt.anchoredPosition = new Vector2(15, 0);

            return btn;
        }

        public static TMP_InputField CreateInputField(Transform parent, string name, float fontSize, Color bg, Color fg, int cornerRadius = 8, string placeholderText = "")
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image bgImg = go.GetComponent<Image>();
            bgImg.color = bg;
            if (cornerRadius > 0)
            {
                bgImg.sprite = GetRoundedSprite(cornerRadius);
                bgImg.type = Image.Type.Sliced;
            }

            GameObject textArea = new GameObject("TextArea", typeof(RectTransform));
            textArea.transform.SetParent(go.transform, false);
            RectTransform taRT = textArea.GetComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(10, 2); taRT.offsetMax = new Vector2(-10, -2);

            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(textArea.transform, false);
            TextMeshProUGUI text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = fg;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            TMP_FontAsset uiFont1 = GetUiFont();
            if (uiFont1 != null) text.font = uiFont1;
            StretchFull(textGo.GetComponent<RectTransform>());

            GameObject placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(textArea.transform, false);
            TextMeshProUGUI ph = placeholderGo.AddComponent<TextMeshProUGUI>();
            ph.text = placeholderText;
            ph.fontSize = fontSize;
            ph.color = new Color(fg.r, fg.g, fg.b, 0.35f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            if (uiFont1 != null) ph.font = uiFont1;
            StretchFull(placeholderGo.GetComponent<RectTransform>());

            TMP_InputField input = go.AddComponent<TMP_InputField>();
            input.textViewport = taRT;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 200;
            return input;
        }

        public static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        /// <summary>
        /// Creates a vertically-scrolling list container. Add children to the
        /// returned "content" RectTransform — each child should carry a
        /// LayoutElement with a preferredHeight, since content uses a
        /// VerticalLayoutGroup + ContentSizeFitter to grow with its children.
        /// </summary>
        public static ScrollRect CreateScrollView(Transform parent, string name, out RectTransform content)
        {
            GameObject scrollGo = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 22f;

            GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            StretchFull(viewportGo.GetComponent<RectTransform>());
            Image viewportImg = viewportGo.GetComponent<Image>();
            viewportImg.color = new Color(1, 1, 1, 0.001f); // near-invisible; RectMask2D needs a Graphic to mask against

            GameObject contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            RectTransform contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.spacing = 6;
            vlg.padding = new RectOffset(2, 2, 2, 2);

            ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = viewportGo.GetComponent<RectTransform>();
            scroll.content = contentRt;

            content = contentRt;
            return scroll;
        }

        public static EventSystem EnsureEventSystem()
        {
            EventSystem es = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (es == null)
            {
                GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                UnityEngine.Object.DontDestroyOnLoad(go);
                es = go.GetComponent<EventSystem>();
            }
            return es;
        }
    }
}
