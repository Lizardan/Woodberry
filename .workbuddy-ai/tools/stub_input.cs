// Заглушка ввода: единственная штатная точка внедрения у PlayerController.
// В сцене Game нет composition root, поэтому ввода нет и игрок не пойдёт.
// Подставляем читатель, который «держит» направление, и смотрим, что
// персонаж реально идёт, а аниматор переключается в Walk.
public sealed class ProbeInput : Woodberry.Core.Input.IInputReader
{
    public UnityEngine.Vector2 Direction = new UnityEngine.Vector2(1f, 0f);

    public UnityEngine.Vector2 Move => Direction;
    public bool SprintHeld => false;
    public bool InteractPressed => false;
    public void EnableGameplayInput() { }
    public void DisableGameplayInput() { }
}

var player = GameObject.Find("Player");
var controller = player.GetComponent<Woodberry.Gameplay.Player.PlayerController>();

if (ProbeInputHolder.Instance == null)
    ProbeInputHolder.Instance = new ProbeInput();

ProbeInputHolder.Instance.Direction = new UnityEngine.Vector2(1f, 0f);
controller.Initialize(ProbeInputHolder.Instance);

return "заглушка ввода подключена, направление (1,0)";
