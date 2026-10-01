using UnityEngine;

namespace Woodberry.CameraRig
{
    /// <summary>
    /// Камера вида сверху для 2D-сцены: строго вертикально вниз, без наклона.
    ///
    /// Ортографическая проекция обязательна: перспектива сверху «разъезжается»
    /// на краях экрана и ломает ровную сетку, по которой игроки координируются.
    /// Сцена читается как схема, а не как фотография.
    ///
    /// Цель задаётся снаружи: сериализованной ссылкой в сцене или
    /// <see cref="SetTarget"/>. Камера не ищет игрока сама, поэтому работает и
    /// для remote-игроков.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TopDownCameraRig : MonoBehaviour
    {
        [Tooltip("Объект следования. Задаётся сценой, не ищется в иерархии.")]
        [SerializeField]
        private Transform _target;

        [Tooltip("Половина высоты видимой области в мировых единицах.")]
        [SerializeField]
        private float _orthographicSize = 5f;

        [Tooltip("Смещение взгляда: сдвиг камеры относительно цели.")]
        [SerializeField]
        private Vector2 _lookOffset = new Vector2(0f, 1f);

        [Tooltip("Жёсткость слежения. Больше — быстрее догоняет, но жёстче.")]
        [SerializeField]
        private float _followSharpness = 6f;

        [Tooltip("Максимальная скорость перегона. Без ограничения камера пролетает карту при телепорте цели.")]
        [SerializeField]
        private float _maxFollowSpeed = 40f;

        [Tooltip("Смещение меньше этого значения игнорируется, чтобы стоящий игрок не уводил камеру дребезгом.")]
        [SerializeField]
        private float _positionDeadZone = 0.01f;

        private Vector2 _followVelocity;
        private Vector2 _lastTargetPosition;
        private float _depth;

        /// <summary>Цель следования. null — камера стоит на месте.</summary>
        public Transform Target => _target;

        private void Awake()
        {
            ConfigureOrthographic();
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            Vector2 target = CurrentTargetPosition();

            // Мёртвая зона: пока цель почти не двигается, камера стоит.
            // Иначе стоящий игрок уводит камеру микродребезгом каждый кадр.
            if ((target - _lastTargetPosition).sqrMagnitude <= _positionDeadZone * _positionDeadZone)
            {
                return;
            }

            _lastTargetPosition = target;

            // Z камеры задаёт удаление от плоскости спрайтов и не должен
            // вычисляться следованием. Неявное присваивание Vector2 в
            // transform.position обнулило бы Z и уронило камеру в плоскость
            // спрайтов — ровно тогда, когда игрок начинает двигаться, то есть
            // в самый неожиданный момент.
            Vector3 desired = Vector2.SmoothDamp(
                CurrentCameraPosition(),
                target + _lookOffset,
                ref _followVelocity,
                1f / Mathf.Max(_followSharpness, 0.01f),
                _maxFollowSpeed);

            transform.position = new Vector3(desired.x, desired.y, CurrentCameraDepth());
        }

        /// <summary>Назначает объект следования и ставит камеру без рывка.</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            _followVelocity = Vector2.zero;

            if (_target != null)
            {
                SnapToTarget();
            }
        }

        private void ConfigureOrthographic()
        {
            Camera camera = GetComponent<Camera>();

            // Строго сверху: без наклона и перспективы. Это требование проекта,
            // а не настройка удобства.
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(0.1f, _orthographicSize);
            transform.rotation = Quaternion.identity;

            // Глубина снимается один раз: следование её не должно трогать.
            _depth = transform.position.z;
        }

        private Vector2 CurrentTargetPosition()
        {
            Vector3 position = _target.position;
            return new Vector2(position.x, position.y);
        }

        private Vector2 CurrentCameraPosition()
        {
            Vector3 position = transform.position;
            return new Vector2(position.x, position.y);
        }

        /// <summary>
        /// Глубина камеры. Фиксируется один раз в Awake: следование по XY её
        /// не меняет, а потерять значение нельзя — камера упадёт в плоскость
        /// спрайтов и сцена станет пустой.
        /// </summary>
        private float CurrentCameraDepth()
        {
            return _depth;
        }

        private void SnapToTarget()
        {
            _lastTargetPosition = CurrentTargetPosition();
            Vector2 desired = _lastTargetPosition + _lookOffset;
            transform.position = new Vector3(desired.x, desired.y, _depth);
        }
    }
}
