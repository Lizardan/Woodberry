// Сборка сцены Game: лес, дом с окнами, игрок, обзор и атмосфера.
// Всё делается через Unity API — .unity как текст не редактируется (правило AGENTS.md).
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var log = new List<string>();
var scenePath = "Assets/Woodberry/Scenes/Game.unity";

UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

// ── Утилиты ───────────────────────────────────────────────────────────────
void SetPrivate(UnityEngine.Object target, string field, System.Action<SerializedProperty> apply)
{
    var so = new SerializedObject(target);
    var prop = so.FindProperty(field);
    if (prop == null)
    {
        log.Add("НЕТ ПОЛЯ " + field + " у " + target.GetType().Name);
        return;
    }
    apply(prop);
    so.ApplyModifiedPropertiesWithoutUndo();
}

Sprite LoadSprite(string path)
{
    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
    if (sprite == null) log.Add("НЕТ СПРАЙТА " + path);
    return sprite;
}

var occluderLayer = LayerMask.NameToLayer("Occluder");
var maskLayer = LayerMask.NameToLayer("VisionMask");
var occluderMask = 1 << occluderLayer;

// ── Чистим прежнее содержимое сцены ───────────────────────────────────────
foreach (var go in scene.GetRootGameObjects())
{
    if (go.name == "Main Camera") continue;
    UnityEngine.Object.DestroyImmediate(go);
}

// ── Земля ─────────────────────────────────────────────────────────────────
var ground = new GameObject("Ground");
ground.transform.position = Vector3.zero;
var groundRenderer = ground.AddComponent<SpriteRenderer>();
groundRenderer.sprite = LoadSprite("Assets/Woodberry/Art/Environment/Ground/Ground_Forest.png");
groundRenderer.drawMode = SpriteDrawMode.Tiled;
groundRenderer.size = new Vector2(64f, 64f);
groundRenderer.sortingOrder = -100;

// ── Дом ───────────────────────────────────────────────────────────────────
// Интерьер: x от -4.5 до 4.5, y от -3 до 3. Толщина стены 0.5.
const float wallThickness = 0.5f;

var house = new GameObject("House");

var floorGo = new GameObject("Floor");
floorGo.transform.SetParent(house.transform, false);
var floorRenderer = floorGo.AddComponent<SpriteRenderer>();
floorRenderer.sprite = LoadSprite("Assets/Woodberry/Art/Environment/Floors/Floor_Interior.png");
floorRenderer.drawMode = SpriteDrawMode.Tiled;
floorRenderer.size = new Vector2(9.6f, 6.6f);
floorRenderer.sortingOrder = -90;

var wallSprite = LoadSprite("Assets/Woodberry/Art/Environment/Walls/Wall_Plank.png");

// Стена — один сегмент: спрайт тайлится вдоль длины, коллайдер блокирует обзор.
GameObject WallSegment(string name, Vector2 position, float length, float rotationZ, bool occludes)
{
    var go = new GameObject(name);
    go.transform.SetParent(house.transform, false);
    go.transform.position = position;
    go.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);

    var renderer = go.AddComponent<SpriteRenderer>();
    renderer.sprite = wallSprite;
    renderer.drawMode = SpriteDrawMode.Tiled;
    renderer.size = new Vector2(length, wallThickness);
    renderer.sortingOrder = -80;

    var collider = go.AddComponent<BoxCollider2D>();
    collider.size = new Vector2(length, wallThickness);

    if (occludes)
        go.layer = occluderLayer;

    return go;
}

// Север: окно на x = -2.5 (проём 2 юнита), поэтому стена разбита на два сегмента.
WallSegment("WallNorth_A", new Vector2(-4.5f, 3.25f), 2.0f, 0f, true);
WallSegment("WallNorth_B", new Vector2(2.0f, 3.25f), 7.0f, 0f, true);

// Юг: дверной проём на x от -1.1 до 1.1 — чтобы игрок мог выйти наружу.
WallSegment("WallSouth_A", new Vector2(-3.3f, -3.25f), 4.4f, 0f, true);
WallSegment("WallSouth_B", new Vector2(3.3f, -3.25f), 4.4f, 0f, true);

// Запад: сплошная.
WallSegment("WallWest", new Vector2(-4.75f, 0f), 7.0f, 90f, true);

// Восток: окно на y от -1 до 1.
WallSegment("WallEast_A", new Vector2(4.75f, -2.25f), 2.5f, 90f, true);
WallSegment("WallEast_B", new Vector2(4.75f, 2.25f), 2.5f, 90f, true);

// ── Окна ──────────────────────────────────────────────────────────────────
var windowSprite = LoadSprite("Assets/Woodberry/Art/Environment/Walls/Wall_Window.png");

GameObject MakeWindow(string name, Vector2 position, float rotationZ)
{
    var go = new GameObject(name);
    go.transform.SetParent(house.transform, false);
    go.transform.position = position;
    go.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);

    var renderer = go.AddComponent<SpriteRenderer>();
    renderer.sprite = windowSprite;
    renderer.sortingOrder = -79;

    // Окно не пропускает игрока, но пропускает обзор: коллайдер лежит на
    // обычном слое, а рейкаст обзора смотрит только на слой Occluder.
    var collider = go.AddComponent<BoxCollider2D>();
    collider.size = new Vector2(2f, wallThickness);

    return go;
}

// Северное окно смотрит наружу вверх: локальный up совпадает с +Y.
var northWindow = MakeWindow("WindowNorth", new Vector2(-2.5f, 3.25f), 0f);
// Восточное окно повёрнуто на -90: локальный up уходит в +X, то есть наружу.
var eastWindow = MakeWindow("WindowEast", new Vector2(4.75f, 0f), -90f);

// ── Лес ───────────────────────────────────────────────────────────────────
var forest = new GameObject("Forest");
var rng = new System.Random(20261001);

var treeSprites = new[]
{
    LoadSprite("Assets/Woodberry/Art/Environment/Foliage/Tree_A.png"),
    LoadSprite("Assets/Woodberry/Art/Environment/Foliage/Tree_B.png"),
    LoadSprite("Assets/Woodberry/Art/Environment/Foliage/Tree_C.png"),
};

int treeCount = 0;
for (int i = 0; i < 190 && treeCount < 64; i++)
{
    double angle = rng.NextDouble() * System.Math.PI * 2.0;
    double radius = 7.5 + rng.NextDouble() * 13.0;
    var position = new Vector3(
        (float)(System.Math.Cos(angle) * radius),
        (float)(System.Math.Sin(angle) * radius),
        0f);

    // Не ставим деревья вплотную к дому: он должен читаться целиком.
    if (System.Math.Abs(position.x) < 6.5f && System.Math.Abs(position.y) < 5.0f)
        continue;

    var tree = new GameObject("Tree_" + treeCount.ToString("00"));
    tree.transform.SetParent(forest.transform, false);
    tree.transform.position = position;
    tree.transform.rotation = Quaternion.Euler(0f, 0f, (float)(rng.NextDouble() * 360.0));
    float scale = 0.75f + (float)rng.NextDouble() * 0.6f;
    tree.transform.localScale = new Vector3(scale, scale, 1f);

    var renderer = tree.AddComponent<SpriteRenderer>();
    renderer.sprite = treeSprites[rng.Next(treeSprites.Length)];
    renderer.sortingOrder = 5;

    // Крона не только декор: она блокирует обзор так же, как стена.
    tree.layer = occluderLayer;
    var collider = tree.AddComponent<CircleCollider2D>();
    collider.radius = 0.5f;

    treeCount++;
}

var bushSprites = new[]
{
    LoadSprite("Assets/Woodberry/Art/Environment/Foliage/Bush_A.png"),
    LoadSprite("Assets/Woodberry/Art/Environment/Foliage/Bush_B.png"),
};

for (int i = 0; i < 40; i++)
{
    double angle = rng.NextDouble() * System.Math.PI * 2.0;
    double radius = 6.0 + rng.NextDouble() * 14.0;
    var position = new Vector3(
        (float)(System.Math.Cos(angle) * radius),
        (float)(System.Math.Sin(angle) * radius),
        0f);

    if (System.Math.Abs(position.x) < 5.5f && System.Math.Abs(position.y) < 4.0f)
        continue;

    var bush = new GameObject("Bush_" + i.ToString("00"));
    bush.transform.SetParent(forest.transform, false);
    bush.transform.position = position;
    bush.transform.rotation = Quaternion.Euler(0f, 0f, (float)(rng.NextDouble() * 360.0));
    float scale = 0.6f + (float)rng.NextDouble() * 0.7f;
    bush.transform.localScale = new Vector3(scale, scale, 1f);

    var renderer = bush.AddComponent<SpriteRenderer>();
    renderer.sprite = bushSprites[rng.Next(bushSprites.Length)];
    renderer.sortingOrder = 4;
}

log.Add("деревьев: " + treeCount);

// ── Игрок ─────────────────────────────────────────────────────────────────
var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Woodberry/Prefabs/Player.prefab");
var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
player.name = "Player";
player.transform.position = new Vector3(0f, 0.5f, 0f);

var playerVision = player.GetComponent<Woodberry.Gameplay.Perception.PlayerVisionSource>();

// ── Камера ────────────────────────────────────────────────────────────────
var cameraGo = GameObject.Find("Main Camera");
var camera = cameraGo.GetComponent<Camera>();
camera.orthographic = true;
camera.orthographicSize = 3.8f;
camera.backgroundColor = new Color(0.016f, 0.019f, 0.026f, 1f);
camera.clearFlags = CameraClearFlags.SolidColor;
camera.nearClipPlane = 0.1f;
camera.farClipPlane = 100f;
cameraGo.transform.position = new Vector3(0f, 0.5f, -10f);

var cameraData = cameraGo.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
if (cameraData != null)
{
    cameraData.renderPostProcessing = true;
    cameraData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.FastApproximateAntialiasing;
}
else
{
    log.Add("у камеры нет UniversalAdditionalCameraData");
}

var rig = cameraGo.GetComponent<Woodberry.CameraRig.TopDownCameraRig>();
if (rig != null)
{
    SetPrivate(rig, "_target", p => p.objectReferenceValue = player.transform);
    SetPrivate(rig, "_orthographicSize", p => p.floatValue = 3.8f);
}

// ── Окна как источники обзора ─────────────────────────────────────────────
void ConfigureWindow(GameObject window, float viewRadius, float halfAngle)
{
    var vision = window.AddComponent<Woodberry.Gameplay.Perception.WindowVision>();
    SetPrivate(vision, "_player", p => p.objectReferenceValue = player.transform);
    SetPrivate(vision, "_viewRadius", p => p.floatValue = viewRadius);
    SetPrivate(vision, "_halfAngleDegrees", p => p.floatValue = halfAngle);
    SetPrivate(vision, "_activationRadius", p => p.floatValue = 2.4f);
    SetPrivate(vision, "_fadeDistance", p => p.floatValue = 1.0f);
}

ConfigureWindow(northWindow, 8.5f, 34f);
ConfigureWindow(eastWindow, 8.5f, 34f);

// ── Обзор и темнота ───────────────────────────────────────────────────────
var visibilityGo = new GameObject("Visibility");
var overlay = visibilityGo.AddComponent<Woodberry.Gameplay.Perception.VisibilityOverlay>();

var maskMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Woodberry/Materials/VisionMask.mat");
var overlayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Woodberry/Materials/VisibilityOverlay.mat");

var sources = new MonoBehaviour[]
{
    playerVision,
    northWindow.GetComponent<Woodberry.Gameplay.Perception.WindowVision>(),
    eastWindow.GetComponent<Woodberry.Gameplay.Perception.WindowVision>(),
};

SetPrivate(overlay, "_targetCamera", p => p.objectReferenceValue = camera);
SetPrivate(overlay, "_occluderMask", p => p.intValue = occluderMask);
SetPrivate(overlay, "_maskLayer", p => p.intValue = maskLayer);
SetPrivate(overlay, "_maskMaterial", p => p.objectReferenceValue = maskMaterial);
SetPrivate(overlay, "_overlayMaterial", p => p.objectReferenceValue = overlayMaterial);
SetPrivate(overlay, "_darkness", p => p.floatValue = 0.985f);
SetPrivate(overlay, "_rayStepDegrees", p => p.floatValue = 2.0f);
SetPrivate(overlay, "_maskHeight", p => p.intValue = 384);
SetPrivate(overlay, "_sources", p =>
{
    p.arraySize = sources.Length;
    for (int i = 0; i < sources.Length; i++)
        p.GetArrayElementAtIndex(i).objectReferenceValue = sources[i];
});

// ── Атмосфера: пост-обработка ─────────────────────────────────────────────
var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
AssetDatabase.CreateAsset(profile, "Assets/Woodberry/Settings/GameMoodProfile.asset");

var vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
vignette.intensity.Override(0.52f);
vignette.smoothness.Override(0.55f);
vignette.color.Override(new Color(0.02f, 0.025f, 0.04f));

var grain = profile.Add<UnityEngine.Rendering.Universal.FilmGrain>(true);
grain.type.Override(UnityEngine.Rendering.Universal.FilmGrainLookup.Medium1);
grain.intensity.Override(0.42f);
grain.response.Override(0.8f);

var colorAdjustments = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
colorAdjustments.postExposure.Override(-0.35f);
colorAdjustments.contrast.Override(14f);
colorAdjustments.saturation.Override(-28f);
colorAdjustments.colorFilter.Override(new Color(0.85f, 0.90f, 1.0f));

var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
bloom.intensity.Override(0.35f);
bloom.threshold.Override(0.85f);
bloom.scatter.Override(0.6f);

var toneMapping = profile.Add<UnityEngine.Rendering.Universal.Tonemapping>(true);
toneMapping.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.Neutral);

AssetDatabase.SaveAssets();

var volumeGo = new GameObject("Global Volume");
var volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
volume.isGlobal = true;
volume.priority = 0f;
volume.sharedProfile = profile;

// Фоновое освещение сцены — в минимум: спрайты неосвещаемые, но фон должен
// быть холодным и почти чёрным, иначе на краях кадра проступает серость.
UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
UnityEngine.RenderSettings.ambientLight = new Color(0.02f, 0.024f, 0.032f);
UnityEngine.RenderSettings.fog = false;

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);

log.Add("сцена сохранена: " + scenePath);
log.Add("корневых объектов: " + scene.GetRootGameObjects().Length);

return new { log, roots = scene.GetRootGameObjects().Select(g => g.name).ToArray() };
