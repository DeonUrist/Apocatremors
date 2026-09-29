using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.UI;

namespace Apocatremors
{
    // Top-left ambush message styled like the game's "new codex entry" notice: a recoloured clone of that Text.
    // The source is taken from the __GameManager__ [Codex] FSM (UI object names are reused across canvases).
    internal static class Notice
    {
        private static GameObject _src, _clone;
        private static Text _text;
        private static float _left, _nextFind;

        public static void Reset() { _src = null; _clone = null; _text = null; _left = 0f; _nextFind = 0f; }

        public static void Show(string message)
        {
            if (!Plugin.ShowNotification.Value || string.IsNullOrEmpty(message) || !Ensure()) return;
            _text.text = message;
            Color col;
            if (ColorUtility.TryParseHtmlString(Plugin.NotificationColor.Value, out col)) _text.color = col;
            Place();
            _clone.SetActive(true);
            _clone.transform.SetAsLastSibling();
            _left = Mathf.Max(0.5f, Plugin.NotificationSeconds.Value);
        }

        public static void Tick(float dt)
        {
            if (_clone == null || _left <= 0f) return;
            _left -= dt;
            if (_left <= 0f) _clone.SetActive(false);
        }

        // The codex text is centre-aligned in a wide box, so its box edge may be off screen. Anchor the clone to the canvas'
        // top-left corner at the codex text's visible left edge (kept inside the screen) and wrap long messages.
        private static void Place()
        {
            var srcRt = _src.GetComponent<RectTransform>();
            var rt = _clone.GetComponent<RectTransform>();
            var parent = rt != null ? rt.parent as RectTransform : null;
            if (srcRt == null || rt == null || parent == null) return;
            var srcText = _src.GetComponent<Text>();

            var c = new Vector3[4];
            srcRt.GetWorldCorners(c);                                      // 0 bottom-left, 1 top-left, 2 top-right
            Vector3 bl = parent.InverseTransformPoint(c[0]), tl = parent.InverseTransformPoint(c[1]), tr = parent.InverseTransformPoint(c[2]);
            float boxW = tr.x - tl.x, boxH = tl.y - bl.y;
            float textW = srcText.preferredWidth * Mathf.Abs(srcRt.localScale.x);
            if (textW <= 0f || textW > boxW) textW = boxW;
            float left = tl.x;
            switch (srcText.alignment)
            {
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter:
                    left = (tl.x + tr.x) * 0.5f - textW * 0.5f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight:
                    left = tr.x - textW; break;
            }

            Rect pr = parent.rect;
            const float margin = 12f;
            float x = Mathf.Max(margin, left - pr.xMin);
            float y = Mathf.Min(-margin, tl.y - pr.yMax);
            if (_src.activeInHierarchy) y -= boxH + 4f;                    // below the codex notice while it is showing

            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.localRotation = srcRt.localRotation;
            rt.localScale = srcRt.localScale;
            float sx = Mathf.Max(0.01f, Mathf.Abs(srcRt.localScale.x)), sy = Mathf.Max(0.01f, Mathf.Abs(srcRt.localScale.y));
            rt.sizeDelta = new Vector2(Mathf.Max(50f, (pr.width - x - margin) / sx), Mathf.Max(boxH, _text.fontSize * 1.5f) / sy);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static bool Ensure()
        {
            if (_clone != null && _text != null) return true;
            if (Time.unscaledTime < _nextFind) return false;
            _nextFind = Time.unscaledTime + 5f;
            _src = FindCodexEntry();
            if (_src == null || _src.GetComponent<Text>() == null) { _src = null; Plugin.Log.LogWarning("Codex notice not found; no ambush notification"); return false; }

            _clone = UnityEngine.Object.Instantiate(_src, _src.transform.parent, false);
            _clone.name = "Apocatremors_Notice";
            foreach (var f in _clone.GetComponentsInChildren<PlayMakerFSM>(true)) UnityEngine.Object.Destroy(f);
            foreach (var fit in _clone.GetComponents<ContentSizeFitter>()) UnityEngine.Object.Destroy(fit);
            _text = _clone.GetComponent<Text>();
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = false;
            switch (_text.alignment)
            {
                case TextAnchor.UpperCenter: case TextAnchor.UpperRight: _text.alignment = TextAnchor.UpperLeft; break;
                case TextAnchor.MiddleCenter: case TextAnchor.MiddleRight: _text.alignment = TextAnchor.MiddleLeft; break;
                case TextAnchor.LowerCenter: case TextAnchor.LowerRight: _text.alignment = TextAnchor.LowerLeft; break;
            }
            _clone.SetActive(false);
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
    }
}
