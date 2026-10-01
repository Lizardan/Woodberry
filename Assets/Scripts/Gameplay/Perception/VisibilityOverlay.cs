using System.Collections.Generic;
using UnityEngine;

namespace Woodberry.Gameplay.Perception
{
    /// <summary>
    /// Рисует темноту и прорезает в ней видимую область.
    ///
    /// Как это работает:
    /// <list type="number">
    /// <item>источники обзора отдают свои конусы;</item>
    /// <item>по каждому конусу пускается веер лучей, лучи упираются в стены —
    /// отсюда берётся форма видимой области;</item>
    /// <item>форма рисуется в отдельную текстуру (маску) вспомогательной
    /// камерой;</item>
    /// <item>поверх кадра кладётся затемняющий четырёхугольник, который
    /// пропускает свет ровно там, где маска белая.</item>
    /// </list>
    ///
    /// Почему маска, а не сразу полигон темноты: источников обзора может быть
    /// несколько (несколько игроков в кооперативе, фонарь, враг), и их
    /// области пересекаются. Объединять полигоны в один — отдельная тяжёлая
    /// задача, а маска складывается сама: перекрытия суммируются и обрезаются
    /// по единице.
    ///
    /// Почему не 2D-свет из URP: он потребовал бы смены рендерера в
    /// настройках проекта и перевода всех спрайтов на другой материал.
    /// Это меняет не механику, а весь проект целиком — см. ADR 0008.
    ///
    /// Стены блокируют обзор не «логикой», а физикой: луч останавливается
    /// на коллайдере, поэтому увидеть сквозь стену невозможно в принципе.
    /// Это и есть требование «угол зрения притупляется при препятствиях».
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VisibilityOverlay : MonoBehaviour
    {
        private const string MaskObjectName = "VisionMaskGeometry";

        /// <summary>
        /// Дистанция, ниже которой попадание считается стартом внутри
        /// коллайдера, а не препятствием.
        /// </summary>
        private const float InsideColliderEpsilon = 0.01f;

        /// <summary>
        /// Ёмкость буфера попаданий на один луч. Нужен только ближайший
        /// результат, поэтому запаса хватает с большим избытком.
        /// </summary>
        private const int HitBufferSize = 16;

        [Header("Композиция")]
        [Tooltip("Камера, поверх кадра которой кладётся темнота. Задаётся сценой.")]
        [SerializeField]
        private Camera _targetCamera;

        [Tooltip("Источники обзора. Пусто — обзор не рисуется, но сцена работает.")]
        [SerializeField]
        private MonoBehaviour[] _sources = new MonoBehaviour[0];

        [Header("Препятствия")]
        [Tooltip("Слои, которые блокируют обзор. Стены и только они.")]
        [SerializeField]
        private LayerMask _occluderMask = ~0;

        [Header("Качество")]
        [Tooltip("Шаг веера лучей в градусах. Меньше — точнее и дороже.")]
        [Range(0.5f, 15f)]
        [SerializeField]
        private float _rayStepDegrees = 2.5f;

        [Tooltip("Насколько расходятся лучи по краям препятствия, в градусах.")]
        [Range(0.05f, 3f)]
        [SerializeField]
        private float _edgeEpsilonDegrees = 0.6f;

        [Tooltip("Высота маски в пикселях. Автоматически поднимается минимум до половины высоты экрана.")]
        [Range(64, 1080)]
        [SerializeField]
        private int _maskHeight = 512;

        [Tooltip("Слой, на котором живёт геометрия маски. Не должен попадать в основную камеру.")]
        [SerializeField]
        private int _maskLayer = 31;

        [Header("Материалы")]
        [SerializeField]
        private Material _maskMaterial;

        [SerializeField]
        private Material _overlayMaterial;

        [Tooltip("Непрозрачность темноты. 1 — кромешная тьма, 0.97 — читаемые силуэты.")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _darkness = 0.985f;

        private readonly List<VisionCone> _cones = new List<VisionCone>(8);
        private readonly List<IVisionSource> _visionSources = new List<IVisionSource>(4);
        private readonly List<float> _angles = new List<float>(512);
        private readonly List<float> _distances = new List<float>(512);
        private readonly List<Vector2> _hitPoints = new List<Vector2>(64);
        private readonly List<Vector2> _lastPoints = new List<Vector2>(512);

        /// <summary>Буфер под попадания лучей. Предвыделен: аллокаций в кадре нет.</summary>
        private readonly RaycastHit2D[] _hitBuffer = new RaycastHit2D[HitBufferSize];
        private readonly List<Vector3> _vertices = new List<Vector3>(1024);
        private readonly List<Color> _colors = new List<Color>(1024);
        private readonly List<int> _triangles = new List<int>(3072);

        private Camera _maskCamera;
        private RenderTexture _maskTexture;
        private Mesh _mesh;
        private Transform _quad;

        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private bool _reportedMissingCamera;

        /// <summary>Сколько источников реально отдало конусы в последнем кадре.</summary>
        public int ActiveSourceCount { get; private set; }

        /// <summary>Число вершин последнего построенного полигона. Для тестов и отладки.</summary>
        public int LastVertexCount => _vertices.Count;

        /// <summary>
        /// Точки дальнего края последнего построенного обзора.
        ///
        /// Существует ради проверки главного требования — «не видит сквозь
        /// стены»: тест ставит стену и убеждается, что ни одна точка обзора
        /// не оказалась за ней. По картинке это не проверить, а мерить форму
        /// полигона больше нечем.
        /// </summary>
        public IReadOnlyList<Vector2> LastPolygonPoints => _lastPoints;

        /// <summary>
        /// Единственная точка внедрения зависимостей сцены. Вызывается
        /// composition root'ом сцены либо тестом.
        /// </summary>
        public void Initialize(Camera targetCamera, IReadOnlyList<IVisionSource> sources,
                               LayerMask occluderMask)
        {
            _targetCamera = targetCamera;
            _occluderMask = occluderMask;

            _visionSources.Clear();
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (sources[i] != null)
                    {
                        _visionSources.Add(sources[i]);
                    }
                }
            }

            CacheSourcesFromSerialized();
        }

        private void Awake()
        {
            CacheSourcesFromSerialized();
        }

        /// <summary>
        /// Готовит маску и затемняющий четырёхугольник при первом же кадре,
        /// когда камера уже известна.
        ///
        /// Раньше это делалось в <c>Awake</c>, и компонент требовал камеру
        /// уже к моменту создания. Из-за этого его нельзя было собрать из
        /// теста: <c>AddComponent</c> вызывает <c>Awake</c> сразу, до того как
        /// тест успевал передать зависимости, и обзор молча отключался.
        /// Ленивая подготовка убирает это ограничение и ничего не ломает
        /// в сцене: там камера задана сериализованной ссылкой.
        /// </summary>
        private bool EnsureReady()
        {
            if (_targetCamera == null)
            {
                ReportMissingCamera();
                return false;
            }

            if (_maskCamera != null)
            {
                return true;
            }

            CacheSourcesFromSerialized();

            // Материалы клонируются: свойства маски меняются каждый кадр, а
            // правка общего материала пачкает ассет в редакторе и делит
            // состояние между всеми, кто этот материал использует.
            _maskMaterial = InstantiateMaterial(_maskMaterial, "VisionMaskInstance");
            _overlayMaterial = InstantiateMaterial(_overlayMaterial, "VisibilityOverlayInstance");

            CreateMaskCamera();
            CreateOverlayQuad();

            // Геометрия маски не должна попасть в основную камеру.
            _targetCamera.cullingMask &= ~(1 << _maskLayer);
            return true;
        }

        /// <summary>
        /// Пишет про отсутствие камеры один раз. Повторять это каждый кадр
        /// значит засорить консоль и спрятать настоящую причину.
        /// </summary>
        private void ReportMissingCamera()
        {
            if (_reportedMissingCamera)
            {
                return;
            }

            _reportedMissingCamera = true;
            Debug.LogError(
                $"{nameof(VisibilityOverlay)}: не задана целевая камера. " +
                "Темнота не будет рисоваться. Задай ссылку в сцене или вызови Initialize.", this);
        }

        private void OnDestroy()
        {
            if (_maskTexture != null)
            {
                _maskTexture.Release();
                Destroy(_maskTexture);
                _maskTexture = null;
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            // Копии материалов живут только пока живёт компонент.
            if (_maskMaterial != null)
            {
                Destroy(_maskMaterial);
                _maskMaterial = null;
            }

            if (_overlayMaterial != null)
            {
                Destroy(_overlayMaterial);
                _overlayMaterial = null;
            }
        }

        private void LateUpdate()
        {
            if (!EnsureReady())
            {
                return;
            }

            SyncMaskView();
            EnsureMaskTexture();
            BuildVisibility();
            ApplyOverlayProperties();
        }

        /// <summary>
        /// Собирает источники из сериализованного массива. MonoBehaviour нельзя
        /// сериализовать как интерфейс, поэтому проверка идёт в рантайме.
        /// </summary>
        private void CacheSourcesFromSerialized()
        {
            if (_visionSources.Count > 0 || _sources == null)
            {
                return;
            }

            for (int i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] is IVisionSource source)
                {
                    _visionSources.Add(source);
                }
            }
        }

        /// <summary>
        /// Делает рантайм-копию материала. Общий материал менять нельзя:
        /// в редакторе это пачкает ассет, а в сборке делит состояние между
        /// всеми его потребителями.
        /// </summary>
        private static Material InstantiateMaterial(Material source, string instanceName)
        {
            if (source == null)
            {
                return null;
            }

            var instance = new Material(source) { name = instanceName };
            return instance;
        }

        private void CreateMaskCamera()
        {
            // Камера маски — ДОЧЕРНЯЯ основной, с единичным локальным
            // трансформом. Это не косметика: раньше она копировала позицию
            // основной в LateUpdate, а порядок LateUpdate между компонентами
            // не определён. Если обзор отрабатывал раньше слежения за игроком,
            // маска снималась со ВЧЕРАШНЕЙ позиции камеры, а кадр рисовался
            // с сегодняшней: видимая область уезжала относительно мира на кадр
            // движения, и её граница тряслась. Дочерний узел берёт трансформ
            // родителя в момент отрисовки, поэтому рассинхрон невозможен.
            var go = new GameObject("VisionMaskCamera");
            go.transform.SetParent(_targetCamera.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            _maskCamera = go.AddComponent<Camera>();
            _maskCamera.orthographic = true;
            _maskCamera.clearFlags = CameraClearFlags.SolidColor;
            _maskCamera.backgroundColor = Color.black;
            _maskCamera.cullingMask = 1 << _maskLayer;
            _maskCamera.allowHDR = false;
            _maskCamera.allowMSAA = false;
            _maskCamera.useOcclusionCulling = false;
            _maskCamera.depth = -100f;
            _maskCamera.nearClipPlane = 0.01f;
            _maskCamera.farClipPlane = Mathf.Max(50f, _targetCamera.farClipPlane);
            _maskCamera.enabled = true;

            // Геометрия маски лежит в корне сцены с единичным трансформом:
            // вершины меша заданы в мировых координатах. Будучи дочерней
            // объекту камеры, она уехала бы вместе с ней и обзор сместился бы.
            var maskGeometry = new GameObject(MaskObjectName);
            maskGeometry.transform.SetParent(null);
            maskGeometry.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            maskGeometry.transform.localScale = Vector3.one;
            maskGeometry.layer = _maskLayer;

            _mesh = new Mesh { name = "VisionMaskMesh" };
            _mesh.MarkDynamic();

            maskGeometry.AddComponent<MeshFilter>().sharedMesh = _mesh;

            var renderer = maskGeometry.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _maskMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void CreateOverlayQuad()
        {
            var go = new GameObject("VisibilityDarkness");
            go.transform.SetParent(_targetCamera.transform, false);
            go.transform.localRotation = Quaternion.identity;
            _quad = go.transform;

            var mesh = new Mesh { name = "VisibilityOverlayQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _overlayMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Подгоняет вид маски под основную камеру.
        ///
        /// Позицию и поворот здесь трогать НЕЛЬЗЯ и не нужно: камера маски —
        /// дочерняя основной и берёт её трансформ в момент отрисовки. Синхро-
        /// низировать приходится только то, что от трансформа не зависит.
        /// </summary>
        private void SyncMaskView()
        {
            _maskCamera.orthographicSize = _targetCamera.orthographicSize;

            if (_quad != null)
            {
                float height = _targetCamera.orthographicSize * 2f;
                float width = height * _targetCamera.aspect;
                _quad.localPosition = new Vector3(0f, 0f, _targetCamera.nearClipPlane + 0.05f);
                _quad.localScale = new Vector3(width, height, 1f);
            }
        }

        private void EnsureMaskTexture()
        {
            // Маска не должна быть грубее половины экрана. При низком разрешении
            // жёсткая граница обзора ложится на крупные тексели, и при движении
            // её кромка «ползёт» ступеньками — это читается как дрожь контура.
            int target = Mathf.Max(_maskHeight, Screen.height / 2);
            int height = Mathf.Clamp(target, 256, 1080);
            int width = Mathf.Max(64, Mathf.RoundToInt(height * _targetCamera.aspect));

            if (_maskTexture != null
                && _maskTexture.width == width
                && _maskTexture.height == height
                && _lastScreenWidth == Screen.width
                && _lastScreenHeight == Screen.height)
            {
                return;
            }

            if (_maskTexture != null)
            {
                _maskCamera.targetTexture = null;
                _maskTexture.Release();
                Destroy(_maskTexture);
            }

            // ARGB32, а не R8: одноканальный формат поддерживается не на всех
            // платформах, а маска читается только из красного канала — разницы
            // по качеству нет, зато нет и риска получить пустую текстуру.
            //
            // Глубина 16 обязательна: URP в render graph ругается на выходную
            // текстуру камеры без буфера глубины и роняет предупреждение
            // в консоль на каждом кадре.
            _maskTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "VisionMask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
            };
            _maskTexture.Create();

            _maskCamera.targetTexture = _maskTexture;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
        }

        private void BuildVisibility()
        {
            _cones.Clear();

            for (int i = 0; i < _visionSources.Count; i++)
            {
                _visionSources[i].CollectCones(_cones);
            }

            ActiveSourceCount = _cones.Count;

            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
            _lastPoints.Clear();

            for (int i = 0; i < _cones.Count; i++)
            {
                AppendCone(_cones[i]);
            }

            _mesh.Clear();

            if (_vertices.Count < 3)
            {
                return;
            }

            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// Строит один веер: вершина в точке обзора, дальний край — по попаданиям.
        /// Цвет вершины задаёт затухание: у центра маска белая, у края гаснет.
        /// </summary>
        private void AppendCone(VisionCone cone)
        {
            if (cone.IsEmpty)
            {
                return;
            }

            _angles.Clear();
            _hitPoints.Clear();

            VisionFanBuilder.BuildAngles(cone, _rayStepDegrees, _angles);

            if (_angles.Count == 0)
            {
                return;
            }

            RaycastAngles(cone, _angles, _distances, _hitPoints);

            // Уточняем границы по кромкам препятствий, затем досчитываем
            // добавленные углы — иначе край тени «плывёт» между лучами.
            VisionFanBuilder.AppendEdgeAngles(_angles, cone.Origin, _hitPoints, _edgeEpsilonDegrees);

            // Сортировка и склейка дублей обесценивают прежние дистанции,
            // поэтому веер после нормализации прогоняется заново.
            VisionPolygonBuilder.NormalizeAngles(_angles, _rayStepDegrees * 0.25f);
            RaycastAngles(cone, _angles, _distances, null);

            if (_angles.Count < 2)
            {
                return;
            }

            int originIndex = _vertices.Count;
            _vertices.Add(new Vector3(cone.Origin.x, cone.Origin.y, 0f));
            _colors.Add(Color.white);

            for (int i = 0; i < _angles.Count; i++)
            {
                Vector2 point = VisionPolygonBuilder.PointAt(
                    cone.Origin, _angles[i], _distances[i] < 0f ? cone.Radius : _distances[i]);

                _vertices.Add(new Vector3(point.x, point.y, 0f));
                _colors.Add(new Color(0.55f, 0.55f, 0.55f, 1f));
                _lastPoints.Add(point);
            }

            int firstRing = originIndex + 1;
            int ringCount = _angles.Count;

            for (int i = 0; i < ringCount - 1; i++)
            {
                _triangles.Add(originIndex);
                _triangles.Add(firstRing + i);
                _triangles.Add(firstRing + i + 1);
            }

            // Круговой обзор замыкается: последний луч и первый — соседи по кругу.
            if (cone.IsOmnidirectional)
            {
                _triangles.Add(originIndex);
                _triangles.Add(firstRing + ringCount - 1);
                _triangles.Add(firstRing);
            }
        }

        private void RaycastAngles(VisionCone cone, List<float> angles, List<float> distances,
                                   List<Vector2> hitPoints)
        {
            distances.Clear();

            for (int i = 0; i < angles.Count; i++)
            {
                float radians = angles[i] * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));

                // Берём ВСЕ попадания и выбираем ближайшее, которое не в самом
                // начале луча.
                //
                // Почему не обычный Physics2D.Raycast: он отдаёт только первое
                // препятствие. Если источник обзора прижат к стене и его
                // вершина оказалась внутри коллайдера, первым всегда будет
                // этот коллайдер, а не то, что за ним. Отбросив попадание,
                // мы потеряли бы всё, что стоит дальше, и обзор просвечивал
                // бы сквозь стену.
                //
                // Попытка «перезапустить луч за коллайдером» по его границам
                // выглядит разумно, но врёт: границы двумерного коллайдера
                // имеют нулевую толщину по Z, и пересечение с лучом в плоскости
                // XY считается неустойчиво. Проверка ловила это как «луч прошёл
                // сквозь настоящее препятствие».
                int count = Physics2D.RaycastNonAlloc(
                    cone.Origin, direction, _hitBuffer, cone.Radius, _occluderMask);

                int nearest = -1;

                for (int h = 0; h < count; h++)
                {
                    if (_hitBuffer[h].collider == null
                        || _hitBuffer[h].distance <= InsideColliderEpsilon)
                    {
                        continue;
                    }

                    if (nearest < 0 || _hitBuffer[h].distance < _hitBuffer[nearest].distance)
                    {
                        nearest = h;
                    }
                }

                if (nearest < 0)
                {
                    distances.Add(-1f);
                    continue;
                }

                distances.Add(_hitBuffer[nearest].distance);
                hitPoints?.Add(_hitBuffer[nearest].point);
            }
        }

        private void ApplyOverlayProperties()
        {
            if (_overlayMaterial == null || _maskTexture == null)
            {
                return;
            }

            _overlayMaterial.SetTexture(ShaderIds.MaskTex, _maskTexture);
            _overlayMaterial.SetFloat(ShaderIds.Darkness, _darkness);
        }

        /// <summary>Идентификаторы свойств шейдера в одном месте, без строк в кадре.</summary>
        private static class ShaderIds
        {
            public static readonly int MaskTex = Shader.PropertyToID("_MaskTex");
            public static readonly int Darkness = Shader.PropertyToID("_Darkness");
        }
    }
}
