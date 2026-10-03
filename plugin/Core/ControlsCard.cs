using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NIVR.Core;

internal sealed class ControlsCard
{
    private GameObject _root;
    private bool _showing;
    private float _shownAt;
    private bool _waitRelease;
    private Material _backgroundMaterial, _textMaterial;
    public bool Tick()
    {
        if (!_showing && VRConfig.ShowControlsCard.Value && !VRConfig.ControlsCardSeen.Value)
        { _showing = true; _shownAt = Time.unscaledTime; }
        if (!_showing) return false;
        bool any = false, pressed = false;
        for (int h = 0; h < 2; h++)
            for (int b = 0; b <= (int)VRButton.Stick; b++)
            { var c = VRRig.Controller((VRHand)h); any |= c.GetButton((VRButton)b); pressed |= c.GetButtonDown((VRButton)b); }
        if (Time.unscaledTime - _shownAt > 0.5f && pressed)
        { VRConfig.ControlsCardSeen.Value = true; VRConfig.File.Save(); _waitRelease = true; }
        if (_waitRelease && !any) { Hide(); _waitRelease = false; }
        if (!VRConfig.ShowControlsCard.Value) Hide();
        return _showing;
    }

    public void BeginEye(Transform head, float scale)
    {
        if (!_showing) return;
        if (_root == null)
        {
            _root = new GameObject("NIVR Controls Card") { layer = UiCapture.VrOnlyLayer };
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var rt = _root.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(920f, 510f);
            var bg = _root.AddComponent<Image>(); bg.color = new Color(0.025f, 0.055f, 0.05f, 0.97f); bg.raycastTarget = false;
            canvas.sortingOrder = 30000;
            _backgroundMaterial = new Material(Shader.Find("UI/Default")) { renderQueue = 4500 };
            _backgroundMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            bg.material = _backgroundMaterial;
            var labelGo = new GameObject("Controls") { layer = UiCapture.VrOnlyLayer };
            labelGo.transform.SetParent(_root.transform, false);
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var font in fonts) if (font != null && font.name.Contains("ChakraPetch")) { text.font = font; break; }
            if (text.fontSharedMaterial != null)
            {
                _textMaterial = new Material(text.fontSharedMaterial) { renderQueue = 4501 };
                _textMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                text.fontSharedMaterial = _textMaterial;
            }
            text.fontSize = 33f; text.color = new Color(0.36f, 0.95f, 0.86f); text.raycastTarget = false;
            text.text = "<size=48>VR controls</size>\n\nLeft stick: walk / navigate\nRight stick: turn / radio knob\nA or pointing trigger: use / select\nB: back     X: skip     Y: hint\nGrip: RT + RB (run / speed up / radio band)\nLeft Menu: pause\nHold both stick clicks: recenter\n\n<size=27>Press any controller button to continue</size>";
            var tr = labelGo.GetComponent<RectTransform>(); tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(35f, 25f); tr.offsetMax = new Vector2(-35f, -25f);
        }
        _root.transform.SetPositionAndRotation(head.position + head.forward * (1.3f * scale), head.rotation);
        _root.transform.localScale = Vector3.one * (0.0014f * scale);
        _root.SetActive(true);
    }
    // Keep the canvas active between eye renders so Unity can lay it out in its normal canvas pass.
    // Game cameras exclude layer 29, so it still never appears on the desktop.
    public void EndEye() { }
    public void Hide() { if (_root != null) _root.SetActive(false); _showing = false; }
    public void Dispose() { if (_root != null) UnityEngine.Object.Destroy(_root); if (_backgroundMaterial != null) UnityEngine.Object.Destroy(_backgroundMaterial); if (_textMaterial != null) UnityEngine.Object.Destroy(_textMaterial); }
}
