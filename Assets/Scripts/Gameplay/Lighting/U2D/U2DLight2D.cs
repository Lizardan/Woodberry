using UnityEngine;
using UnityEngine.Rendering;

namespace Woodberry.Lighting.U2D
{
    /// <summary>Тип источника света.</summary>
    public enum U2DLightType
    {
        /// <summary>Процедурный: затухание считается по расстоянию от центра квада.</summary>
        Analytical,

        /// <summary>Текстурированный: яркость берётся из cookie-текстуры.</summary>
        Textured,
    }

    /// <summary>
    /// Источник 2D-света в духе SardineFish/Unity2DLighting.
    /// Квад растянут на <c>2 * LightDistance</c>, поэтому UV идут 0..1,
    /// а центру соответствует (0.5, 0.5) — оттуда шейдер считает расстояние.
    /// </summary>
    [AddComponentMenu("Woodberry/Lighting/U2D Light 2D")]
    public class U2DLight2D : U2DLight2DBase
    {
        [Header("Форма света")]
        public U2DLightType LightType = U2DLightType.Analytical;

        [Tooltip("Крутизна затухания. -1 — без спада (глухой диск), 0 — линейное, ближе к 1 — резкая граница.")]
        [Range(-1f, 1f)]
        public float Attenuation;

        public Color LightColor = Color.white;

        [Min(0f)]
        public float Intensity = 1f;

        [Tooltip("Только для текстурированного света.")]
        public Texture LightTexture;

        private Mesh _quadMesh;
        private Material _lightMaterial;
        private MaterialPropertyBlock _properties;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int LightCookieId = Shader.PropertyToID("_LightCookie");
        private static readonly int ShadowMapId = Shader.PropertyToID("_ShadowMap");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int AttenuationId = Shader.PropertyToID("_Attenuation");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int LightPosId = Shader.PropertyToID("_2DLightPos");
        private static readonly int LightRangeId = Shader.PropertyToID("_LightRange");

        private Material LightMaterial
        {
            get
            {
                if (_lightMaterial == null)
                {
                    _lightMaterial = U2DShaderPool.Get("Woodberry/U2DLight");
                }
                return _lightMaterial;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureQuad();
        }

        private void EnsureQuad()
        {
            if (_quadMesh != null)
            {
                return;
            }

            _quadMesh = new Mesh { name = "U2D Light Quad" };

            // Квад строится в единичном масштабе, а размер задаётся через TRS
            // на DrawMesh, поэтому менять вершины каждый кадр не нужно.
            _quadMesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(-1f, 1f, 0f),
                new Vector3(1f, 1f, 0f),
            };

            _quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };

            _quadMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };

            _quadMesh.RecalculateNormals();
            _quadMesh.MarkDynamic();
        }

        protected override void OnDestroy()
        {
            // Сначала чистим собственный квад, затем общий список из базы.
            if (_quadMesh != null)
            {
                Destroy(_quadMesh);
                _quadMesh = null;
            }
            base.OnDestroy();
        }

        public override void RenderLight(UnsafeCommandBuffer cmd, ref U2DLightRenderingData data)
        {
            Material material = LightMaterial;
            if (material == null || _quadMesh == null)
            {
                return;
            }

            if (_properties == null)
            {
                _properties = new MaterialPropertyBlock();
            }

            // Свойства источника передаём через MaterialPropertyBlock, а не через
            // глобалы: так несколько источников не затирают параметры друг друга,
            // и DrawMesh применяет их к конкретному драву.
            // Cookie может быть не назначен — тогда кладём белую текстуру, иначе
            // MaterialPropertyBlock.SetTexture(null) бросает на стороне рендера.
            Texture cookie = LightTexture != null ? LightTexture : Texture2D.whiteTexture;
            _properties.SetTexture(MainTexId, cookie);
            _properties.SetTexture(LightCookieId, cookie);
            _properties.SetColor(ColorId, LightColor);
            _properties.SetFloat(AttenuationId, Attenuation);
            _properties.SetFloat(IntensityId, Intensity);
            _properties.SetVector(LightPosId, transform.position);
            _properties.SetFloat(LightRangeId, LightDistance);

            // Карта теней — общая для всех источников кадра, поэтому её кладём глобально.
            cmd.SetGlobalTexture(ShadowMapId, data.ShadowMap);

            // Масштаб несёт радиус: шейдер ждёт UV в диапазоне 0..1.
            Matrix4x4 trs = Matrix4x4.TRS(
                transform.position,
                transform.rotation,
                new Vector3(LightDistance, LightDistance, 1f));

            int pass = LightType == U2DLightType.Analytical ? 0 : 1;
            cmd.DrawMesh(_quadMesh, trs, material, 0, pass, _properties);
        }
    }
}
