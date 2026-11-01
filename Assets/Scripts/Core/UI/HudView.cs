using UnityEngine;
using UnityEngine.UI;

namespace MedSimXR.Core.UI
{
    /// <summary>Screen-space HUD built entirely in code. Uses legacy uGUI Text with the built-in font.</summary>
    public class HudView : MonoBehaviour
    {
        Font _font;
        Text _crosshair, _hover, _task, _status, _feedback, _info, _overlayTitle, _overlayBody, _banner;
        GameObject _feedbackPanel, _infoPanel, _overlayPanel, _taskPanel, _monitorPanel;
        Text _monitor;
        Canvas _canvas;
        CanvasScaler _scaler;
        Transform _follow;
        float _followDistance = 1.6f;
        bool _vrMode;

        public bool OverlayVisible { get { return _overlayPanel != null && _overlayPanel.activeSelf; } }

        void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("HudCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            _canvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            _scaler = scaler;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            _crosshair = MakeText(root, "Crosshair", "+", 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40, 40), Vector2.zero);

            _hover = MakeText(root, "Hover", "", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(900, 60), new Vector2(0, 70));

            _taskPanel = MakePanel(root, "TaskPanel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(520, 130), new Vector2(16, -16), new Color(0.05f, 0.1f, 0.15f, 0.8f));
            _task = MakeText(_taskPanel.transform, "Task", "", 20, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 12);
            _taskPanel.SetActive(false);

            _monitorPanel = MakePanel(root, "MonitorPanel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(380, 250), new Vector2(16, -160), new Color(0.0f, 0.04f, 0.02f, 0.92f));
            _monitor = MakeText(_monitorPanel.transform, "Monitor", "", 22, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 14);
            _monitor.color = new Color(0.45f, 1f, 0.55f);
            _monitorPanel.SetActive(false);

            _status = MakeText(root, "Status", "", 16, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(520, 60), new Vector2(-16, -16));

            _feedbackPanel = MakePanel(root, "FeedbackPanel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(900, 130), new Vector2(0, 130), new Color(0.05f, 0.1f, 0.15f, 0.88f));
            _feedback = MakeText(_feedbackPanel.transform, "Feedback", "", 20, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 14);
            _feedbackPanel.SetActive(false);

            _infoPanel = MakePanel(root, "InfoPanel", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(430, 360), new Vector2(-16, 0), new Color(0.05f, 0.1f, 0.15f, 0.9f));
            _info = MakeText(_infoPanel.transform, "Info", "", 18, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 14);
            _infoPanel.SetActive(false);

            _overlayPanel = MakePanel(root, "OverlayPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(820, 520), Vector2.zero, new Color(0.03f, 0.06f, 0.1f, 0.95f));
            _overlayTitle = MakeText(_overlayPanel.transform, "Title", "", 30, TextAnchor.UpperCenter, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 60), new Vector2(0, -16));
            _overlayBody = MakeText(_overlayPanel.transform, "Body", "", 20, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(-40, -110), new Vector2(0, -30));
            _overlayPanel.SetActive(false);

            _banner = MakeText(root, "Banner", "MedSim XR - PROTOTYPE - synthetic data - not clinically reviewed", 13, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(700, 24), new Vector2(12, 8));
            _banner.color = new Color(1f, 1f, 1f, 0.55f);
        }

        GameObject MakePanel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
            return go;
        }

        Text MakeText(Transform parent, string name, string text, int size, TextAnchor align,
                      Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Vector2 pos, float inset = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(anchorMin.x == anchorMax.x ? anchorMin.x : 0.5f, anchorMin.y == anchorMax.y ? anchorMin.y : 0.5f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = pos;
            if (inset > 0f) { rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset); }
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public void SetCrosshairVisible(bool v) { _crosshair.enabled = v && !_vrMode; }
        public bool VrMode { get { return _vrMode; } }
        public bool IsWorldSpace { get { return _canvas != null && _canvas.renderMode == RenderMode.WorldSpace; } }
        public string StatusText { get { return _status.text; } }

        /// <summary>VR: the screen-space overlay is invisible in a headset, so show the same HUD on a world-space panel that eases after the head.</summary>
        public void SetWorldSpace(Camera cam, float distance = 1.6f, float width = 1.8f)
        {
            _vrMode = true;
            _crosshair.enabled = false;
            _scaler.enabled = false;
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = cam;
            var rt = (RectTransform)_canvas.transform;
            rt.sizeDelta = new Vector2(1280, 720);
            rt.localScale = Vector3.one * (width / 1280f);
            _follow = cam.transform;
            _followDistance = distance;
            SnapToHead();
        }

        public void SetScreenSpace()
        {
            _vrMode = false;
            _follow = null;
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var rt = (RectTransform)_canvas.transform;
            rt.localScale = Vector3.one;
            _scaler.enabled = true;
            _crosshair.enabled = true;
        }

        Vector3 TargetPosition()
        {
            Vector3 fwd = _follow.forward; fwd.y = Mathf.Clamp(fwd.y, -0.3f, 0.3f);
            return _follow.position + fwd.normalized * _followDistance;
        }

        void SnapToHead()
        {
            if (_follow == null) return;
            _canvas.transform.position = TargetPosition();
            _canvas.transform.rotation = Quaternion.LookRotation(_canvas.transform.position - _follow.position, Vector3.up);
        }

        void LateUpdate()
        {
            if (_follow == null || !_vrMode) return;
            float k = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            _canvas.transform.position = Vector3.Lerp(_canvas.transform.position, TargetPosition(), k);
            var want = Quaternion.LookRotation(_canvas.transform.position - _follow.position, Vector3.up);
            _canvas.transform.rotation = Quaternion.Slerp(_canvas.transform.rotation, want, k);
        }
        public void SetHover(string s) { _hover.text = s ?? ""; }
        public string HoverText { get { return _hover.text; } }
        public string InfoText { get { return _info.text; } }
        public void SetStatus(string s) { _status.text = s ?? ""; }

        public void SetTask(string s)
        {
            _taskPanel.SetActive(!string.IsNullOrEmpty(s));
            _task.text = s ?? "";
        }

        public void ShowFeedback(string s, Color color)
        {
            _feedbackPanel.SetActive(!string.IsNullOrEmpty(s));
            _feedback.text = s ?? "";
            _feedback.color = color;
        }

        public void ShowInfo(string s)
        {
            _infoPanel.SetActive(!string.IsNullOrEmpty(s));
            _info.text = s ?? "";
        }

        public bool InfoVisible { get { return _infoPanel.activeSelf; } }

        public void ShowOverlay(string title, string body)
        {
            _overlayPanel.SetActive(true);
            _overlayTitle.text = title;
            _overlayBody.text = body;
        }

        public void HideOverlay() { _overlayPanel.SetActive(false); }

        public string OverlayTitle { get { return _overlayTitle.text; } }
        public string OverlayBody { get { return _overlayBody.text; } }

        /// <summary>Simulated patient-monitor readout (top-left, below the task panel). Null or empty hides it.</summary>
        public void SetMonitor(string s)
        {
            if (_monitorPanel == null) return;
            _monitorPanel.SetActive(!string.IsNullOrEmpty(s));
            _monitor.text = s ?? "";
        }

        public string MonitorText { get { return _monitor != null ? _monitor.text : ""; } }

        /// <summary>Text currently shown, for automated tests.</summary>
        public string DebugText()
        {
            return "[task]" + _task.text + "[feedback]" + _feedback.text + "[overlay]" + _overlayTitle.text + " " + _overlayBody.text;
        }
    }
}
