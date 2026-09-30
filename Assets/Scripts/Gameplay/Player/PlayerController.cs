using UnityEngine;
using Woodberry.Core;
using Woodberry.Core.Input;

namespace Woodberry.Gameplay.Player
{
    /// <summary>
    /// Кинематическое движение игрока в плоскости XZ.
    /// Физика твёрдых тел не используется намеренно: в top-down кооперативе она
    /// даёт дребезг и рассинхрон. Коллизии решает <see cref="CharacterController"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float DefaultMoveSpeed = 3.5f;
        private const float DefaultSprintMultiplier = 1.8f;

        [SerializeField]
        private float _moveSpeed = DefaultMoveSpeed;

        [SerializeField]
        private float _sprintMultiplier = DefaultSprintMultiplier;

        [Tooltip("Composition root сцены. Нужен, чтобы взять IInputReader в Start.")]
        [SerializeField]
        private GameBootstrap _bootstrap;

        private CharacterController _controller;
        private IInputReader _input;
        private Vector3 _facing = Vector3.forward;

#if UNITY_EDITOR
        private bool _warnedAboutMissingInput;
#endif

        /// <summary>
        /// Текущая скорость передвижения в единицах/сек (0 = стоит).
        /// Не нормализовано: это фактическая скорость, а не доля от максимума.
        /// </summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>Направление взгляда на плоскости XZ. Не зависит от камеры.</summary>
        public Vector3 Facing => _facing;

        /// <summary>
        /// Единственная точка внедрения зависимости. Тесты вызывают её напрямую;
        /// сцена полагается на <see cref="_bootstrap"/>. Компонент не ищет сервисы
        /// в сцене сам — это держит правило «никаких FindObjectOfType» проверяемым.
        /// </summary>
        public void Initialize(IInputReader input)
        {
            _input = input;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Start()
        {
            if (_input == null && _bootstrap != null)
            {
                _input = _bootstrap.Input;
            }
        }

        private void Update()
        {
            if (_input == null)
            {
                CurrentSpeed = 0f;

#if UNITY_EDITOR
                if (!_warnedAboutMissingInput)
                {
                    _warnedAboutMissingInput = true;
                    Debug.LogWarning(
                        $"{nameof(PlayerController)}: IInputReader не назначен. " +
                        "Либо вызови Initialize, либо укажи GameBootstrap в инспекторе. " +
                        "Игрок не будет двигаться.",
                        this);
                }
#endif
                return;
            }

            if (_controller == null)
            {
                CurrentSpeed = 0f;
                return;
            }

            float speed = _input.SprintHeld ? _moveSpeed * _sprintMultiplier : _moveSpeed;
            Vector3 displacement =
                PlayerMovement.ComputeDisplacement(_input.Move, speed, Time.deltaTime);

            _controller.Move(displacement);

            Vector2 planar = new Vector2(displacement.x, displacement.z);

            if (planar.sqrMagnitude > 0f)
            {
                _facing = new Vector3(planar.x, 0f, planar.y).normalized;
                CurrentSpeed = speed;
            }
            else
            {
                CurrentSpeed = 0f;
            }
        }
    }
}
