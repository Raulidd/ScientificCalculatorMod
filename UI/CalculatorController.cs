using System.Collections.Generic;
using ScientificCalculatorMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScientificCalculatorMod.UI
{
    /// <summary>
    /// Builds and drives the in-game calculator tool. Press F8 to toggle it.
    /// The window stays fixed in a screen corner (not draggable) so it never
    /// gets in the way of gameplay, with two modes: Scientific (live evaluation)
    /// and Graph (plotting).
    /// </summary>
    public class CalculatorController : MonoBehaviour
    {
        // Palette — a dark graphite body with a light display, loosely evoking the
        // look of a physical graphing-calculator device (no branding, no logos).
        private static readonly Color PanelBg = new Color(0.13f, 0.13f, 0.14f, 0.98f);
        private static readonly Color TitleBg = new Color(0.09f, 0.09f, 0.10f, 1f);
        private static readonly Color Accent = new Color(0.30f, 0.55f, 0.90f);
        private static readonly Color ScreenBg = new Color(0.92f, 0.93f, 0.91f);
        private static readonly Color ScreenFg = new Color(0.10f, 0.11f, 0.10f);
        private static readonly Color ScreenFgDim = new Color(0.40f, 0.42f, 0.40f); // dim text on the light screen
        private static readonly Color PanelFgDim = new Color(0.60f, 0.62f, 0.66f);  // dim text on the dark body
        private static readonly Color BtnNum = new Color(0.94f, 0.94f, 0.95f);
        private static readonly Color BtnNumText = new Color(0.12f, 0.12f, 0.13f);
        private static readonly Color BtnOp = new Color(0.30f, 0.55f, 0.90f);
        private static readonly Color BtnFn = new Color(0.24f, 0.25f, 0.28f);
        private static readonly Color BtnShift = new Color(0.65f, 0.22f, 0.50f);
        private static readonly Color BtnClear = new Color(0.55f, 0.18f, 0.18f);
        private static readonly Color BtnExe = new Color(0.20f, 0.55f, 0.85f);
        private static readonly Color TextLight = Color.white;

        private Canvas _canvas;
        private RectTransform _window;
        private CalcContext _ctx = new CalcContext();

        private TMP_InputField _exprField;
        private TextMeshProUGUI _previewText;
        private TextMeshProUGUI _historyText;
        private TextMeshProUGUI _degRadLabel;
        private TextMeshProUGUI _shiftLabel;

        private RectTransform _sciPanel;
        private RectTransform _graphPanel;
        private GraphView _graphView;
        private TextMeshProUGUI _graphHintLabel;
        private const string GraphHintDefault = "Tap the graph for coordinates · drag to pan · scroll to zoom";

        // --- Graph options panel (resolution / functions / etc.) ---
        private RectTransform _optionsPanel;
        private RectTransform _functionsContent;
        private Button[] _qualityBtns = new Button[3];   // Baja / Media / Alta -> SuperSampleFactor 1/2/3
        private Button[] _thicknessBtns = new Button[3]; // Fina / Normal / Gruesa -> LineThicknessMultiplier
        private Button _gridToggleBtn;
        private static readonly int[] QualityLevels = { 1, 2, 3 };
        private static readonly float[] ThicknessLevels = { 0.6f, 1f, 1.5f };
        private static readonly Color SegSelected = new Color(0.30f, 0.55f, 0.90f);
        private static readonly Color SegUnselected = new Color(0.22f, 0.23f, 0.26f);

        private bool _shiftActive;
        private readonly List<string> _history = new List<string>();

        private void Start()
        {
            BuildUI();
            SetVisible(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                SetVisible(!_window.gameObject.activeSelf);
            }

            if (_window.gameObject.activeSelf && _exprField != null)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    if (_exprField.isFocused) Commit();
                }
            }
        }

        private void SetVisible(bool visible)
        {
            _window.gameObject.SetActive(visible);
            if (visible && _exprField != null)
            {
                _exprField.ActivateInputField();
            }
        }

        // ------------------------------------------------------------------
        // UI construction
        // ------------------------------------------------------------------
        private void BuildUI()
        {
            UIFactory.EnsureEventSystem();

            GameObject canvasGo = new GameObject("ScientificCalculatorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasGo);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30000;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Fixed position in the top-right corner, inset from the edge so it
            // never overlaps the HUD in the center/bottom of the screen.
            _window = UIFactory.CreatePanel(canvasGo.transform, "Window", PanelBg, 18);
            _window.sizeDelta = new Vector2(600, 560);
            _window.pivot = new Vector2(1f, 1f);
            _window.anchorMin = _window.anchorMax = new Vector2(1f, 1f);
            _window.anchoredPosition = new Vector2(-18, -18);
            AddOutline(_window);

            BuildTitleBar();
            BuildTabs();
            BuildScientificPanel();
            BuildGraphPanel();

            _graphPanel.gameObject.SetActive(false);
        }

        private void AddOutline(RectTransform rt)
        {
            Shadow sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.5f);
            sh.effectDistance = new Vector2(0, -4);
        }

        private void BuildTitleBar()
        {
            RectTransform bar = UIFactory.CreatePanel(_window, "TitleBar", TitleBg, 14);
            UIFactory.SetRect(bar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -40), new Vector2(0, 0));

            RectTransform dot = UIFactory.CreatePanel(bar, "AccentDot", Accent, 6);
            dot.sizeDelta = new Vector2(8, 8);
            dot.anchorMin = dot.anchorMax = new Vector2(0, 0.5f);
            dot.anchoredPosition = new Vector2(14, 0);

            TextMeshProUGUI title = UIFactory.CreateText(bar, "Title", "Scientific Calculator", 16, TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold);
            UIFactory.SetRect(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(28, 0), new Vector2(-70, 0));

            TextMeshProUGUI hint = UIFactory.CreateText(bar, "Hint", "F8", 12, TextAlignmentOptions.MidlineRight, PanelFgDim);
            UIFactory.SetRect(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(-40, 0));

            Button close = UIFactory.CreateButton(bar, "Close", "x", 16, new Color(0.55f, 0.15f, 0.15f), Color.white, () => SetVisible(false), 8);
            RectTransform closeRt = close.GetComponent<RectTransform>();
            UIFactory.SetRect(closeRt, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);
            closeRt.sizeDelta = new Vector2(28, 26);
            closeRt.anchoredPosition = new Vector2(-16, 0);
        }

        private Button _tabSciBtn, _tabGraphBtn;

        private void BuildTabs()
        {
            RectTransform tabBar = UIFactory.CreatePanel(_window, "Tabs", new Color(0, 0, 0, 0));
            UIFactory.SetRect(tabBar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -70), new Vector2(-10, -44));

            _tabSciBtn = UIFactory.CreateButton(tabBar, "TabSci", "SCIENTIFIC", 13, BtnFn, Color.white, () => ShowTab(true), 8);
            RectTransform r1 = _tabSciBtn.GetComponent<RectTransform>();
            UIFactory.SetRect(r1, new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(0, 2), new Vector2(-3, 0));

            _tabGraphBtn = UIFactory.CreateButton(tabBar, "TabGraph", "GRAPH", 13, new Color(0.18f, 0.19f, 0.22f), Color.white, () => ShowTab(false), 8);
            RectTransform r2 = _tabGraphBtn.GetComponent<RectTransform>();
            UIFactory.SetRect(r2, new Vector2(0.5f, 0), new Vector2(1, 1), new Vector2(3, 2), new Vector2(0, 0));
        }

        private void ShowTab(bool sci)
        {
            _sciPanel.gameObject.SetActive(sci);
            _graphPanel.gameObject.SetActive(!sci);
            SetBtnColor(_tabSciBtn, sci ? BtnFn : new Color(0.18f, 0.19f, 0.22f));
            SetBtnColor(_tabGraphBtn, !sci ? BtnFn : new Color(0.18f, 0.19f, 0.22f));
        }

        private void SetBtnColor(Button b, Color c)
        {
            b.GetComponent<Image>().color = c;
            ColorBlock cb = b.colors;
            cb.normalColor = c;
            cb.highlightedColor = Color.Lerp(c, Color.white, 0.25f);
            cb.pressedColor = Color.Lerp(c, Color.black, 0.2f);
            cb.selectedColor = c;
            b.colors = cb;
        }

        // ------------------------------------------------------------------
        // Scientific panel
        // ------------------------------------------------------------------
        private void BuildScientificPanel()
        {
            _sciPanel = UIFactory.CreatePanel(_window, "SciPanel", new Color(0, 0, 0, 0));
            UIFactory.SetRect(_sciPanel, Vector2.zero, new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, -70));

            // --- Display ---
            RectTransform screen = UIFactory.CreatePanel(_sciPanel, "Screen", ScreenBg, 12);
            UIFactory.SetRect(screen, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -166), new Vector2(-10, -10));

            _historyText = UIFactory.CreateText(screen, "History", "", 12, TextAlignmentOptions.TopLeft, new Color(ScreenFgDim.r, ScreenFgDim.g, ScreenFgDim.b, 0.8f));
            UIFactory.SetRect(_historyText.rectTransform, new Vector2(0, 0.55f), new Vector2(1, 1), new Vector2(10, 0), new Vector2(-10, -6));

            _exprField = UIFactory.CreateInputField(screen, "ExprField", 20, new Color(0, 0, 0, 0), ScreenFg, 0);
            RectTransform exprRt = _exprField.GetComponent<RectTransform>();
            UIFactory.SetRect(exprRt, new Vector2(0, 0.25f), new Vector2(1, 0.58f), new Vector2(8, 0), new Vector2(-8, 0));
            _exprField.onValueChanged.AddListener(OnExpressionChanged);
            _exprField.text = "";

            _previewText = UIFactory.CreateText(screen, "Preview", "= 0", 20, TextAlignmentOptions.MidlineRight, Accent, FontStyles.Bold);
            UIFactory.SetRect(_previewText.rectTransform, new Vector2(0, 0.02f), new Vector2(1, 0.26f), new Vector2(8, 0), new Vector2(-8, 0));

            // --- Status row: Deg/Rad, Shift, Memory ---
            RectTransform statusRow = UIFactory.CreatePanel(_sciPanel, "StatusRow", new Color(0, 0, 0, 0));
            UIFactory.SetRect(statusRow, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -192), new Vector2(-10, -170));
            HorizontalLayoutGroup hl = statusRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;

            _degRadLabel = MakeStatusButton(statusRow, "Deg", () =>
            {
                _ctx.DegreeMode = !_ctx.DegreeMode;
                _degRadLabel.text = _ctx.DegreeMode ? "Deg" : "Rad";
                Recalculate();
            }).GetComponentInChildren<TextMeshProUGUI>();

            _shiftLabel = MakeStatusButton(statusRow, "Shift", () =>
            {
                _shiftActive = !_shiftActive;
                _shiftLabel.text = _shiftActive ? "[Shift]" : "Shift";
            }).GetComponentInChildren<TextMeshProUGUI>();

            MakeStatusButton(statusRow, "MC", () => { _ctx.Memory = 0; });
            MakeStatusButton(statusRow, "M+", () => { TryCommitMemory(1); });
            MakeStatusButton(statusRow, "MR", () => { Insert("M"); });

            // --- Keypad ---
            RectTransform pad = UIFactory.CreatePanel(_sciPanel, "Keypad", new Color(0, 0, 0, 0));
            UIFactory.SetRect(pad, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 10), new Vector2(-10, -198));
            GridLayoutGroup grid = pad.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(76, 42);
            grid.spacing = new Vector2(5, 5);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 7;

            AddKey(pad, "sin", BtnFn, () => InsertFunc(_shiftActive ? "asin(" : "sin("));
            AddKey(pad, "cos", BtnFn, () => InsertFunc(_shiftActive ? "acos(" : "cos("));
            AddKey(pad, "tan", BtnFn, () => InsertFunc(_shiftActive ? "atan(" : "tan("));
            AddKey(pad, "ln", BtnFn, () => InsertFunc(_shiftActive ? "exp(" : "ln("));
            AddKey(pad, "log", BtnFn, () => InsertFunc("log("));
            AddKey(pad, "(", BtnFn, () => Insert("("));
            AddKey(pad, ")", BtnFn, () => Insert(")"));

            AddKey(pad, "x²", BtnFn, () => Insert("^2"));
            AddKey(pad, "x^y", BtnFn, () => Insert("^"));
            AddKey(pad, "√", BtnFn, () => InsertFunc("sqrt("));
            AddKey(pad, "x!", BtnFn, () => Insert("!"));
            AddKey(pad, "π", BtnFn, () => Insert("pi"));
            AddKey(pad, "e", BtnFn, () => Insert("e"));
            AddKey(pad, "Ans", BtnFn, () => Insert("Ans"));

            AddKey(pad, "7", BtnNum, () => Insert("7"));
            AddKey(pad, "8", BtnNum, () => Insert("8"));
            AddKey(pad, "9", BtnNum, () => Insert("9"));
            AddKey(pad, "DEL", BtnClear, DeleteLast);
            AddKey(pad, "AC", BtnClear, ClearAll);
            AddKey(pad, "%", BtnOp, () => Insert("%"));
            AddKey(pad, "nCr", BtnFn, () => InsertFunc("ncr("));

            AddKey(pad, "4", BtnNum, () => Insert("4"));
            AddKey(pad, "5", BtnNum, () => Insert("5"));
            AddKey(pad, "6", BtnNum, () => Insert("6"));
            AddKey(pad, "×", BtnOp, () => Insert("*"));
            AddKey(pad, "÷", BtnOp, () => Insert("/"));
            AddKey(pad, "abs", BtnFn, () => InsertFunc("abs("));
            AddKey(pad, "nPr", BtnFn, () => InsertFunc("npr("));

            AddKey(pad, "1", BtnNum, () => Insert("1"));
            AddKey(pad, "2", BtnNum, () => Insert("2"));
            AddKey(pad, "3", BtnNum, () => Insert("3"));
            AddKey(pad, "+", BtnOp, () => Insert("+"));
            AddKey(pad, "-", BtnOp, () => Insert("-"));
            AddKey(pad, "min", BtnFn, () => InsertFunc("min("));
            AddKey(pad, "max", BtnFn, () => InsertFunc("max("));

            AddKey(pad, "0", BtnNum, () => Insert("0"));
            AddKey(pad, ".", BtnNum, () => Insert("."));
            AddKey(pad, ",", BtnNum, () => Insert(","));
            AddKey(pad, "(-)", BtnOp, () => Insert("-"));
            AddKey(pad, "EXE", BtnExe, Commit);
            AddKey(pad, "M-", BtnFn, () => TryCommitMemory(-1));
            AddKey(pad, "→Graph", BtnShift, SendCurrentToGraph);
        }

        private Button MakeStatusButton(Transform parent, string label, System.Action onClick)
        {
            return UIFactory.CreateButton(parent, "Status_" + label, label, 12, new Color(0.18f, 0.19f, 0.23f), Color.white, onClick, 6);
        }

        private void AddKey(Transform parent, string label, Color color, System.Action onClick)
        {
            Color fg = color == BtnNum ? BtnNumText : TextLight;
            UIFactory.CreateButton(parent, "Key_" + label, label, 14, color, fg, onClick, 8);
        }

        // ------------------------------------------------------------------
        // Input / live evaluation
        // ------------------------------------------------------------------
        private void Insert(string s)
        {
            if (_exprField == null) return;
            int pos = _exprField.caretPosition;
            string t = _exprField.text;
            t = t.Insert(Mathf.Clamp(pos, 0, t.Length), s);
            _exprField.text = t;
            _exprField.caretPosition = pos + s.Length;
            _exprField.ActivateInputField();
        }

        private void InsertFunc(string funcOpenParen) => Insert(funcOpenParen);

        private void DeleteLast()
        {
            if (_exprField == null) return;
            string t = _exprField.text;
            int pos = _exprField.caretPosition;
            if (pos > 0 && t.Length > 0)
            {
                _exprField.text = t.Remove(pos - 1, 1);
                _exprField.caretPosition = pos - 1;
            }
            _exprField.ActivateInputField();
        }

        private void ClearAll()
        {
            _exprField.text = "";
            _previewText.text = "= 0";
        }

        private void OnExpressionChanged(string value) => Recalculate();

        private void Recalculate()
        {
            if (string.IsNullOrEmpty(_exprField.text)) { _previewText.text = "= 0"; _previewText.color = Accent; return; }
            try
            {
                double result = MathEvaluator.Evaluate(_exprField.text, _ctx);
                _previewText.text = "= " + FormatNumber(result);
                _previewText.color = Accent;
            }
            catch
            {
                _previewText.text = "Math error";
                _previewText.color = new Color(0.85f, 0.30f, 0.30f);
            }
        }

        private void Commit()
        {
            if (string.IsNullOrEmpty(_exprField.text)) return;
            try
            {
                double result = MathEvaluator.Evaluate(_exprField.text, _ctx);
                _history.Add(_exprField.text + " = " + FormatNumber(result));
                if (_history.Count > 5) _history.RemoveAt(0);
                _historyText.text = string.Join("\n", _history.ToArray());
                _ctx.Ans = result;
                _exprField.text = FormatNumber(result);
                _previewText.text = "= " + FormatNumber(result);
                _exprField.caretPosition = _exprField.text.Length;
            }
            catch
            {
                _previewText.text = "Math error";
                _previewText.color = new Color(0.85f, 0.30f, 0.30f);
            }
            _exprField.ActivateInputField();
        }

        private void TryCommitMemory(int sign)
        {
            try
            {
                double v = string.IsNullOrEmpty(_exprField.text) ? _ctx.Ans : MathEvaluator.Evaluate(_exprField.text, _ctx);
                _ctx.Memory += sign * v;
            }
            catch { /* ignore */ }
        }

        private static string FormatNumber(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "Math error";
            if (System.Math.Abs(v) > 0 && (System.Math.Abs(v) < 1e-9 || System.Math.Abs(v) >= 1e10))
                return v.ToString("0.######E+00", System.Globalization.CultureInfo.InvariantCulture);
            double rounded = System.Math.Round(v, 10);
            return rounded.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Graph panel
        // ------------------------------------------------------------------
        private void BuildGraphPanel()
        {
            _graphPanel = UIFactory.CreatePanel(_window, "GraphPanel", new Color(0, 0, 0, 0));
            UIFactory.SetRect(_graphPanel, Vector2.zero, new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, -70));

            // Slim top bar: just an Options button now — the function list
            // moved into the options panel so it can hold any number of them.
            RectTransform topBar = UIFactory.CreatePanel(_graphPanel, "GraphTopBar", new Color(0, 0, 0, 0));
            UIFactory.SetRect(topBar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -40), new Vector2(-10, -6));

            Button optionsBtn = UIFactory.CreateIconButton(topBar, "OptionsBtn", UIFactory.GetGearIconSprite(), "Options", 13, BtnFn, Color.white, ToggleOptionsPanel, 8);
            RectTransform optBtnRt = optionsBtn.GetComponent<RectTransform>();
            UIFactory.SetRect(optBtnRt, new Vector2(0, 0), new Vector2(0.42f, 1), Vector2.zero, Vector2.zero);

            RectTransform graphArea = UIFactory.CreatePanel(_graphPanel, "GraphArea", ScreenBg, 12);
            UIFactory.SetRect(graphArea, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 42), new Vector2(-10, -46));

            GameObject rawGo = new GameObject("GraphImage", typeof(RectTransform), typeof(RawImage));
            rawGo.transform.SetParent(graphArea, false);
            RawImage raw = rawGo.GetComponent<RawImage>();
            UIFactory.SetRect(rawGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));

            _graphView = rawGo.AddComponent<GraphView>();
            _graphView.Target = raw;
            _graphView.Ctx = _ctx;

            TextMeshProUGUI trace = UIFactory.CreateText(graphArea, "Trace", "Tap the graph for coordinates · drag to pan · scroll to zoom", 11, TextAlignmentOptions.TopLeft, new Color(ScreenFg.r, ScreenFg.g, ScreenFg.b, 0.8f));
            UIFactory.SetRect(trace.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -16), new Vector2(-10, 0));
            _graphView.TraceLabel = trace;
            _graphHintLabel = trace;

            // Default starting functions: Y1/Y2/Y3, empty — same as the
            // original fixed layout, but now removable/extendable.
            _graphView.AddFunction();
            _graphView.AddFunction();
            _graphView.AddFunction();

            RedrawGraph();

            RectTransform bottomRow = UIFactory.CreatePanel(_graphPanel, "GraphButtons", new Color(0, 0, 0, 0));
            UIFactory.SetRect(bottomRow, new Vector2(0, 0), new Vector2(1, 0), new Vector2(10, 4), new Vector2(-10, 38));
            HorizontalLayoutGroup hl = bottomRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;

            UIFactory.CreateButton(bottomRow, "ZoomIn", "Zoom +", 13, BtnFn, Color.white, () => _graphView.Zoom(0.8), 8);
            UIFactory.CreateButton(bottomRow, "ZoomOut", "Zoom -", 13, BtnFn, Color.white, () => _graphView.Zoom(1.25), 8);
            UIFactory.CreateButton(bottomRow, "Reset", "Reset view", 13, BtnFn, Color.white, () => _graphView.ResetView(), 8);

            BuildOptionsPanel();
        }

        private void SendCurrentToGraph()
        {
            if (string.IsNullOrEmpty(_exprField.text)) return;
            if (_graphView.FunctionCount == 0) _graphView.AddFunction();
            _graphView.SetFunction(0, _exprField.text);
            if (_optionsPanel != null && _optionsPanel.gameObject.activeSelf) RebuildFunctionRows();
            RedrawGraph();
            ShowTab(false);
        }

        // ------------------------------------------------------------------
        // Graph options panel: resolution/quality, line thickness, grid
        // toggle, and the (now unlimited) function list.
        // ------------------------------------------------------------------
        private void BuildOptionsPanel()
        {
            // Backdrop — dims the graph panel behind it and eats clicks so
            // taps don't fall through onto the graph/zoom buttons.
            _optionsPanel = UIFactory.CreatePanel(_graphPanel, "OptionsBackdrop", new Color(0, 0, 0, 0.55f));
            UIFactory.SetRect(_optionsPanel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _optionsPanel.GetComponent<Image>().raycastTarget = true;

            RectTransform card = UIFactory.CreatePanel(_optionsPanel, "OptionsCard", new Color(0.16f, 0.17f, 0.19f, 0.99f), 14);
            UIFactory.SetRect(card, new Vector2(0, 0), new Vector2(1, 1), new Vector2(6, 6), new Vector2(-6, -6));
            AddOutline(card);

            TextMeshProUGUI title = UIFactory.CreateText(card, "Title", "Graph Options", 15, TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold);
            UIFactory.SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -30), new Vector2(-46, -6));

            Button closeBtn = UIFactory.CreateButton(card, "CloseOptions", "x", 15, new Color(0.55f, 0.15f, 0.15f), Color.white, ToggleOptionsPanel, 8);
            RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
            closeRt.anchorMin = closeRt.anchorMax = new Vector2(1, 1);
            closeRt.pivot = new Vector2(1, 1);
            closeRt.sizeDelta = new Vector2(26, 24);
            closeRt.anchoredPosition = new Vector2(-8, -6);

            TextMeshProUGUI qualLbl = UIFactory.CreateText(card, "QualLbl", "graph resolution", 12, TextAlignmentOptions.MidlineLeft, PanelFgDim);
            UIFactory.SetRect(qualLbl.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -50), new Vector2(-12, -34));

            RectTransform qualRow = UIFactory.CreatePanel(card, "QualRow", new Color(0, 0, 0, 0));
            UIFactory.SetRect(qualRow, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -80), new Vector2(-12, -52));
            HorizontalLayoutGroup qualHl = qualRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            qualHl.spacing = 6; qualHl.childForceExpandWidth = true; qualHl.childForceExpandHeight = true;

            string[] qualLabels = { "Low", "Medium", "High" };
            for (int i = 0; i < 3; i++)
            {
                int lvl = i;
                _qualityBtns[i] = UIFactory.CreateButton(qualRow, "Qual" + i, qualLabels[i], 12, SegUnselected, Color.white, () => SetQuality(lvl), 6);
            }
            RefreshQualityButtons();

            // --- Line Thickness ---
            TextMeshProUGUI thickLbl = UIFactory.CreateText(card, "ThickLbl", "Line Thickness", 12, TextAlignmentOptions.MidlineLeft, PanelFgDim);
            UIFactory.SetRect(thickLbl.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -96), new Vector2(-12, -80));

            RectTransform thickRow = UIFactory.CreatePanel(card, "ThickRow", new Color(0, 0, 0, 0));
            UIFactory.SetRect(thickRow, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -126), new Vector2(-12, -98));
            HorizontalLayoutGroup thickHl = thickRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            thickHl.spacing = 6; thickHl.childForceExpandWidth = true; thickHl.childForceExpandHeight = true;

            string[] thickLabels = { "Fine", "Normal", "Thick" };
            for (int i = 0; i < 3; i++)
            {
                int lvl = i;
                _thicknessBtns[i] = UIFactory.CreateButton(thickRow, "Thick" + i, thickLabels[i], 12, SegUnselected, Color.white, () => SetThickness(lvl), 6);
            }
            RefreshThicknessButtons();

            _gridToggleBtn = UIFactory.CreateButton(card, "GridToggle", "grid: ON", 12, SegSelected, Color.white, ToggleGrid, 6);
            RectTransform gridRt = _gridToggleBtn.GetComponent<RectTransform>();
            UIFactory.SetRect(gridRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -156), new Vector2(-12, -132));

            TextMeshProUGUI funcLbl = UIFactory.CreateText(card, "FuncLbl", "Functions", 12, TextAlignmentOptions.MidlineLeft, PanelFgDim);
            UIFactory.SetRect(funcLbl.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -172), new Vector2(-12, -156));

            ScrollRect funcScroll = UIFactory.CreateScrollView(card, "FuncScroll", out _functionsContent);
            RectTransform scrollRt = funcScroll.GetComponent<RectTransform>();
            UIFactory.SetRect(scrollRt, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 42), new Vector2(-10, -176));

            Button addBtn = UIFactory.CreateButton(card, "AddFunc", "+ Add Function", 13, BtnShift, Color.white, () =>
            {
                _graphView.AddFunction();
                RebuildFunctionRows();
                RedrawGraph();
            }, 8);
            RectTransform addRt = addBtn.GetComponent<RectTransform>();
            UIFactory.SetRect(addRt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 8), new Vector2(-12, 36));

            RebuildFunctionRows();
            _optionsPanel.gameObject.SetActive(false);
        }

        private void ToggleOptionsPanel()
        {
            bool show = !_optionsPanel.gameObject.activeSelf;
            if (show) RebuildFunctionRows();
            _optionsPanel.gameObject.SetActive(show);
        }

        private void SetQuality(int levelIndex)
        {
            _graphView.SuperSampleFactor = QualityLevels[levelIndex];
            RefreshQualityButtons();
            RedrawGraph();
        }

        private void RefreshQualityButtons()
        {
            for (int i = 0; i < _qualityBtns.Length; i++)
                SetBtnColor(_qualityBtns[i], _graphView.SuperSampleFactor == QualityLevels[i] ? SegSelected : SegUnselected);
        }

        private void SetThickness(int levelIndex)
        {
            _graphView.LineThicknessMultiplier = ThicknessLevels[levelIndex];
            RefreshThicknessButtons();
            RedrawGraph();
        }

        private void RefreshThicknessButtons()
        {
            for (int i = 0; i < _thicknessBtns.Length; i++)
                SetBtnColor(_thicknessBtns[i], Mathf.Approximately(_graphView.LineThicknessMultiplier, ThicknessLevels[i]) ? SegSelected : SegUnselected);
        }

        private void ToggleGrid()
        {
            _graphView.ShowGrid = !_graphView.ShowGrid;
            _gridToggleBtn.GetComponentInChildren<TextMeshProUGUI>().text = "Grid: " + (_graphView.ShowGrid ? "ON" : "OFF");
            SetBtnColor(_gridToggleBtn, _graphView.ShowGrid ? SegSelected : SegUnselected);
            RedrawGraph();
        }

        /// <summary>Clears and rebuilds the scrollable function-row list from
        /// GraphView's current function list. Called whenever the count changes
        /// (add/remove) so row indices stay in sync.</summary>
        private void RebuildFunctionRows()
        {
            if (_functionsContent == null) return;
            for (int i = _functionsContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_functionsContent.GetChild(i).gameObject);

            for (int i = 0; i < _graphView.FunctionCount; i++)
            {
                int idx = i;
                RectTransform row = UIFactory.CreatePanel(_functionsContent, "FuncRow" + idx, new Color(0, 0, 0, 0));
                LayoutElement le = row.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 34;
                le.minHeight = 34;

                RectTransform swatch = UIFactory.CreatePanel(row, "Swatch", GraphView.ColorForSlot(idx), 4);
                swatch.anchorMin = new Vector2(0, 0.5f); swatch.anchorMax = new Vector2(0, 0.5f);
                swatch.sizeDelta = new Vector2(10, 10);
                swatch.anchoredPosition = new Vector2(8, 0);

                TextMeshProUGUI lbl = UIFactory.CreateText(row, "Lbl", "Y" + (idx + 1) + " =", 13, TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold);
                UIFactory.SetRect(lbl.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(22, 0), new Vector2(60, 0));

                TMP_InputField field = UIFactory.CreateInputField(row, "Field", 13, new Color(0.11f, 0.12f, 0.14f), Color.white, 6, "f(x)");
                RectTransform fieldRt = field.GetComponent<RectTransform>();
                UIFactory.SetRect(fieldRt, new Vector2(0, 0), new Vector2(1, 1), new Vector2(60, 2), new Vector2(-34, -2));
                field.text = _graphView.GetFunction(idx) ?? "";
                field.onValueChanged.AddListener(v => { _graphView.SetFunction(idx, v); RedrawGraph(); });

                Button removeBtn = UIFactory.CreateButton(row, "Remove", "x", 13, BtnClear, Color.white, () =>
                {
                    _graphView.RemoveFunction(idx);
                    RebuildFunctionRows();
                    RedrawGraph();
                }, 6);
                RectTransform removeRt = removeBtn.GetComponent<RectTransform>();
                removeRt.anchorMin = new Vector2(1, 0.5f); removeRt.anchorMax = new Vector2(1, 0.5f);
                removeRt.sizeDelta = new Vector2(26, 26);
                removeRt.anchoredPosition = new Vector2(-4, 0);
            }
        }

        /// <summary>Redraws the graph and surfaces any expression error where the
        /// "tap to trace" hint normally sits, instead of failing silently.</summary>
        private void RedrawGraph()
        {
            _graphView.Redraw();
            if (_graphHintLabel == null) return;
            if (!string.IsNullOrEmpty(_graphView.LastError))
            {
                _graphHintLabel.text = "Error in " + _graphView.LastError;
                _graphHintLabel.color = new Color(0.75f, 0.15f, 0.15f);
            }
            else
            {
                _graphHintLabel.text = GraphHintDefault;
                _graphHintLabel.color = new Color(ScreenFg.r, ScreenFg.g, ScreenFg.b, 0.8f);
            }
        }
    }
}
