using UnityEngine;
using Woodberry.Core;
using Woodberry.Core.Input;

namespace Woodberry.Gameplay.Player
{
    /// <summary>
    /// Движение игрока в плоскости XY через кинематический <see cref="Rigidbody2D"/>.
    ///
    /// Почему кинематический, а не динамический: spec запрещает динамическое
    /// тело для игрока — в top-down кооперативе оно даёт дребезг и рассинхрон
    /// между клиентами. Кинематическое тело — точная аналогия трёхмерного
    /// <c>CharacterController</c> в 2D: двигаем сами, но учитываем коллизии.
    ///
    /// Движение считается в <c>FixedUpdate</c>, то есть с фиксированным тиком.
    /// Это совпадает с требованием сетевого тика 20–30 Гц из
    /// <c>coop-networking.md</c>, поэтому перенос на сеть не потребует
    /// переписывания цикла движения.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float DefaultMoveSpeed = 3.5f;
        private const float DefaultSprintMultiplier = 1.8f;

        /// <summary>Имя параметра аниматора: идёт ли шаг.</summary>
        private const string IsMovingParameter = "IsMoving";

        /// <summary>Узел, который разворачивается по направлению движения.</summary>
        private const string VisualNodeName = "Visual";

        private static readonly int IsMovingHash = Animator.StringToHash(IsMovingParameter);

        [SerializeField]
        private float _moveSpeed = DefaultMoveSpeed;

        [SerializeField]
        private float _sprintMultiplier = DefaultSprintMultiplier;

        [Tooltip("Аниматор персонажа. Необязателен: без него движение работает, анимация нет.")]
        [SerializeField]
        private Animator _animator;

        private Rigidbody2D _body;
        private IInputReader _input;
        private Transform _visual;
        private Vector2 _facing = Vector2.down;
        private bool _bodyMissing;

#if UNITY_EDITOR
        private bool _warnedAboutMissingInput;
#endif

        /// <summary>
        /// Текущая скорость передвижения в единицах/сек (0 = стоит).
        /// Не нормализовано: это фактическая скорость, а не доля от максимума.
        /// </summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>Направление взгляда в плоскости XY. Не зависит от камеры.</summary>
        public Vector2 Facing => _facing;

        /// <summary>
        /// Единственная точка внедрения зависимости. Тесты вызывают её напрямую.
        /// Если её не вызвали, <see cref="Start"/> берёт ввод из глобального
        /// <see cref="ServiceRegistry"/> — так сцена не зависит от того, в какой
        /// сцене живёт composition root.
        /// </summary>
        public void Initialize(IInputReader input)
        {
            _input = input;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _visual = transform.Find(VisualNodeName);

            // RequireComponent срабатывает только когда компонент ДОБАВЛЯЮТ.
            // Если скрипт изменился у объекта, который уже лежит в сцене,
            // Unity не доливает требуемые компоненты повторно — объект
            // остаётся без тела, и обращение к нему падает. Поэтому проверяем
            // явно и говорим, что делать, вместо MissingComponentException.
            if (_body == null)
            {
                _bodyMissing = true;
                Debug.LogError(
                    $"{nameof(PlayerController)}: на объекте нет {nameof(Rigidbody2D)}. " +
                    $"Добавь {nameof(Rigidbody2D)} и {nameof(CircleCollider2D)} " +
                    "(RequireComponent не помогает для объектов, уже сохранённых в сцене). " +
                    "Игрок не будет двигаться.", this);
                return;
            }

            // Кинематическое тело: позицией управляем мы, а не симуляция.
            // Без этого Unity будет стремить тело вниз и игрок провалится.
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
        }

        private void Start()
        {
            if (_input == null)
            {
                ServiceRegistry.TryGet(out _input);
            }
        }

        private void FixedUpdate()
        {
            if (_bodyMissing)
            {
                // Ошибка уже сказана в Awake. Повторять её каждый физический тик
                // значит засорять консоль и замедлять отладку.
                return;
            }

            if (_input == null)
            {
                StopWalking();
                ReportMissingInput();
                return;
            }

            if (_body == null)
            {
                CurrentSpeed = 0f;
                SetMoving(false);
                return;
            }

            float speed = _input.SprintHeld ? _moveSpeed * _sprintMultiplier : _moveSpeed;
            Vector2 displacement =
                PlayerMovement.ComputeDisplacement(_input.Move, speed, Time.fixedDeltaTime);

            _body.MovePosition(_body.position + displacement);

            if (displacement.sqrMagnitude > 0f)
            {
                _facing = displacement.normalized;
                CurrentSpeed = speed;
                ApplyFacing();
            }
            else
            {
                CurrentSpeed = 0f;
            }

            SetMoving(CurrentSpeed > 0f);
        }

        /// <summary>
        /// Разворачивает спрайт по направлению движения.
        ///
        /// Четыре направления: набор из восьми потребовал бы вдвое больше
        /// графики, а четыре — минимум, при котором персонаж читается.
        /// Влево и вправо — зеркальные, поэтому ассетов всего четыре.
        ///
        /// Поворот идёт на узле <c>Visual</c>, а не на корне: корень двигает
        /// физическое тело, и его поворот утащил бы за собой кинематику.
        /// </summary>
        private void ApplyFacing()
        {
            if (_visual == null)
            {
                return;
            }

            // Сам угол считает чистая функция: её можно проверить тестом,
            // а каскад условий здесь — нельзя, и именно в нём лево с правом
            // однажды поменялись местами.
            float degrees = PlayerFacing.ComputeZRotation(_facing);
            _visual.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private void StopWalking()
        {
            CurrentSpeed = 0f;
            SetMoving(false);
        }

        private void SetMoving(bool isMoving)
        {
            if (_animator != null)
            {
                _animator.SetBool(IsMovingHash, isMoving);
            }
        }

        /// <summary>
        /// Пишет предупреждение один раз за сессию. В рантайме не логируется
        /// ничего: промах с инъекцией — это ошибка сцены, а не игрока.
        /// </summary>
        private void ReportMissingInput()
        {
#if UNITY_EDITOR
            if (_warnedAboutMissingInput)
            {
                return;
            }

            _warnedAboutMissingInput = true;
            Debug.LogWarning(
                $"{nameof(PlayerController)}: IInputReader недоступен. " +
                "Вызови Initialize или запусти сцену через Bootstrap. " +
                "Игрок не будет двигаться.",
                this);
#endif
        }
    }
}
