// Состояние компонента слежения: включён ли, что в полях, идёт ли время.
var cam = Camera.main;
var rig = cam.GetComponent<Woodberry.CameraRig.TopDownCameraRig>();

var so = new SerializedObject(rig);
var fields = new List<string>();
foreach (var name in new[]
{
    "_orthographicSize", "_lookOffset", "_followSharpness",
    "_maxFollowSpeed", "_positionDeadZone",
})
{
    var prop = so.FindProperty(name);
    if (prop == null) { fields.Add(name + "=НЕТ"); continue; }

    fields.Add(name + "=" + (prop.propertyType == SerializedPropertyType.Vector2
        ? prop.vector2Value.ToString("F3")
        : prop.propertyType == SerializedPropertyType.Float
            ? prop.floatValue.ToString("F3")
            : prop.propertyType.ToString()));
}

return new
{
    rigEnabled = rig != null && rig.enabled,
    rigActive = rig != null && rig.gameObject.activeInHierarchy,
    camEnabled = cam != null && cam.enabled,
    deltaTime = Time.deltaTime,
    timeScale = Time.timeScale,
    fields,
    camPos = cam.transform.position.ToString("F3"),
    targetPos = rig.Target != null ? rig.Target.position.ToString("F3") : "NULL",
};
