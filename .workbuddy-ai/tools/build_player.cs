// Персонаж: слои, материалы, покадровые анимации, контроллер и префаб.
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var log = new List<string>();

// ── Слои ──────────────────────────────────────────────────────────────────
void EnsureLayer(string name)
{
    var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
    var tagManager = new SerializedObject(assets[0]);
    var layers = tagManager.FindProperty("layers");

    for (int i = 0; i < layers.arraySize; i++)
    {
        if (layers.GetArrayElementAtIndex(i).stringValue == name)
        {
            log.Add("слой уже есть: " + name + " = " + i);
            return;
        }
    }

    for (int i = 8; i < layers.arraySize; i++)
    {
        var slot = layers.GetArrayElementAtIndex(i);
        if (string.IsNullOrEmpty(slot.stringValue))
        {
            slot.stringValue = name;
            tagManager.ApplyModifiedProperties();
            log.Add("слой добавлен: " + name + " = " + i);
            return;
        }
    }

    log.Add("НЕТ СВОБОДНОГО СЛОЯ для " + name);
}

EnsureLayer("Occluder");
EnsureLayer("VisionMask");

int occluderLayer = LayerMask.NameToLayer("Occluder");
int maskLayer = LayerMask.NameToLayer("VisionMask");
log.Add("Occluder=" + occluderLayer + " VisionMask=" + maskLayer);

// ── Материалы ─────────────────────────────────────────────────────────────
Material MakeMaterial(string shaderName, string path)
{
    var shader = Shader.Find(shaderName);
    if (shader == null)
    {
        log.Add("НЕТ ШЕЙДЕРА: " + shaderName);
        return null;
    }

    var material = new Material(shader);
    AssetDatabase.CreateAsset(material, path);
    log.Add("материал: " + path);
    return AssetDatabase.LoadAssetAtPath<Material>(path);
}

var maskMaterial = MakeMaterial("Woodberry/VisionMask",
    "Assets/Woodberry/Materials/VisionMask.mat");
var overlayMaterial = MakeMaterial("Woodberry/VisibilityOverlay",
    "Assets/Woodberry/Materials/VisibilityOverlay.mat");

// ── Спрайты ───────────────────────────────────────────────────────────────
Sprite[] Frames(string path, int expected)
{
    var sprites = AssetDatabase.LoadAllAssetsAtPath(path)
        .OfType<Sprite>()
        .OrderBy(s => s.name)
        .ToArray();

    if (sprites.Length != expected)
        log.Add("ВНИМАНИЕ: " + path + " дал " + sprites.Length + " кадров вместо " + expected);

    return sprites;
}

var idleFrames = Frames("Assets/Woodberry/Art/Characters/Player_TopDown_Idle.png", 4);
var walkFrames = Frames("Assets/Woodberry/Art/Characters/Player_TopDown_Walk.png", 8);
var shadowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
    "Assets/Woodberry/Art/Characters/Player_TopDown_Shadow.png");

// ── Клипы ─────────────────────────────────────────────────────────────────
AnimationClip MakeClip(string path, Sprite[] frames, float fps, string nodePath)
{
    var clip = new AnimationClip { frameRate = fps };
    clip.name = System.IO.Path.GetFileNameWithoutExtension(path);

    var binding = new EditorCurveBinding
    {
        type = typeof(SpriteRenderer),
        path = nodePath,
        propertyName = "m_Sprite",
    };

    var keys = new ObjectReferenceKeyframe[frames.Length];
    for (int i = 0; i < frames.Length; i++)
    {
        keys[i] = new ObjectReferenceKeyframe
        {
            time = i / fps,
            value = frames[i],
        };
    }

    AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

    // Зацикливание обязательно: без него персонаж отшагивает один цикл и
    // замирает на середине шага, а состояние аниматора остаётся «Walk» —
    // то есть проверка «аниматор в Walk» такой отказ не поймает.
    var settings = AnimationUtility.GetAnimationClipSettings(clip);
    settings.loopTime = true;
    AnimationUtility.SetAnimationClipSettings(clip, settings);

    AssetDatabase.CreateAsset(clip, path);
    return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
}

var idleClip = MakeClip("Assets/Woodberry/Art/Characters/Animations/Player_TopDown_Idle.anim",
    idleFrames, 6f, "Visual");
var walkClip = MakeClip("Assets/Woodberry/Art/Characters/Animations/Player_TopDown_Walk.anim",
    walkFrames, 12f, "Visual");
log.Add("клипы: idle " + idleClip.length.ToString("F2") + "c, walk " + walkClip.length.ToString("F2") + "c");

// ── Контроллер ────────────────────────────────────────────────────────────
var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(
    "Assets/Woodberry/Art/Characters/Animations/Player_TopDown.controller");

controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

var stateMachine = controller.layers[0].stateMachine;
var idleState = stateMachine.AddState("Idle");
idleState.motion = idleClip;
var walkState = stateMachine.AddState("Walk");
walkState.motion = walkClip;
stateMachine.defaultState = idleState;

// Переходы строго между состояниями, без AnyState: AnyState -> Walk
// срабатывает и когда уже стоишь в Walk, и ноги начинают дрожать
// вместо шага (этот баг уже ловили в Stage 02).
void Link(UnityEditor.Animations.AnimatorState from, UnityEditor.Animations.AnimatorState to, bool moving)
{
    var transition = from.AddTransition(to);
    transition.hasExitTime = false;
    transition.duration = 0.08f;
    transition.AddCondition(moving ? UnityEditor.Animations.AnimatorConditionMode.If
                            : UnityEditor.Animations.AnimatorConditionMode.IfNot,
                            0f, "IsMoving");
}

Link(idleState, walkState, true);
Link(walkState, idleState, false);
log.Add("контроллер: Idle + Walk по параметру IsMoving");

// ── Префаб ────────────────────────────────────────────────────────────────
// SaveAsPrefabAsset перезаписывает существующий ассет на этом пути,
// поэтому отдельное удаление не нужно (и запрещено политикой инструмента).
var root = new GameObject("Player");
root.layer = 0;

var body = root.AddComponent<Rigidbody2D>();
body.bodyType = RigidbodyType2D.Kinematic;
body.gravityScale = 0f;
body.interpolation = RigidbodyInterpolation2D.Interpolate;
body.constraints = RigidbodyConstraints2D.FreezeRotation;

var collider = root.AddComponent<CircleCollider2D>();
collider.radius = 0.22f;
collider.offset = new Vector2(0f, 0.02f);

// Тень — отдельный узел вне Visual: она не должна поворачиваться вместе
// с персонажем, иначе эллипс тени уезжает вбок при повороте на 90 градусов.
var shadow = new GameObject("Shadow");
shadow.transform.SetParent(root.transform, false);
shadow.transform.localPosition = new Vector3(0f, -0.02f, 0f);
var shadowRenderer = shadow.AddComponent<SpriteRenderer>();
shadowRenderer.sprite = shadowSprite;
shadowRenderer.sortingOrder = -20;

var visual = new GameObject("Visual");
visual.transform.SetParent(root.transform, false);
var visualRenderer = visual.AddComponent<SpriteRenderer>();
visualRenderer.sprite = idleFrames[0];
visualRenderer.sortingOrder = 0;

var animator = root.AddComponent<Animator>();
animator.runtimeAnimatorController = controller;
// Unity 6.5: AnimatorUpdateMode.AnimatePhysics переименован в Fixed,
// а синхронизация трансформов с физикой вынесена в отдельный флаг.
animator.updateMode = AnimatorUpdateMode.Fixed;
animator.animatePhysics = true;
animator.applyRootMotion = false;

var controllerComponent = root.AddComponent<Woodberry.Gameplay.Player.PlayerController>();
var vision = root.AddComponent<Woodberry.Gameplay.Perception.PlayerVisionSource>();

// _animator — приватное сериализованное поле, поэтому только через SerializedObject.
var serialized = new SerializedObject(controllerComponent);
var animatorField = serialized.FindProperty("_animator");
if (animatorField != null)
{
    animatorField.objectReferenceValue = animator;
    serialized.ApplyModifiedPropertiesWithoutUndo();
    log.Add("PlayerController._animator привязан");
}
else
{
    log.Add("НЕ НАЙДЕНО поле _animator у PlayerController");
}

var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Woodberry/Prefabs/Player.prefab");
UnityEngine.Object.DestroyImmediate(root);
log.Add("префаб: " + (prefab != null ? "сохранён" : "НЕ СОХРАНЁН"));

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

return new { log, occluderLayer, maskLayer };
