string name = "Scene2Button";
var go = UnityEngine.GameObject.Find(name);
if (go == null)
{
    return "кнопка не найдена: " + name;
}

var btn = go.GetComponent<UnityEngine.UI.Button>();
if (btn == null)
{
    return "нет Button на " + name;
}

btn.onClick.Invoke();
return "клик по " + name + " выполнен";
