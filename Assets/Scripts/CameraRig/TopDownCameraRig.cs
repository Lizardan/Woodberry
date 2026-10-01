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

            // Мёртвая зона считается по ОСТАТКУ ПУТИ КАМЕРЫ, а не по смещению
            // цели за кадр.
            //
            // Здесь был дефект: зона сравнивалась с дельтой цели, и после
            // первого же шага «цель не двигалась» становилось истиной —
            // камера замирала, не догнав цель, и оставалась позади навсегда.
            // На глаз это выглядит как «камера отстала», а не как отказ,
            // поэтому дефект и прожил до первой сцены с телепортом.
            //
            // Сравнение с остатком решает обе задачи сразу: пока камера не
            // доехала — она едет, а когда доехала — микродребезг цели её
            // больше не трогает.
            Vector2 desiredPosition = CurrentTargetPosition() + _lookOffset;
            Vector2 currentPosition = CurrentCameraPosition();

            if ((desiredPosition - currentPosition).sqrMagnitude
                <= _positionDeadZone * _positionDeadZone)
            {
                _followVelocity = Vector2.zero;
                return;
            }

            // Z камеры задаёт удаление от плоскости спрайтов и не должен
            // вычисляться следованием. Неявное присваивание Vector2 в
            // transform.position обнулило бы Z и уронило камеру в плоскость
            // спрайтов — ровно тогда, когда игрок начинает двигаться, то есть
            // в самый неожиданный момент.
            Vector3 desired = Vector2.SmoothDamp(
                currentPosition,
                desiredPosition,
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
            Vector2 desired = CurrentTargetPosition() + _lookOffset;
            transform.position = new Vector3(desired.x, desired.y, _depth);
        }
    }
}
