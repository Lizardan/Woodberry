using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Woodberry.Lighting.U2D
{
    /// <summary>Режим сглаживания мягкой тени.</summary>
    public enum U2DShadowSmooth
    {
        /// <summary>Размытие карты теней гауссом.</summary>
        Blur,

        /// <summary>Аналитический объёмный источник (полутень от диска света).</summary>
        VolumeLight,
    }

    /// <summary>
    /// База для всех источников 2D-света. Порт <c>Light2DBase</c> из
    /// SardineFish/Unity2DLighting.
    ///
    /// Теневая геометрия строится здесь же: для каждого полигона-препятствия
    /// считается «объём» тени (polygon → две касательные + дуга на границе
    /// радиуса света). Результат складывается в один меш и рисуется в карту
    /// теней через CommandBuffer.
    /// </summary>
    public abstract class U2DLight2DBase : MonoBehaviour
    {
        /// <summary>Все созданные источники. Порт AssetsManager из оригинала.</summary>
        private static readonly List<U2DLight2DBase> Registry = new List<U2DLight2DBase>();

        public static IReadOnlyList<U2DLight2DBase> All => Registry;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // В проекте выключен domain reload — статика обязана сбрасываться сама.
            Registry.Clear();
        }

        public LightShadows Shadows = LightShadows.None;

        public U2DShadowSmooth ShadowSmooth = U2DShadowSmooth.Blur;

        [Range(1, 16)]
        public int SmoothRadius = 4;

        [Tooltip("Радиус светящегося диска. Влияет на полутень при объёмной мягкой тени.")]
        public float LightVolume = 1f;

        [Tooltip("Радиус, на который достаёт свет.")]
        public float LightDistance = 20f;

        [Tooltip("Показывать границы света и объёма в Scene View.")]
        public bool DebugLight;

        private readonly Collider2D[] _shadowCasters = new Collider2D[256];
        private readonly List<Mesh> _subMeshes = new List<Mesh>(64);

        private Mesh _shadowMesh;
        private Mesh _tempMesh;
        private Material _shadowMaterial;

        protected virtual void OnEnable()
        {
            // Конструктор в оригинале регистрировал себя сразу; в Unity надёжнее
            // делать это в OnEnable, чтобы объект попадал в список только активным.
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }
        }

        protected virtual void OnDisable()
        {
            Registry.Remove(this);
        }

        protected virtual void OnDestroy()
        {
            Registry.Remove(this);
            if (_shadowMesh != null) Destroy(_shadowMesh);
            if (_tempMesh != null) Destroy(_tempMesh);
            for (int i = 0; i < _subMeshes.Count; i++)
            {
                if (_subMeshes[i] != null) Destroy(_subMeshes[i]);
            }
        }

        /// <summary>Отрисовка собственно света в lightmap.</summary>
        public abstract void RenderLight(UnsafeCommandBuffer cmd, ref U2DLightRenderingData data);

        private Material ShadowMaterial
        {
            get
            {
                if (_shadowMaterial == null)
                {
                    _shadowMaterial = U2DShaderPool.Get("Woodberry/U2DShadow");
                }
                return _shadowMaterial;
            }
        }

        /// <summary>
        /// Набор точек препятствия в мировых координатах. Умеет все типы
        /// коллайдеров, которые встречаются в проекте: стены — Box, деревья —
        /// Circle, окна и мебель — Box, произвольные контуры — Polygon.
        /// </summary>
        private static Vector2[] GetColliderPoints(Collider2D caster)
        {
            switch (caster)
            {
                case BoxCollider2D box:
                {
                    Vector2 c = box.offset;
                    Vector2 h = box.size * 0.5f;
                    return new[]
                    {
                        c + new Vector2(-h.x, -h.y),
                        c + new Vector2(h.x, -h.y),
                        c + new Vector2(h.x, h.y),
                        c + new Vector2(-h.x, h.y),
                    };
                }

                case CircleCollider2D circle:
                {
                    // Окружность аппроксимируется многоугольником: объём тени
                    // всё равно строится по касательным к рёбрам.
                    const int segments = 16;
                    var points = new Vector2[segments];
                    for (int i = 0; i < segments; i++)
                    {
                        float a = 2f * Mathf.PI * i / segments;
                        points[i] = circle.offset + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * circle.radius;
                    }
                    return points;
                }

                case CapsuleCollider2D capsule:
                {
                    // Капсула как прямоугольник со скруглёнными торцами.
                    Vector2 h = capsule.size * 0.5f;
                    float r = Mathf.Min(h.x, h.y);
                    var list = new List<Vector2>();
                    for (int i = 0; i < 5; i++)
                    {
                        float a = Mathf.PI * (0.5f + i / 4f);
                        list.Add(capsule.offset + new Vector2(Mathf.Cos(a) * r, h.y - r + Mathf.Sin(a) * r));
                    }
                    for (int i = 0; i < 5; i++)
                    {
                        float a = -Mathf.PI * (0.5f + i / 4f);
                        list.Add(capsule.offset + new Vector2(Mathf.Cos(a) * r, -h.y + r + Mathf.Sin(a) * r));
                    }
                    return list.ToArray();
                }

                case PolygonCollider2D polygon:
                {
                    var list = new List<Vector2>();
                    for (int path = 0; path < polygon.pathCount; path++)
                    {
                        list.AddRange(polygon.GetPath(path));
                    }
                    return list.Count > 0 ? list.ToArray() : null;
                }

                case EdgeCollider2D edge:
                    return edge.points;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Строит тень от препятствия, заданного точками в мировых координатах.
        /// Порт <c>PolygonShadowMesh</c>: для каждого ребра считаются две
        /// касательные к диску источника и дальняя дуга на границе радиуса.
        /// </summary>
        public Mesh BuildShadowMesh(Vector2[] worldPoints, Mesh target)
        {
            if (worldPoints == null || worldPoints.Length < 2)
            {
                return null;
            }

            Matrix4x4 toLight = transform.worldToLocalMatrix;
            int n = worldPoints.Length;
            U2DMeshBuilder builder = new U2DMeshBuilder(5 * n, 3 * n);

            float r2 = LightDistance * LightDistance;
            float rv2 = LightVolume * LightVolume;
            float sqrtR2MinusRv2 = Mathf.Sqrt(Mathf.Max(0f, r2 - rv2));

            for (int i = 0; i < n; i++)
            {
                Vector2 w0 = worldPoints[(i + 1) % n];
                Vector2 w1 = worldPoints[i];

                Vector3 p0 = toLight.MultiplyPoint3x4(w0);
                Vector3 p1 = toLight.MultiplyPoint3x4(w1);
                p0.z = 0f;
                p1.z = 0f;

                float mag0 = Mathf.Max(p0.magnitude, 0.0001f);
                float mag1 = Mathf.Max(p1.magnitude, 0.0001f);

                // Угол между направлением на точку и касательной к диску света.
                float ang0 = Mathf.Asin(Mathf.Clamp(LightVolume / mag0, -1f, 1f));
                float ang1 = Mathf.Asin(Mathf.Clamp(LightVolume / mag1, -1f, 1f));

                // Rotate работает с Vector2, поэтому на входе берём XY, а обратно
                // возвращаем Vector3 явно — иначе компилятор не выведет тип.
                Vector2 rotA = U2DMathUtility.Rotate(new Vector2(p0.x, p0.y), -ang0);
                Vector2 rotB = U2DMathUtility.Rotate(new Vector2(p1.x, p1.y), ang1);

                Vector3 shadowA = new Vector3(rotA.x, rotA.y, 0f).normalized *
                                  (sqrtR2MinusRv2 - mag0 * Mathf.Cos(ang0));
                Vector3 shadowB = new Vector3(rotB.x, rotB.y, 0f).normalized *
                                  (sqrtR2MinusRv2 - mag1 * Mathf.Cos(ang1));
                shadowA += p0;
                shadowB += p1;

                int meshType = 0;

                // Случай «тень закручивается за точку» — заменяем дальнюю точку.
                if (Vector3.Cross(p1 - p0, shadowB - p1).z >= 0f)
                {
                    meshType |= 1;
                    Vector2 rotA2 = U2DMathUtility.Rotate(new Vector2(p0.x, p0.y), ang0);
                    shadowB = new Vector3(rotA2.x, rotA2.y, 0f).normalized *
                              (sqrtR2MinusRv2 - mag0 * Mathf.Cos(ang0)) + p0;
                }

                if (Vector3.Cross(p0 - shadowA, p1 - p0).z >= 0f)
                {
                    meshType |= 2;
                    Vector2 rotB2 = U2DMathUtility.Rotate(new Vector2(p1.x, p1.y), -ang1);
                    shadowA = new Vector3(rotB2.x, rotB2.y, 0f).normalized *
                              (sqrtR2MinusRv2 - mag1 * Mathf.Cos(ang1)) + p1;
                }

                Vector3 oc = (shadowA + shadowB) * 0.5f;
                Vector3 shadowR = oc.normalized * (r2 / Mathf.Max(oc.magnitude, 0.0001f));

                Vector3[] verts;
                int[] tris;
                Vector2[] uvA;
                Vector2[] uvB;

                if (meshType == 0)
                {
                    verts = new[] { p0, p1, shadowB, shadowA, shadowR };
                    tris = new[] { 0, 3, 4, 1, 0, 4, 1, 4, 2 };
                    uvA = new[] { (Vector2)p0, (Vector2)p0, (Vector2)p0, (Vector2)p0, (Vector2)p0 };
                    uvB = new[] { (Vector2)p1, (Vector2)p1, (Vector2)p1, (Vector2)p1, (Vector2)p1 };
                }
                else if (meshType == 1)
                {
                    verts = new[] { p0, shadowB, shadowA, shadowR };
                    tris = new[] { 0, 2, 3, 0, 3, 1 };
                    uvA = new[] { (Vector2)p0, (Vector2)p0, (Vector2)p0, (Vector2)p0 };
                    uvB = new[] { (Vector2)p1, (Vector2)p1, (Vector2)p1, (Vector2)p1 };
                }
                else if (meshType == 2)
                {
                    verts = new[] { p1, shadowB, shadowA, shadowR };
                    tris = new[] { 0, 2, 3, 0, 3, 1 };
                    uvA = new[] { (Vector2)p0, (Vector2)p0, (Vector2)p0, (Vector2)p0 };
                    uvB = new[] { (Vector2)p1, (Vector2)p1, (Vector2)p1, (Vector2)p1 };
                }
                else
                {
                    verts = new[] { p1, p0, shadowB, shadowA, shadowR };
                    tris = new[] { 0, 3, 4, 1, 0, 4, 1, 4, 2 };
                    uvA = new[] { (Vector2)p1, (Vector2)p1, (Vector2)p1, (Vector2)p1, (Vector2)p1 };
                    uvB = new[] { (Vector2)p0, (Vector2)p0, (Vector2)p0, (Vector2)p0, (Vector2)p0 };
                }

                builder.AddVertsAndTriangles(verts, tris, uvA, uvB);
            }

            return builder.ToMesh(target != null ? target : NewTempMesh());
        }

        private Mesh NewTempMesh()
        {
            Mesh mesh = new Mesh { name = "U2D Sub Shadow Mesh" };
            _subMeshes.Add(mesh);
            return mesh;
        }

        /// <summary>
        /// Рисует тень источника в карту теней. Порт <c>RenderShadow</c>.
        /// </summary>
        /// <param name="cmd">Unsafe-буфер: установка глобалов и DrawMesh.</param>
        /// <param name="nativeCmd">Нативный буфер: нужен <see cref="Blitter"/>
        /// для гауссова размытия.</param>
        public void RenderShadow(UnsafeCommandBuffer cmd, CommandBuffer nativeCmd,
            ref U2DLightRenderingData data, TextureHandle shadowTarget)
        {
            if (Shadows == LightShadows.None || ShadowMaterial == null)
            {
                return;
            }

            if (_shadowMesh == null)
            {
                _shadowMesh = new Mesh { name = "U2D Shadow Mesh" };
            }

            if (_tempMesh == null)
            {
                _tempMesh = new Mesh { name = "U2D Sub Shadow Mesh" };
                _subMeshes.Add(_tempMesh);
            }

            U2DMeshBuilder builder = new U2DMeshBuilder();

            // Тень строят только препятствия слоя Occluder — те же, что режут
            // обзор (ADR 0008). Раньше фильтр брал все слои, и в тени попадало
            // всё подряд: игрок затенял свет и получал огромный клин тьмы
            // от самого себя, а окно переставало пропускать свет, хотя обязано
            // (в Occluder его нет намеренно — оно проём, а не препятствие).
            //
            // Правило «препятствие — это слой, а не условие в коде» здесь то же,
            // что и в системе обзора: чтобы сделать объект непрозрачным для
            // света, его кладут на Occluder.
            var filter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask = data.Settings != null
                    ? data.Settings.ShadowCasterMask
                    : (LayerMask)(1 << U2DLightSystemSettings.DefaultOccluderLayer),
                useDepth = false,
                useOutsideDepth = false,
                useNormalAngle = false,
                useOutsideNormalAngle = false,
            };

            int count = Physics2D.OverlapCircle(
                transform.position, LightDistance, filter, _shadowCasters);

            for (int i = 0; i < count; i++)
            {
                Collider2D caster = _shadowCasters[i];
                if (caster == null || !caster.enabled || caster.isTrigger)
                {
                    continue;
                }

                // Препятствие не должно затенять само себя, если источник внутри него.
                if (caster.transform.IsChildOf(transform))
                {
                    continue;
                }

                Vector2[] localPoints = GetColliderPoints(caster);
                if (localPoints == null)
                {
                    continue;
                }

                // Точки коллайдера в локальных координатах объекта — переводим в
                // мировые, BuildShadowMesh сам вернёт их в координаты источника.
                var worldPoints = new Vector2[localPoints.Length];
                for (int p = 0; p < localPoints.Length; p++)
                {
                    worldPoints[p] = caster.transform.TransformPoint(localPoints[p]);
                }

                Mesh sub = BuildShadowMesh(worldPoints, _tempMesh);
                if (sub != null)
                {
                    builder.AddCopiedMesh(sub);
                }
            }

            _shadowMesh = builder.ToMesh(_shadowMesh);

            Matrix4x4 trs = Matrix4x4.TRS(transform.position, transform.rotation, transform.localScale);

            // Таргет уже выставлен композитором; здесь только рисуем.
            if (Shadows == LightShadows.Soft && ShadowSmooth == U2DShadowSmooth.VolumeLight)
            {
                cmd.SetGlobalFloat("_LightSize", Mathf.Max(LightVolume, 0.01f));
                // Pass 1 — объёмная тень, полутень считается в шейдере.
                cmd.DrawMesh(_shadowMesh, trs, ShadowMaterial, 0, 1);
            }
            else
            {
                // Pass 0 — жёсткая тень.
                cmd.DrawMesh(_shadowMesh, trs, ShadowMaterial, 0, 0);

                if (Shadows == LightShadows.Soft && ShadowSmooth == U2DShadowSmooth.Blur)
                {
                    Material blur = U2DShaderPool.Get("Woodberry/U2DGaussian");
                    U2DGaussianBlur.Blur(SmoothRadius, cmd, nativeCmd, shadowTarget, shadowTarget,
                        data.ShadowMapScratch, blur);
                }
            }
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (!DebugLight)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.85f, 0.3f, 1f);
            Gizmos.DrawWireSphere(transform.position, LightVolume);
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            Gizmos.DrawWireSphere(transform.position, LightDistance);
        }
    }
}
