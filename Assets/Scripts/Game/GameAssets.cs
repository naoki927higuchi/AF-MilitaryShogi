using System.Collections.Generic;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Runtime access to generated resources (Assets/Generated/Resources). Materials are
    /// cloned from two template assets so the shaders are guaranteed to be in the build:
    /// Materials/Lit (Standard) and Materials/Overlay (Sprites/Default, alpha blended, unlit).
    /// </summary>
    public static class GameAssets
    {
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private static Material litTemplate, overlayTemplate;
        private static Font uiFont;

        public static Texture2D Texture(string path)
        {
            Texture2D t;
            if (!textures.TryGetValue(path, out t))
            {
                t = Resources.Load<Texture2D>("Textures/" + path);
                if (t == null) Debug.LogError("Missing texture Resources/Textures/" + path);
                textures[path] = t;
            }
            return t;
        }

        /// <summary>Front face texture. Only ever requested for the viewer's own pieces.</summary>
        public static Texture2D Face(PieceType type) { return Texture("Pieces/piece_" + type); }
        public static Texture2D Back { get { return Texture("Pieces/piece_back"); } }
        public static Texture2D Side { get { return Texture("Pieces/piece_side"); } }

        public static Material Lit(Texture texture, Color tint, float smoothness = 0.2f)
        {
            if (litTemplate == null) litTemplate = Resources.Load<Material>("Materials/Lit");
            var m = new Material(litTemplate);
            m.mainTexture = texture;
            m.color = tint;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        /// <param name="layer">Draw order among overlays lying on the same plane (higher = on top).</param>
        public static Material Overlay(Color color, Texture texture = null, int layer = 0)
        {
            if (overlayTemplate == null) overlayTemplate = Resources.Load<Material>("Materials/Overlay");
            var m = new Material(overlayTemplate);
            m.mainTexture = texture != null ? texture : Texture2D.whiteTexture;
            m.color = color;
            m.renderQueue = 3000 + layer;
            return m;
        }

        public static Font UiFont
        {
            get
            {
                if (uiFont == null)
                    uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic", "MS Gothic" }, 16);
                return uiFont;
            }
        }
    }

    /// <summary>Small procedural mesh helpers.</summary>
    public static class MeshKit
    {
        public static GameObject Quad(string name, Transform parent, Vector3 center, float width, float depth, float yaw, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var mesh = new Mesh();
            float w = width / 2, d = depth / 2;
            mesh.vertices = new[] { new Vector3(-w, 0, -d), new Vector3(-w, 0, d), new Vector3(w, 0, d), new Vector3(w, 0, -d) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A flat strip between two points on the XZ plane.</summary>
        public static GameObject Line(string name, Transform parent, Vector3 a, Vector3 b, float width, float y, Material mat)
        {
            Vector3 mid = (a + b) / 2f;
            Vector3 dir = b - a;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            return Quad(name, parent, new Vector3(mid.x, y, mid.z), width, dir.magnitude, yaw, mat);
        }

        public static GameObject Disc(string name, Transform parent, Vector3 center, float innerRadius, float outerRadius, Material mat, int segments = 48)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.Add(dir * innerRadius); uvs.Add(new Vector2(0.5f + dir.x * 0.5f * innerRadius / outerRadius, 0.5f + dir.z * 0.5f * innerRadius / outerRadius));
                verts.Add(dir * outerRadius); uvs.Add(new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.z * 0.5f));
                if (i < segments)
                {
                    int k = i * 2;
                    tris.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            // Wind so the face points up.
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
            mesh.triangles = t;
            mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Axis-aligned box with per-face UVs (top face UV spans the whole top).</summary>
        public static Mesh Box(Vector3 size)
        {
            float x = size.x / 2, y = size.y / 2, z = size.z / 2;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var top = new List<int>();
            var sides = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<int> tri, float uScale, float vScale)
            {
                int i = v.Count;
                v.AddRange(new[] { a, b, c, d });
                uv.AddRange(new[] { new Vector2(0, 0), new Vector2(0, vScale), new Vector2(uScale, vScale), new Vector2(uScale, 0) });
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Face(new Vector3(-x, y, -z), new Vector3(-x, y, z), new Vector3(x, y, z), new Vector3(x, y, -z), top, 1, 1);
            Face(new Vector3(-x, -y, -z), new Vector3(-x, y, -z), new Vector3(x, y, -z), new Vector3(x, -y, -z), sides, 1, 0.05f);
            Face(new Vector3(x, -y, z), new Vector3(x, y, z), new Vector3(-x, y, z), new Vector3(-x, -y, z), sides, 1, 0.05f);
            Face(new Vector3(-x, -y, z), new Vector3(-x, y, z), new Vector3(-x, y, -z), new Vector3(-x, -y, -z), sides, 1, 0.05f);
            Face(new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(x, y, z), new Vector3(x, -y, z), sides, 1, 0.05f);
            Face(new Vector3(-x, -y, z), new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, -y, z), sides, 1, 1);
            var mesh = new Mesh { subMeshCount = 2 };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(top, 0);
            mesh.SetTriangles(sides, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
