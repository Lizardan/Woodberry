using UnityEngine;

namespace Woodberry.Lighting.U2D
{
    /// <summary>
    /// Векторная математика для построения теневых объёмов.
    /// Порт <c>MathUtility</c> из SardineFish/Unity2DLighting (взято только то,
    /// что реально используется портом).
    /// </summary>
    public static class U2DMathUtility
    {
        public static Vector3 ToVector3(this Vector2 v)
        {
            return new Vector3(v.x, v.y, 0f);
        }

        public static Vector2 ToVector2(this Vector3 v)
        {
            return new Vector2(v.x, v.y);
        }

        /// <summary>Поворот вектора на угол в радианах.</summary>
        public static Vector2 Rotate(Vector2 v, float rad)
        {
            float rx = Mathf.Cos(rad);
            float ry = Mathf.Sin(rad);
            return new Vector2(rx * v.x - ry * v.y, rx * v.y + ry * v.x);
        }
    }
}
