using UnityEngine;
using UnityEngine.Rendering;

namespace NIVR.Core;

internal sealed class ControllerVisuals
{
    private readonly GameObject[] _markers = new GameObject[2];
    private Material _material;

    public void BeginEyes(bool peephole)
    {
        for (int h = 0; h < 2; h++)
        {
            var c = VRRig.Controller((VRHand)h);
            bool show = !peephole && VRConfig.ControllerVisuals.Value && c.IsGripTracked && c.Grip != null;
            if (show && _markers[h] == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = h == 0 ? "NIVR Left Controller" : "NIVR Right Controller";
                go.layer = UiCapture.VrOnlyLayer;
                UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                UnityEngine.Object.DontDestroyOnLoad(go);
                _material ??= new Material(Shader.Find("UI/Default")) { color = new Color(0.19f, 0.57f, 0.51f), renderQueue = 4003 };
                _material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                var r = go.GetComponent<Renderer>(); r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                _markers[h] = go;
            }
            if (_markers[h] == null) continue;
            if (show)
            {
                float scale = VRRig.Origin.localScale.x;
                _markers[h].transform.SetPositionAndRotation(c.Grip.position, c.Grip.rotation);
                _markers[h].transform.localScale = new Vector3(0.035f, 0.055f, 0.09f) * scale;
            }
            _markers[h].SetActive(show);
        }
    }

    public void EndEyes() { for (int h = 0; h < 2; h++) if (_markers[h] != null) _markers[h].SetActive(false); }
    public void Dispose() { for (int h = 0; h < 2; h++) if (_markers[h] != null) UnityEngine.Object.Destroy(_markers[h]); if (_material != null) UnityEngine.Object.Destroy(_material); }
}
