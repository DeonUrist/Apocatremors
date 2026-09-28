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

        public static void Reset() { _src = null; _clone = null; _text = null; _left = 0f; _nextFind = 0f; }

        public static void Show(string message)
        {
            if (!Plugin.ShowNotification.Value || string.IsNullOrEmpty(message)) return;
            if (!Ensure()) { Plugin.Log.LogInfo("(notice) " + message); return; }

            _text.text = message;
            Color col;
            if (ColorUtility.TryParseHtmlString(Plugin.NotificationColor.Value, out col)) _text.color = col;

            // sit below the codex notice while that one is showing
            var srcRt = _src.GetComponent<RectTransform>();
            var rt = _clone.GetComponent<RectTransform>();
            if (srcRt != null && rt != null)
            {
                var pos = srcRt.anchoredPosition;
                if (_src.activeInHierarchy) pos.y -= Mathf.Max(20f, srcRt.rect.height) + 4f;
                rt.anchoredPosition = pos;
            }
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
            _text = _clone.GetComponent<Text>();
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
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
