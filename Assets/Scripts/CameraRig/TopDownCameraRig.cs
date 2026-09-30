using UnityEngine;

namespace Woodberry.CameraRig
{
    /// <summary>
    /// Камера вида сверху: держит фиксированный угол и следует за целью.
    /// Цель задаётся снаружи — сериализованной ссылкой в сцене или вызовом
    /// <see cref="SetTarget"/> из кода. Камера не ищет игрока сама, поэтому
    /// работает и для remote-игроков.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TopDownCameraRig : MonoBehaviour
    {
        [Tooltip("Объект следования. Задаётся сценой, не ищется в иерархии.")]
        [SerializeField]
        private Transform _target;

        [SerializeField]
        private float _height = 12f;

        [SerializeField]
        private Vector3 _lookOffset = new Vector3(0f, 0f, 2f);

        [SerializeField]
        private float _followSharpness = 6f;

        [Tooltip("Максимальная скорость перегона. Без ограничения камера пролетает карту при телепорте цели.")]
        [SerializeField]
        private float _maxFollowSpeed = 40f;

        [Tooltip("Смещение меньше этого значения игнорируется, чтобы стоящий игрок не уводил камеру дребезгом.")]
        [SerializeField]
        private float _positionDeadZone = 0.01f;

        private Vector3 _followVelocity;
        private Vector3 _lastTargetPosition;

        private void Awake()
        {
            if (_target != null)
            {
                SnapToTarget();
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            // Мёртвая зона: пока цель почти не двигается, камера стоит.
            // Иначе стоящий игрок уводит камеру микродребезгом каждый кадр.
            if ((_target.position - _lastTargetPosition).sqrMagnitude <=
                _positionDeadZone * _positionDeadZone)
            {
                return;
            }

            _lastTargetPosition = _target.position;

            transform.position = Vector3.SmoothDamp(
                transform.position,
                ComputeDesiredPosition(),
                ref _followVelocity,
                1f / Mathf.Max(_followSharpness, 0.01f),
                _maxFollowSpeed);

            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>Назначает объект следования и ставит камеру без рывка.</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            _followVelocity = Vector3.zero;

            if (_target != null)
            {
                SnapToTarget();
            }
        }

        private Vector3 ComputeDesiredPosition()
        {
            return _target.position + _lookOffset + Vector3.up * _height;
        }

        private void SnapToTarget()
        {
            _lastTargetPosition = _target.position;
            transform.position = ComputeDesiredPosition();
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}
