// Разведка: есть ли в этой сборке URP 2D-рендерер и 2D-свет.
var report = new Dictionary<string, object>();

string[] names = {
    "UnityEngine.Rendering.Universal.Renderer2D, Unity.RenderPipelines.Universal.Runtime",
    "UnityEngine.Rendering.Universal.Renderer2DData, Unity.RenderPipelines.Universal.Runtime",
    "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime",
    "UnityEngine.Rendering.Universal.ShadowCaster2D, Unity.RenderPipelines.Universal.Runtime",
    "UnityEngine.Rendering.Universal.Light2D+LightType, Unity.RenderPipelines.Universal.Runtime",
};

foreach (var n in names)
{
    var t = Type.GetType(n);
    report[n] = t == null ? "NOT FOUND" : t.FullName + " (asm: " + t.Assembly.GetName().Name + ")";
}

// Ищем любые типы с именем Renderer2D в загруженных сборках — на случай смены namespace.
var found = new List<string>();
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    Type[] types;
    try { types = asm.GetTypes(); } catch { continue; }
    foreach (var t in types)
    {
        if (t.Name == "Renderer2DData" || t.Name == "Renderer2D" || t.Name == "Light2D" || t.Name == "ShadowCaster2D")
            found.Add(t.FullName + "  [" + asm.GetName().Name + "]");
    }
}
report["scan"] = found.Distinct().OrderBy(x => x).ToList();

// Какие рендереры сейчас в URP-ассете.
var rp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
report["defaultRP"] = rp == null ? "null" : rp.GetType().FullName + " :: " + rp.name;
if (rp != null)
{
    var prop = rp.GetType().GetProperty("m_RendererDataList",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    var list = prop?.GetValue(rp) as System.Collections.IEnumerable;
    var names2 = new List<string>();
    if (list != null)
    {
        foreach (var item in list)
        {
            var obj = item as UnityEngine.Object;
            names2.Add(obj == null ? "null" : obj.name + " (" + obj.GetType().Name + ")");
        }
    }
    report["renderers"] = names2;
}

// Версия URP.
var urpAsm = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "Unity.RenderPipelines.Universal.Runtime");
report["urpVersion"] = urpAsm == null ? "?" : urpAsm.GetName().Version.ToString();

return report;
