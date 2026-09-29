using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.UI;

namespace Apocatremors
{
    // Top-left ambush message in the same style as the game's "new codex entry" notice: a clone of that Text object
    // (same font, size, outline, position), recoloured red. The source object is taken from the __GameManager__ [Codex]
    // FSM's ActivateGameObject action (UI names are reused across canvases, so never looked up by name).
    internal static class Notice
    {
        private static GameObject _src, _clone;
        private static Text _text;
        private static float _left;
        private static float _nextFind;

        public static void Reset() { _src = null; _clone = null; _text = null; _left = 0f; _nextFind = 0f; _placedLogged = false; }

        public static void Show(string message)
        {
            if (!Plugin.ShowNotification.Value || string.IsNullOrEmpty(message)) return;
            if (!Ensure()) { Plugin.Log.LogInfo("(notice) " + message); return; }

            _text.text = message;
            Color col;
            if (ColorUtility.TryParseHtmlString(Plugin.NotificationColor.Value, out col)) _text.color = col;

            Place();
            _clone.SetActive(true);
            _clone.transform.SetAsLastSibling();
            _left = Mathf.Max(0.5f, Plugin.NotificationSeconds.Value);
        }

        // The codex notice is a centre-aligned Text in a wide box; its visible text starts where the (short) text is drawn, not at
        // the box's left edge (which can be off screen). Anchor the clone to the canvas' top-left corner, start it at the codex text's
        // visible left edge (clamped inside the screen), and let long messages wrap within the rest of the screen width.
        private static void Place()
        {
            var srcRt = _src.GetComponent<RectTransform>();
            var rt = _clone.GetComponent<RectTransform>();
            var parent = rt != null ? rt.parent as RectTransform : null;
            if (srcRt == null || rt == null || parent == null) return;
            var srcText = _src.GetComponent<Text>();

            var c = new Vector3[4];
            srcRt.GetWorldCorners(c);                                  // 0 bottom-left, 1 top-left, 2 top-right
            Vector3 bl = parent.InverseTransformPoint(c[0]), tl = parent.InverseTransformPoint(c[1]), tr = parent.InverseTransformPoint(c[2]);
            float boxW = tr.x - tl.x, boxH = tl.y - bl.y;
            float textW = srcText != null ? srcText.preferredWidth * Mathf.Abs(srcRt.localScale.x) : 0f;
            if (textW <= 0f || textW > boxW) textW = boxW;
            float left = tl.x;
            if (srcText != null)
            {
                switch (srcText.alignment)
                {
                    case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter:
                        left = (tl.x + tr.x) * 0.5f - textW * 0.5f; break;
                    case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight:
                        left = tr.x - textW; break;
                }
            }

            Rect pr = parent.rect;
            const float margin = 12f;
            float x = Mathf.Max(margin, left - pr.xMin);                   // from the canvas' left edge
            float y = Mathf.Min(-margin, tl.y - pr.yMax);                  // from the canvas' top edge (negative = down)
            if (_src.activeInHierarchy) y -= boxH + 4f;                    // below the codex notice while it is showing
            float h = Mathf.Max(boxH, _text.fontSize * 1.5f);

            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.localRotation = srcRt.localRotation;
            rt.localScale = srcRt.localScale;
            float sx = Mathf.Max(0.01f, Mathf.Abs(srcRt.localScale.x));
            rt.sizeDelta = new Vector2(Mathf.Max(50f, (pr.width - x - margin) / sx), h / Mathf.Max(0.01f, Mathf.Abs(srcRt.localScale.y)));
            rt.anchoredPosition = new Vector2(x, y);

            if (!_placedLogged || Plugin.VerboseLog.Value)
            {
                _placedLogged = true;
                var d = new Vector3[4];
                rt.GetWorldCorners(d);
                var canvas = _clone.GetComponentInParent<Canvas>();
                Plugin.Log.LogInfo(string.Format("Notice placed: codex box x {0:0}..{1:0} top {2:0} (canvas {3:0}x{4:0}), codex text width {5:0}, " +
                    "notice at x {6:0} y {7:0}; screen corners ({8:0},{9:0})-({10:0},{11:0}) of {12}x{13}, canvas mode {14}",
                    tl.x - pr.xMin, tr.x - pr.xMin, pr.yMax - tl.y, pr.width, pr.height, textW, x, -y,
                    d[1].x, d[1].y, d[3].x, d[3].y, Screen.width, Screen.height, canvas != null ? canvas.renderMode.ToString() : "?"));
            }
        }

        private static bool _placedLogged;

        public static void Tick(float dt)
        {
            if (_clone == null || _left <= 0f) return;
            _left -= dt;
            if (_left <= 0f) _clone.SetActive(false);
        }

        private static bool Ensure()
        {
            if (_clone != null && _text != null) return true;
            if (Time.unscaledTime < _nextFind) return false;
            _nextFind = Time.unscaledTime + 5f;
            _src = FindCodexEntry();
            if (_src == null) { Plugin.Log.LogWarning("Codex notice object not found; notifications go to the log"); return false; }
            var srcText = _src.GetComponent<Text>();
            if (srcText == null) { Plugin.Log.LogWarning("Codex notice has no Text component"); _src = null; return false; }

            _clone = UnityEngine.Object.Instantiate(_src, _src.transform.parent, false);
            _clone.name = "Apocatremors_Notice";
            foreach (var f in _clone.GetComponentsInChildren<PlayMakerFSM>(true)) UnityEngine.Object.Destroy(f);
            foreach (var fit in _clone.GetComponents<ContentSizeFitter>()) UnityEngine.Object.Destroy(fit);
            _text = _clone.GetComponent<Text>();
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;              // wraps inside the rest of the screen width
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = false;
            // keep the left edge fixed so a longer sentence grows to the right, not off the screen
            switch (_text.alignment)
            {
                case TextAnchor.UpperCenter: case TextAnchor.UpperRight: _text.alignment = TextAnchor.UpperLeft; break;
                case TextAnchor.MiddleCenter: case TextAnchor.MiddleRight: _text.alignment = TextAnchor.MiddleLeft; break;
                case TextAnchor.LowerCenter: case TextAnchor.LowerRight: _text.alignment = TextAnchor.LowerLeft; break;
            }
            _clone.SetActive(false);
            Plugin.Log.LogInfo("Notice cloned from " + Path(_src.transform) + " (font " + (srcText.font != null ? srcText.font.name : "?") + " " + srcText.fontSize + ", align " + srcText.alignment + ")");
            return true;
        }

        private static GameObject FindCodexEntry()
        {
            var gm = GameObject.Find("__GameManager__");
            if (gm == null) return null;
            foreach (var f in gm.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName != "Codex" || !f.Fsm.Initialized) continue;
                foreach (var st in f.FsmStates)
                {
                    if (st == null || st.Name != "newEntry" || st.Actions == null) continue;
                    foreach (var a in st.Actions)
                    {
                        var act = a as ActivateGameObject;
                        if (act == null) continue;
                        try { var go = f.Fsm.GetOwnerDefaultTarget(act.gameObject); if (go != null) return go; }
                        catch (Exception) { }
                    }
                }
            }
            return null;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }
    }
}
