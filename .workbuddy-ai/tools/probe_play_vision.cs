// Диагностика во время Play: камера, маска, фактические значения шейдера.
var log = new List<string>();

var overlay = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.VisibilityOverlay>();
if (overlay == null) { log.Add("overlay НЕ НАЙДЕН"); return string.Join("\n", log); }

var cam = overlay.GetComponentInParent<Camera>();
if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<Camera>();

log.Add("isPlaying = " + Application.isPlaying);
log.Add("screen = " + Screen.width + "x" + Screen.height);

var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
foreach (var c in cams)
{
    log.Add("камера " + c.name + " ortho=" + c.orthographic
        + " size=" + c.orthographicSize + " depth=" + c.depth
        + " culling=" + c.cullingMask + " tex=" + (c.targetTexture == null ? "null" : c.targetTexture.name));
}

var target = overlay.GetType()
    .GetField("_targetCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var targetCam = target == null ? null : target.GetValue(overlay) as Camera;
log.Add("_targetCamera = " + (targetCam == null ? "null" : targetCam.name + " size=" + targetCam.orthographicSize));

var maskCamField = overlay.GetType()
    .GetField("_maskCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var maskCam = maskCamField == null ? null : maskCamField.GetValue(overlay) as Camera;
log.Add("_maskCamera = " + (maskCam == null ? "null" : maskCam.name + " size=" + maskCam.orthographicSize));

var maskTexField = overlay.GetType()
    .GetField("_maskTexture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var maskTex = maskTexField == null ? null : maskTexField.GetValue(overlay) as RenderTexture;
log.Add("_maskTexture = " + (maskTex == null ? "null" : maskTex.width + "x" + maskTex.height));

var matField = overlay.GetType()
    .GetField("_overlayMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var overlayMat = matField == null ? null : matField.GetValue(overlay) as Material;
if (overlayMat != null)
{
    log.Add("overlayMat.Найм = " + overlayMat.name);
    log.Add("  _Darkness=" + overlayMat.GetFloat("_Darkness"));
    log.Add("  _EdgeSoftness=" + overlayMat.GetFloat("_EdgeSoftness"));
    log.Add("  _FalloffStart=" + overlayMat.GetFloat("_FalloffStart"));
    log.Add("  _FalloffPower=" + overlayMat.GetFloat("_FalloffPower"));
}

var maskMatField = overlay.GetType()
    .GetField("_maskMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var maskMat = maskMatField == null ? null : maskMatField.GetValue(overlay) as Material;
if (maskMat != null)
{
    log.Add("maskMat.Имя = " + maskMat.name);
    log.Add("  _FalloffStart=" + maskMat.GetFloat("_FalloffStart"));
    log.Add("  _FalloffPower=" + maskMat.GetFloat("_FalloffPower"));
    log.Add("  _EdgeSoftness=" + maskMat.GetFloat("_EdgeSoftness"));
}
else
{
    log.Add("maskMat = null");
}

log.Add("вершин в меше = " + overlay.LastVertexCount);
log.Add("источников = " + overlay.ActiveSourceCount);
log.Add("точек полигона = " + overlay.LastPolygonPoints.Count);

// Радиус обзора: дошли ли новые 4.2 до игрока.
var vis = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.PlayerVisionSource>();
log.Add("игрок radius = " + (vis == null ? "НЕТ" : vis.Radius.ToString()));

return string.Join("\n", log);
