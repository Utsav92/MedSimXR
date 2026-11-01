using UnityEngine;
using UnityEngine.UI;

namespace MedSimXR.Equipment
{
    /// <summary>
    /// A world-space information panel (readable in desktop and VR) that shows an equipment entry.
    /// The panel turns to face the viewer around the vertical axis.
    /// </summary>
    public class WorldInfoPanel : MonoBehaviour
    {
        public string equipmentId;
        public Vector2 sizeMeters = new Vector2(0.6f, 0.4f);

        Transform _viewer;
        Text _text;
        GameObject _canvasGo;

        /// <summary>Panels name the equipment, so they are hidden while an identification quiz is running.</summary>
        public void SetVisible(bool visible) { if (_canvasGo != null) _canvasGo.SetActive(visible); }
        public bool Visible { get { return _canvasGo != null && _canvasGo.activeSelf; } }

        public void Build(EquipmentEntry e)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("InfoCanvas", typeof(Canvas), typeof(CanvasScaler));
            _canvasGo = canvasGo;
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(600, 400);
            rt.localScale = new Vector3(sizeMeters.x / 600f, sizeMeters.y / 400f, 1f);

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(rt, false);
            var brt = (RectTransform)bg.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.04f, 0.1f, 0.16f, 0.92f);

            var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(rt, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(18, 14); trt.offsetMax = new Vector2(-18, -14);
            _text = tgo.GetComponent<Text>();
            _text.font = font;
            _text.fontSize = 26;
            _text.color = Color.white;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;
            SetEntry(e);
        }

        public void SetEntry(EquipmentEntry e)
        {
            if (_text == null) return;
            _text.text = e.displayName.ToUpperInvariant() + "\n\n" + e.description + "\n\nTraining use: " + e.trainingUse
                         + (e.IsPlaceholder ? "\n\nPLACEHOLDER MODEL" : "\n\nSource: NVIDIA SimReady (CC-BY-4.0)");
        }

        public string CurrentText { get { return _text != null ? _text.text : ""; } }

        void LateUpdate()
        {
            if (_viewer == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _viewer = cam.transform;
            }
            Vector3 d = _viewer.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(-d.normalized, Vector3.up);
        }
    }
}
