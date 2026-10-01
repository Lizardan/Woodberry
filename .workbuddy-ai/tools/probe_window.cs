// Диагностика обзора в Play Mode: где игрок, что видят источники.
var player = GameObject.Find("Player");
var overlay = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.VisibilityOverlay>();

var report = new Dictionary<string, object>
{
    { "player", player != null ? player.transform.position.ToString("F3") : "нет" },
    { "activeSources", overlay != null ? overlay.ActiveSourceCount : -1 },
    { "vertices", overlay != null ? overlay.LastVertexCount : -1 },
    { "camera", Camera.main != null ? Camera.main.transform.position.ToString("F3") : "нет" },
    { "orthoSize", Camera.main != null ? Camera.main.orthographicSize : -1f },
};

// Позиция, в которую нужно поставить игрока (пусто — не двигать).
string target = null;
var windows = UnityEngine.Object.FindObjectsByType<Woodberry.Gameplay.Perception.WindowVision>(
    FindObjectsSortMode.None);

var windowInfo = new List<string>();
foreach (var w in windows)
{
    windowInfo.Add(w.name + " origin=" + w.Origin.ToString("F2")
        + " up=" + w.OutwardDirection.ToString("F2")
        + " reveal=" + w.CurrentReveal.ToString("F3"));
}

report["windows"] = windowInfo;

return report;
