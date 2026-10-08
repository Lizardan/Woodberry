using System;
using UnityEngine;

namespace Woodberry.Lighting.U2D
{
    /// <summary>
    /// Построитель меша из разрозненных кусков геометрии.
    /// Порт <c>MeshBuilder</c> из SardineFish/Unity2DLighting: массивы растут по
    /// мере добавления, UV1 и UV2 несут рёбра теневого объёма.
    /// </summary>
    public class U2DMeshBuilder
    {
        public Vector3[] Vertices;
        public int[] Triangles;
        public Vector2[] UV1;
        public Vector2[] UV2;

        private int _vertexCount;
        private int _triangleCount;

        public U2DMeshBuilder() : this(0, 0)
        {
        }

        public U2DMeshBuilder(int vertCount, int triangleCount)
        {
            Vertices = new Vector3[vertCount];
            UV1 = new Vector2[vertCount];
            UV2 = new Vector2[vertCount];
            Triangles = new int[triangleCount * 3];
        }

        private void ResizeVerts(int vertCount)
        {
            Array.Resize(ref Vertices, vertCount);
            Array.Resize(ref UV1, vertCount);
            Array.Resize(ref UV2, vertCount);
        }

        public void AddVertsAndTriangles(Vector3[] vertices, int[] triangles, Vector2[] uv1, Vector2[] uv2)
        {
            if (vertices.Length + _vertexCount > Vertices.Length)
            {
                // Растём с запасом, чтобы не резайзить массив на каждый чанк.
                ResizeVerts(Mathf.Max(vertices.Length + _vertexCount, Vertices.Length * 2 + 64));
            }

            if (triangles.Length + _triangleCount > Triangles.Length)
            {
                Array.Resize(ref Triangles, Mathf.Max(triangles.Length + _triangleCount, Triangles.Length * 2 + 192));
            }

            int offset = _vertexCount;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vertices[offset + i] = vertices[i];
                UV1[offset + i] = uv1[i];
                UV2[offset + i] = uv2[i];
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                Triangles[_triangleCount + i] = triangles[i] + offset;
            }

            _vertexCount += vertices.Length;
            _triangleCount += triangles.Length;
        }

        public void AddCopiedMesh(Mesh mesh)
        {
            AddVertsAndTriangles(mesh.vertices, mesh.triangles, mesh.uv, mesh.uv2);
        }

        public Mesh ToMesh(Mesh mesh)
        {
            mesh.Clear();

            // Массивы выделены с запасом — обрезаем до фактического заполнения,
            // иначе в меш попадут нулевые вершины в начале координат.
            Vector3[] verts = new Vector3[_vertexCount];
            Vector2[] uv1 = new Vector2[_vertexCount];
            Vector2[] uv2 = new Vector2[_vertexCount];
            int[] tris = new int[_triangleCount];

            Array.Copy(Vertices, verts, _vertexCount);
            Array.Copy(UV1, uv1, _vertexCount);
            Array.Copy(UV2, uv2, _vertexCount);
            Array.Copy(Triangles, tris, _triangleCount);

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.uv = uv1;
            mesh.uv2 = uv2;
            mesh.RecalculateNormals();
            return mesh;
        }

        public Mesh ToMesh()
        {
            return ToMesh(new Mesh());
        }
    }
}
