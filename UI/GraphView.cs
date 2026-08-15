using System;
using ScientificCalculatorMod.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScientificCalculatorMod.UI
{
    internal class GraphView : MonoBehaviour, IDragHandler, IScrollHandler, IPointerClickHandler
    {
        public RawImage Target;
        public TMPro.TextMeshProUGUI TraceLabel;
        public CalcContext Ctx;

        public int SuperSampleFactor = 2;

        private const int BaseW = 480, BaseH = 360;
        private const int MaxDispW = 900, MaxDispH = 700;

        private Texture2D _tex;

        private int _dispW, _dispH;
        private int _renderW, _renderH;

        private Color[] _renderBuffer;
        private Color[] _dispBuffer;

        public double XMin = -10, XMax = 10, YMin = -6, YMax = 6;

        public bool ShowGrid = true;

        public float LineThicknessMultiplier = 1f;

        private readonly System.Collections.Generic.List<string> _funcs = new System.Collections.Generic.List<string>();

        private static readonly Color[] _colorPalette =
        {
            new Color(0.10f, 0.60f, 0.30f),
            new Color(0.80f, 0.42f, 0.02f),
            new Color(0.10f, 0.40f, 0.80f),
            new Color(0.75f, 0.15f, 0.55f),
            new Color(0.55f, 0.35f, 0.85f),
            new Color(0.85f, 0.65f, 0.05f),
            new Color(0.05f, 0.60f, 0.60f),
            new Color(0.70f, 0.20f, 0.20f),
        };

        public static Color ColorForSlot(int slot) => _colorPalette[slot % _colorPalette.Length];

        public int FunctionCount => _funcs.Count;

        public int AddFunction(string expr = null)
        {
            _funcs.Add(string.IsNullOrEmpty(expr) ? null : expr);
            return _funcs.Count - 1;
        }

        public void RemoveFunction(int index)
        {
            if (index < 0 || index >= _funcs.Count) return;
            _funcs.RemoveAt(index);
        }

        public string GetFunction(int index) => (index >= 0 && index < _funcs.Count) ? _funcs[index] : null;

        private void Awake()
        {

        }

        private void EnsureTextureBound()
        {
            if (Target != null && Target.texture != _tex) Target.texture = _tex;
        }

        private void EnsureResolution()
        {
            int newDispW = BaseW, newDispH = BaseH;

            if (Target != null)
            {
                RectTransform rt = Target.rectTransform;
                Rect r = rt.rect;
                float scale = 1f;
                Canvas canvas = Target.canvas;
                if (canvas != null) scale = canvas.scaleFactor;

                if (r.width > 1f && r.height > 1f)
                {
                    newDispW = Mathf.Clamp(Mathf.RoundToInt(r.width * scale), 64, MaxDispW);
                    newDispH = Mathf.Clamp(Mathf.RoundToInt(r.height * scale), 64, MaxDispH);
                }
            }

            int ss = Math.Max(1, SuperSampleFactor);
            int newRenderW = newDispW * ss;
            int newRenderH = newDispH * ss;

            if (_tex != null && newDispW == _dispW && newDispH == _dispH && newRenderW == _renderW)
                return;

            _dispW = newDispW; _dispH = newDispH;
            _renderW = newRenderW; _renderH = newRenderH;

            if (_tex == null)
            {
                _tex = new Texture2D(_dispW, _dispH, TextureFormat.RGBA32, false);
            }
            else
            {
                _tex.Reinitialize(_dispW, _dispH);
            }
            _tex.filterMode = FilterMode.Bilinear;
            _tex.wrapMode = TextureWrapMode.Clamp;

            _renderBuffer = new Color[_renderW * _renderH];
            _dispBuffer = new Color[_dispW * _dispH];

            if (Target != null) Target.texture = _tex;
        }

        public void SetFunction(int slot, string expr)
        {
            if (slot < 0 || slot >= _funcs.Count) return;
            _funcs[slot] = string.IsNullOrEmpty(expr) ? null : expr;
        }

        public void ResetView()
        {
            XMin = -10; XMax = 10; YMin = -6; YMax = 6;
            Redraw();
        }

        public string LastError { get; private set; }

        public void Redraw()
        {
            EnsureResolution();
            if (_tex == null) return;
            EnsureTextureBound();

            Color bg = new Color(0.92f, 0.93f, 0.91f);
            Color grid = new Color(0, 0, 0, 0.10f);
            Color axis = new Color(0, 0, 0, 0.45f);

            var buf = _renderBuffer;
            for (int i = 0; i < buf.Length; i++) buf[i] = bg;

            if (ShowGrid)
            {
                double stepX = NiceStep(XMax - XMin, 10);
                double stepY = NiceStep(YMax - YMin, 10);

                for (double gx = Math.Ceiling(XMin / stepX) * stepX; gx <= XMax; gx += stepX)
                    DrawVLine(buf, XToPixel(gx), grid);
                for (double gy = Math.Ceiling(YMin / stepY) * stepY; gy <= YMax; gy += stepY)
                    DrawHLine(buf, YToPixel(gy), grid);
            }

            if (XMin <= 0 && XMax >= 0) DrawVLine(buf, XToPixel(0), axis);
            if (YMin <= 0 && YMax >= 0) DrawHLine(buf, YToPixel(0), axis);

            DrawFunctions(buf);

            Downsample();
            _tex.SetPixels(_dispBuffer);
            _tex.Apply(false);
        }

        /// <summary>
        /// Box-filters the supersampled _renderBuffer (SuperSampleFactor^2
        /// samples per output pixel) down into _dispBuffer. This is the step
        /// that actually produces the anti-aliasing: hard 1-render-pixel edges
        /// (grid lines, curve edges) become smooth gradients once averaged.
        /// </summary>
        private void Downsample()
        {
            int ss = Math.Max(1, SuperSampleFactor);
            if (ss == 1)
            {
                Array.Copy(_renderBuffer, _dispBuffer, _dispBuffer.Length);
                return;
            }

            float inv = 1f / (ss * ss);
            for (int oy = 0; oy < _dispH; oy++)
            {
                int ry0 = oy * ss;
                int outRowBase = oy * _dispW;
                for (int ox = 0; ox < _dispW; ox++)
                {
                    int rx0 = ox * ss;
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        int rowBase = (ry0 + sy) * _renderW + rx0;
                        for (int sx = 0; sx < ss; sx++)
                        {
                            Color c = _renderBuffer[rowBase + sx];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    }
                    _dispBuffer[outRowBase + ox] = new Color(r * inv, g * inv, b * inv, a * inv);
                }
            }
        }

        /// <summary>
        /// Compiles and draws Y1/Y2/Y3 together. For each render-space pixel
        /// column, several sub-samples are evaluated (not just one) and the
        /// min/max range covered is drawn as a vertical segment — this keeps
        /// fast-oscillating functions (e.g. "x*sin(x*x)") looking like a proper
        /// waveform instead of aliasing into a jagged, unreadable line. Slots
        /// are evaluated in order at every sample so a later one can reference
        /// an earlier one (e.g. Y3 = y1 + y2). Any slot that fails to even
        /// compile is reported via LastError instead of silently leaving a
        /// blank graph.
        /// </summary>
        private const int SubSamples = 5;

        private void DrawFunctions(Color[] buf)
        {
            int count = _funcs.Count;
            var compiled = new Func<double, CalcContext, double>[count];
            string firstError = null;

            for (int slot = 0; slot < count; slot++)
            {
                if (string.IsNullOrEmpty(_funcs[slot])) continue;
                try { compiled[slot] = MathEvaluator.CompileForX(_funcs[slot]); }
                catch (Exception ex)
                {
                    compiled[slot] = null;
                    if (firstError == null) firstError = $"Y{slot + 1}: {ex.Message}";
                }
            }
            LastError = firstError;

            // Carries the last valid sample's pixel-y from one column into the
            // next, so consecutive columns still connect with a line even where
            // the function isn't oscillating fast enough to need the min/max band.
            var carryPy = new int?[count];
            var havePrevColumn = new bool[count];

            for (int px = 0; px < _renderW; px++)
            {
                double xLeft = XMin + (XMax - XMin) * (px / (double)(_renderW - 1));
                double xRight = XMin + (XMax - XMin) * ((px + 1) / (double)(_renderW - 1));

                for (int slot = 0; slot < count; slot++)
                {
                    if (compiled[slot] == null) continue;

                    int? colMin = null, colMax = null;
                    int? firstSamplePy = null, lastSamplePy = null;
                    bool anyValid = false;

                    for (int s = 0; s < SubSamples; s++)
                    {
                        double x = xLeft + (xRight - xLeft) * (s / (double)(SubSamples - 1));
                        Ctx.X = x;
                        bool ok = true;
                        double y = 0;
                        try
                        {
                            y = compiled[slot](x, Ctx);
                            if (double.IsNaN(y) || double.IsInfinity(y)) ok = false;
                        }
                        catch { ok = false; }

                        // Feed this slot's value forward at THIS sample so later
                        // slots (Y2, Y3...) can reference it via "y1"/"y2"/"y3".
                        if (slot == 0) Ctx.Y1 = ok ? y : 0;
                        else if (slot == 1) Ctx.Y2 = ok ? y : 0;
                        else if (slot == 2) Ctx.Y3 = ok ? y : 0;

                        if (!ok) continue;

                        int py = YToPixel(y);
                        if (!firstSamplePy.HasValue) firstSamplePy = py;
                        lastSamplePy = py;
                        colMin = colMin.HasValue ? Math.Min(colMin.Value, py) : py;
                        colMax = colMax.HasValue ? Math.Max(colMax.Value, py) : py;
                        anyValid = true;
                    }

                    if (!anyValid)
                    {
                        havePrevColumn[slot] = false;
                        carryPy[slot] = null;
                        continue;
                    }

                    // Connect to the previous column so the curve reads continuously.
                    if (havePrevColumn[slot] && carryPy[slot].HasValue &&
                        Math.Abs(firstSamplePy.Value - carryPy[slot].Value) < _renderH * 2)
                        DrawLine(buf, px - 1, carryPy[slot].Value, px, firstSamplePy.Value, ColorForSlot(slot));

                    // Fill the vertical range this column actually covers (this is
                    // what makes fast oscillation look like a solid waveform band
                    // instead of a random jagged line).
                    DrawVSegment(buf, px, colMin.Value, colMax.Value, ColorForSlot(slot));

                    carryPy[slot] = lastSamplePy;
                    havePrevColumn[slot] = true;
                }
            }
        }

        private int XToPixel(double x) => (int)Math.Round((x - XMin) / (XMax - XMin) * (_renderW - 1));
        private int YToPixel(double y) => (int)Math.Round((y - YMin) / (YMax - YMin) * (_renderH - 1));

        // Line/segment "radius" in render-space pixels, scaled with the
        // supersampling factor so the perceived on-screen thickness stays
        // consistent regardless of SuperSampleFactor.
        private int LineHalfThickness => Math.Max(1, Mathf.RoundToInt(Math.Max(1, SuperSampleFactor) * 0.9f * Math.Max(0.1f, LineThicknessMultiplier)));

        private void DrawVLine(Color[] px, int x, Color c)
        {
            if (x < 0 || x >= _renderW) return;
            for (int y = 0; y < _renderH; y++) px[y * _renderW + x] = Blend(px[y * _renderW + x], c);
        }

        /// <summary>Draws a vertical segment between two pixel rows (inclusive) in one column.</summary>
        private void DrawVSegment(Color[] px, int x, int y0, int y1, Color c)
        {
            if (x < 0 || x >= _renderW) return;
            int pad = LineHalfThickness;
            int lo = Math.Max(0, Math.Min(y0, y1) - pad);
            int hi = Math.Min(_renderH - 1, Math.Max(y0, y1) + pad);
            for (int y = lo; y <= hi; y++) px[y * _renderW + x] = Blend(px[y * _renderW + x], c);
        }

        private void DrawHLine(Color[] px, int y, Color c)
        {
            if (y < 0 || y >= _renderH) return;
            for (int x = 0; x < _renderW; x++) px[y * _renderW + x] = Blend(px[y * _renderW + x], c);
        }

        private void DrawLine(Color[] px, int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                PlotThick(px, x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void PlotThick(Color[] px, int x, int y, Color c)
        {
            int half = LineHalfThickness;
            for (int oy = -half; oy <= half; oy++)
                for (int ox = -half; ox <= half; ox++)
                {
                    int xx = x + ox, yy = y + oy;
                    if (xx < 0 || xx >= _renderW || yy < 0 || yy >= _renderH) continue;
                    px[yy * _renderW + xx] = c;
                }
        }

        private static Color Blend(Color under, Color over) => Color.Lerp(under, over, over.a);

        private static double NiceStep(double range, int targetDivisions)
        {
            double raw = range / targetDivisions;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double norm = raw / mag;
            double niceNorm = norm < 1.5 ? 1 : (norm < 3 ? 2 : (norm < 7 ? 5 : 10));
            return niceNorm * mag;
        }

        // ---- Interaction ----
        public void OnDrag(PointerEventData eventData)
        {
            if (Target == null) return;
            RectTransform rt = Target.rectTransform;
            float unitsPerPixelX = (float)(XMax - XMin) / rt.rect.width;
            float unitsPerPixelY = (float)(YMax - YMin) / rt.rect.height;
            double dx = -eventData.delta.x * unitsPerPixelX;
            double dy = -eventData.delta.y * unitsPerPixelY;
            XMin += dx; XMax += dx;
            YMin += dy; YMax += dy;
            Redraw();
        }

        public void OnScroll(PointerEventData eventData)
        {
            float scroll = eventData.scrollDelta.y;
            double zoom = scroll > 0 ? 0.85 : (scroll < 0 ? 1.176 : 1.0);
            Zoom(zoom);
        }

        public void Zoom(double factor)
        {
            double cx = (XMin + XMax) / 2, cy = (YMin + YMax) / 2;
            double halfW = (XMax - XMin) / 2 * factor;
            double halfH = (YMax - YMin) / 2 * factor;
            XMin = cx - halfW; XMax = cx + halfW;
            YMin = cy - halfH; YMax = cy + halfH;
            Redraw();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Target == null || TraceLabel == null) return;
            RectTransform rt = Target.rectTransform;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out local);
            Rect r = rt.rect;
            double tx = XMin + (XMax - XMin) * ((local.x - r.x) / r.width);
            double ty = YMin + (YMax - YMin) * ((local.y - r.y) / r.height);
            TraceLabel.text = $"x = {tx:0.####}   y = {ty:0.####}";
        }
    }
}
