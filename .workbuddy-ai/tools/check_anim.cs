// Проверка анимации: идёт ли смена кадров и меняется ли поза.
var player = GameObject.Find("Player");
var animator = player.GetComponent<Animator>();
var visual = player.transform.Find("Visual");
var renderer = visual.GetComponent<SpriteRenderer>();

var state = animator.GetCurrentAnimatorStateInfo(0);
var names = new List<string>
{
    "state=" + (state.IsName("Idle") ? "Idle" : state.IsName("Walk") ? "Walk" : "?")
        + " normalized=" + state.normalizedTime.ToString("F2"),
    "sprite=" + (renderer.sprite != null ? renderer.sprite.name : "нет"),
};

// Принудительно включаем шаг: в этой сцене нет ввода, сам игрок не пойдёт.
animator.SetBool("IsMoving", true);

return names;
