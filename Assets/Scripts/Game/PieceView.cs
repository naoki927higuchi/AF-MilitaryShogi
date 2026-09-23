using System.Collections;
using System.Collections.Generic;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>Builds the shogi-style wedge piece mesh from the same outline the texture generator uses.</summary>
    public static class PieceMeshFactory
    {
        // Outline in normalized face coordinates (u right, v down from the tip).
        // Must match OUTLINE in Tools/TextureGen/generate_textures.py.
        private static readonly Vector2[] outline =
        {
            new Vector2(0.5f, 0f), new Vector2(0.856f, 0.137f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0.144f, 0.137f),
        };

        public const float Width = 0.78f;
        public const float Length = 0.87f;
        public const float BaseHeight = 0.16f;
        public const float TipHeight = 0.10f;

        private static Mesh mesh;

        /// <summary>Submesh 0 = top face (face texture), submesh 1 = sides and bottom (plain wood).</summary>
        public static Mesh Mesh
        {
            get
            {
                if (mesh == null) mesh = Build();
                return mesh;
            }
        }

        private static Vector3 Local(Vector2 uv, float y) { return new Vector3((uv.x - 0.5f) * Width, y, (0.5f - uv.y) * Length); }
        private static float HeightAt(float v) { return Mathf.Lerp(TipHeight, BaseHeight, v); }

        private static Mesh Build()
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var top = new List<int>();
            var rest = new List<int>();

            // Top face: fan from the centroid.
            Vector2 centroid = Vector2.zero;
            foreach (var p in outline) centroid += p;
            centroid /= outline.Length;
            int c = verts.Count;
            verts.Add(Local(centroid, HeightAt(centroid.y))); uvs.Add(new Vector2(centroid.x, 1 - centroid.y));
            foreach (var p in outline) { verts.Add(Local(p, HeightAt(p.y))); uvs.Add(new Vector2(p.x, 1 - p.y)); }
            for (int i = 0; i < outline.Length; i++)
            {
                int a = c + 1 + i, b = c + 1 + (i + 1) % outline.Length;
                top.AddRange(new[] { c, a, b });
            }

            // Sides.
            float run = 0f;
            for (int i = 0; i < outline.Length; i++)
            {
                var p0 = outline[i];
                var p1 = outline[(i + 1) % outline.Length];
                int s = verts.Count;
                float len = Vector2.Distance(p0, p1);
                verts.Add(Local(p0, 0)); verts.Add(Local(p0, HeightAt(p0.y))); verts.Add(Local(p1, HeightAt(p1.y))); verts.Add(Local(p1, 0));
                uvs.Add(new Vector2(run, 0)); uvs.Add(new Vector2(run, 0.25f)); uvs.Add(new Vector2(run + len, 0.25f)); uvs.Add(new Vector2(run + len, 0));
                run += len;
                rest.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }

            // Bottom.
            int bc = verts.Count;
            verts.Add(Local(centroid, 0)); uvs.Add(centroid);
            foreach (var p in outline) { verts.Add(Local(p, 0)); uvs.Add(p); }
            for (int i = 0; i < outline.Length; i++)
            {
                int a = bc + 1 + i, b = bc + 1 + (i + 1) % outline.Length;
                rest.AddRange(new[] { bc, b, a });
            }

            var m = new Mesh { name = "ShogiPiece", subMeshCount = 2 };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(top, 0);
            m.SetTriangles(rest, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();
            // Guarantee the top faces up regardless of outline orientation.
            var n = m.normals;
            if (n[0].y < 0)
            {
                for (int sub = 0; sub < 2; sub++)
                {
                    var t = m.GetTriangles(sub);
                    for (int i = 0; i < t.Length; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }
                    m.SetTriangles(t, sub);
                }
                m.RecalculateNormals();
            }
            return m;
        }
    }

    /// <summary>
    /// A piece on screen. Own pieces get their face texture. Enemy pieces are created with
    /// the common back texture on every surface and are never told their kind: there is no
    /// code path that could put an enemy's face on screen.
    /// </summary>
    public sealed class PieceView : MonoBehaviour
    {
        public int Id { get; private set; }
        public int Number { get; private set; }
        public bool IsOwn { get; private set; }
        public int Node { get; set; }
        private MeshRenderer meshRenderer;
        private static Material backMaterial, sideMaterial;
        private static readonly Dictionary<PieceType, Material> faceMaterials = new Dictionary<PieceType, Material>();

        // 1.1.0: low gloss so the key light's specular does not wash out the black ink.
        private const float FaceGloss = 0.1f;

        private static Material SideMaterial
        {
            get { return sideMaterial ?? (sideMaterial = GameAssets.Lit(GameAssets.Side, new Color(0.95f, 0.9f, 0.85f), 0.3f)); }
        }

        public static Material BackMaterial
        {
            get { return backMaterial ?? (backMaterial = GameAssets.Lit(GameAssets.Back, Color.white, FaceGloss)); }
        }

        private static Material Face(PieceType type)
        {
            Material m;
            if (!faceMaterials.TryGetValue(type, out m)) faceMaterials[type] = m = GameAssets.Lit(GameAssets.Face(type), Color.white, FaceGloss);
            return m;
        }

        public static PieceView CreateOwn(Transform parent, int id, int number, PieceType type, bool facingNorth)
        {
            var v = Create(parent, "Own#" + number, id, number, true, facingNorth);
            v.meshRenderer.sharedMaterials = new[] { Face(type), SideMaterial };
            return v;
        }

        public static PieceView CreateEnemy(Transform parent, int id, int number, bool facingNorth)
        {
            var v = Create(parent, "Enemy#" + number, id, number, false, facingNorth);
            v.meshRenderer.sharedMaterials = new[] { BackMaterial, SideMaterial };
            return v;
        }

        /// <summary>Change the face of an own piece (placement editing swaps kinds between views).</summary>
        public void SetOwnKind(PieceType type)
        {
            if (!IsOwn) throw new System.InvalidOperationException("Enemy pieces never show a face.");
            meshRenderer.sharedMaterials = new[] { Face(type), SideMaterial };
        }

        private static PieceView Create(Transform parent, string name, int id, int number, bool own, bool facingNorth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(0, facingNorth ? 0 : 180, 0);
            go.AddComponent<MeshFilter>().sharedMesh = PieceMeshFactory.Mesh;
            var v = go.AddComponent<PieceView>();
            v.meshRenderer = go.AddComponent<MeshRenderer>();
            v.meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            v.Id = id;
            v.Number = number;
            v.IsOwn = own;
            v.Node = -1;
            return v;
        }

        public Bounds WorldBounds { get { return meshRenderer.bounds; } }

        public Material[] Materials { get { return meshRenderer.sharedMaterials; } }

        public void PlaceAt(int node)
        {
            Node = node;
            transform.localPosition = BoardLayout.Node(node);
            transform.localScale = Vector3.one;
            gameObject.SetActive(true);
        }

        public void SetLifted(bool lifted)
        {
            var p = transform.localPosition;
            transform.localPosition = new Vector3(p.x, lifted ? 0.12f : 0f, p.z);
        }

        // ------------------------------------------------------------------
        // Animation (identical for every piece kind)
        // ------------------------------------------------------------------

        public IEnumerator MoveAlong(IList<Vector3> points, float secondsPerStep, bool arc)
        {
            Vector3 start = transform.localPosition;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = i == 0 ? start : points[i - 1];
                Vector3 b = points[i];
                float t = 0;
                while (t < 1f)
                {
                    t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.01f, secondsPerStep));
                    float e = Mathf.SmoothStep(0, 1, t);
                    Vector3 p = Vector3.Lerp(a, b, e);
                    p.y = arc ? Mathf.Sin(e * Mathf.PI) * 0.35f : Mathf.Sin(e * Mathf.PI) * 0.06f;
                    transform.localPosition = p;
                    yield return null;
                }
            }
        }

        public IEnumerator Shake(float seconds, float amplitude)
        {
            Vector3 basePos = transform.localPosition;
            float t = 0;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = 1f - t / seconds;
                transform.localPosition = basePos + new Vector3(Mathf.Sin(t * 55f) * amplitude * k, 0.05f * k, Mathf.Cos(t * 47f) * amplitude * k);
                yield return null;
            }
            transform.localPosition = basePos;
        }

        /// <summary>Loser leaves the board: sinks and shrinks. Same for every kind.</summary>
        public IEnumerator Defeat(float seconds)
        {
            Vector3 p0 = transform.localPosition;
            float t = 0;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / seconds);
                transform.localPosition = p0 + new Vector3(0, -0.2f * k, 0);
                transform.localScale = Vector3.one * (1f - 0.6f * k);
                yield return null;
            }
            gameObject.SetActive(false);
        }
    }
}
