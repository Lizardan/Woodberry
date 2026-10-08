var sys = Woodberry.Lighting.U2D.U2DLightSystem.Instance;
if (sys == null) return "no system";
var so = new UnityEditor.SerializedObject(sys);
var p = so.FindProperty("_settings").FindPropertyRelative("_globalLight");
p.colorValue = new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f);
so.ApplyModifiedPropertiesWithoutUndo();
return "globalLight=" + p.colorValue;
