using UnityEngine;

namespace Woodberry.Lighting.U2D
{
    /// <summary>
    /// Пул материалов «шейдер → материал». Порт <c>ShaderPool</c> из
    /// SardineFish/Unity2DLighting. Материалы создаются через
    /// <see cref="Shader.Find(string)"/> и живут до конца сессии.
    /// </summary>
    public static class U2DShaderPool
    {
        private static readonly System.Collections.Generic.Dictionary<string, Material> Pool =
            new System.Collections.Generic.Dictionary<string, Material>();

        public static Material Get(string shaderName)
        {
            if (Pool.TryGetValue(shaderName, out Material material) && material != null)
            {
                return material;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[U2D] Шейдер не найден: {shaderName}. Проверь, что он лежит в билде (Always Included Shaders или материал в Resources).");
                return null;
            }

            material = new Material(shader) { hideFlags = HideFlags.DontSave };
            Pool[shaderName] = material;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pool.Clear();
        }
    }
}
