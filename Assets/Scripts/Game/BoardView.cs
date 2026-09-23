using System.Collections.Generic;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    public enum HighlightKind { None, Selected, Move, Attack, Placement, LastMove }

    /// <summary>
    /// Builds the board geometrically: thick wooden slab (provided dark wood texture),
    /// engraved grid lines per camp, the no-man's-land band, the two X gateways with
    /// their centre circles, and the headquarters frames. No board image is used for
    /// any of the markings.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private const float LineY = 0.003f;
        private readonly Dictionary<int, GameObject> highlights = new Dictionary<int, GameObject>();
        private readonly Dictionary<HighlightKind, Material> highlightMats = new Dictionary<HighlightKind, Material>();

        public void Build()
        {
            var t = transform;
            // Table.
            var table = new GameObject("Table");
            table.transform.SetParent(t, false);
            table.transform.localPosition = new Vector3(0, -BoardLayout.BoardThickness - 0.01f, 0);
            table.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(60, 0.02f, 60));
            var tableMat = GameAssets.Lit(GameAssets.Texture("Board/table_wood"), new Color(0.55f, 0.5f, 0.46f), 0.15f);
            tableMat.mainTextureScale = new Vector2(6, 6);
            table.AddComponent<MeshRenderer>().sharedMaterials = new[] { tableMat, tableMat };

            // Board slab.
            var slab = new GameObject("BoardSlab");
            slab.transform.SetParent(t, false);
            slab.transform.localPosition = new Vector3(0, -BoardLayout.BoardThickness / 2f, 0);
            slab.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(BoardLayout.Width, BoardLayout.BoardThickness, BoardLayout.Depth));
            var wood = GameAssets.Texture("Board/board_wood");
            var topMat = GameAssets.Lit(wood, new Color(1.08f, 1.0f, 0.95f), 0.32f);
            var sideMat = GameAssets.Lit(wood, new Color(0.62f, 0.55f, 0.5f), 0.25f);
            var slabRenderer = slab.AddComponent<MeshRenderer>();
            slabRenderer.sharedMaterials = new[] { topMat, sideMat };
            slabRenderer.receiveShadows = true;

            var groove = GameAssets.Overlay(new Color(0.06f, 0.035f, 0.02f, 0.85f), null, 3);
            var inlay = GameAssets.Overlay(new Color(0.93f, 0.78f, 0.55f, 0.55f), null, 5);
            var shade = GameAssets.Overlay(new Color(0f, 0f, 0f, 0.28f), null, 1);
            var road = GameAssets.Overlay(new Color(0.95f, 0.8f, 0.58f, 0.13f), null, 2);

            // Camp grids.
            foreach (var side in new[] { Side.South, Side.North })
            {
                float z0 = side == Side.South ? BoardLayout.ZOfRow(0) - 0.5f : BoardLayout.ZOfRow(4) - 0.5f;
                float z1 = z0 + BoardGraph.CampRows * BoardLayout.Cell;
                for (int i = 0; i <= BoardGraph.Columns; i++)
                {
                    float x = BoardLayout.XOf(0) - 0.5f + i;
                    float w = i == 0 || i == BoardGraph.Columns ? 0.06f : 0.035f;
                    MeshKit.Line("GridV", t, new Vector3(x, 0, z0 - 0.02f), new Vector3(x, 0, z1 + 0.02f), w, LineY, groove);
                }
                for (int j = 0; j <= BoardGraph.CampRows; j++)
                {
                    float z = z0 + j;
                    float w = j == 0 || j == BoardGraph.CampRows ? 0.06f : 0.035f;
                    MeshKit.Line("GridH", t, new Vector3(BoardLayout.XOf(0) - 0.53f, 0, z), new Vector3(BoardLayout.XOf(7) + 0.53f, 0, z), w, LineY, groove);
                }
                // Headquarters: framed two-cell area on the back row.
                int[] hq = BoardGraph.Headquarters(side);
                Vector3 a = BoardLayout.Node(hq[0]), b = BoardLayout.Node(hq[1]);
                Vector3 c = (a + b) / 2f;
                MeshKit.Quad("HQ_Fill", t, new Vector3(c.x, LineY + 0.0005f, c.z), 1.86f, 0.86f, 0, shade);
                float hx = 0.93f, hz = 0.43f;
                MeshKit.Line("HQ_Frame", t, new Vector3(c.x - hx, 0, c.z - hz), new Vector3(c.x + hx, 0, c.z - hz), 0.03f, LineY + 0.001f, inlay);
                MeshKit.Line("HQ_Frame", t, new Vector3(c.x - hx, 0, c.z + hz), new Vector3(c.x + hx, 0, c.z + hz), 0.03f, LineY + 0.001f, inlay);
                MeshKit.Line("HQ_Frame", t, new Vector3(c.x - hx, 0, c.z - hz), new Vector3(c.x - hx, 0, c.z + hz), 0.03f, LineY + 0.001f, inlay);
                MeshKit.Line("HQ_Frame", t, new Vector3(c.x + hx, 0, c.z - hz), new Vector3(c.x + hx, 0, c.z + hz), 0.03f, LineY + 0.001f, inlay);
                // Label in the margin behind the HQ, readable from its owner's seat.
                var label = GameAssets.Texture("Board/label_hq");
                float labelZ = side == Side.South ? c.z - 0.5f - BoardLayout.Margin / 2f : c.z + 0.5f + BoardLayout.Margin / 2f;
                float lw = 1.25f, lh = lw * label.height / label.width;
                MeshKit.Quad("HQ_Label", t, new Vector3(c.x, LineY, labelZ), lw, lh, side == Side.South ? 0 : 180, GameAssets.Overlay(new Color(1, 1, 1, 0.8f), label, 4));
            }

            // Band between camps.
            float bandHalf = BoardLayout.Band / 2f;
            MeshKit.Quad("Band", t, new Vector3(0, LineY - 0.001f, 0), BoardGraph.Columns + 1.06f, BoardLayout.Band - 0.06f, 0, GameAssets.Overlay(new Color(0f, 0f, 0f, 0.22f), null, 0));

            // X gateways: two engraved diagonal roads per gateway, crossing at the centre circle.
            foreach (int crossing in new[] { BoardGraph.LeftCrossing, BoardGraph.RightCrossing })
            {
                int left = crossing == BoardGraph.LeftCrossing ? 0 : 5, right = crossing == BoardGraph.LeftCrossing ? 2 : 7;
                var sL = new Vector3(BoardLayout.XOf(left), 0, -bandHalf);
                var sR = new Vector3(BoardLayout.XOf(right), 0, -bandHalf);
                var nL = new Vector3(BoardLayout.XOf(left), 0, bandHalf);
                var nR = new Vector3(BoardLayout.XOf(right), 0, bandHalf);
                foreach (var pair in new[] { new[] { sL, nR }, new[] { sR, nL } })
                {
                    MeshKit.Line("GateRoad", t, pair[0], pair[1], 0.46f, LineY + 0.0002f, road);
                    Vector3 dir = (pair[1] - pair[0]).normalized;
                    Vector3 side = new Vector3(dir.z, 0, -dir.x) * 0.23f;
                    MeshKit.Line("GateEdge", t, pair[0] + side, pair[1] + side, 0.03f, LineY + 0.0004f, groove);
                    MeshKit.Line("GateEdge", t, pair[0] - side, pair[1] - side, 0.03f, LineY + 0.0004f, groove);
                }
                Vector3 centre = BoardLayout.Node(crossing);
                MeshKit.Disc("CentreFill", t, new Vector3(centre.x, LineY + 0.0006f, centre.z), 0f, 0.44f, GameAssets.Overlay(new Color(0.12f, 0.07f, 0.04f, 0.9f), null, 4));
                MeshKit.Disc("CentreRing", t, new Vector3(centre.x, LineY + 0.0008f, centre.z), 0.44f, 0.48f, inlay);
                MeshKit.Disc("CentreDot", t, new Vector3(centre.x, LineY + 0.0008f, centre.z), 0f, 0.035f, inlay, 16);
            }

            // Highlights (hidden by default).
            highlightMats[HighlightKind.Selected] = GameAssets.Overlay(new Color(1f, 0.85f, 0.35f, 0.55f), null, 10);
            highlightMats[HighlightKind.Move] = GameAssets.Overlay(new Color(0.45f, 0.95f, 0.55f, 0.42f), null, 10);
            highlightMats[HighlightKind.Attack] = GameAssets.Overlay(new Color(1f, 0.3f, 0.22f, 0.5f), null, 10);
            highlightMats[HighlightKind.Placement] = GameAssets.Overlay(new Color(0.4f, 0.75f, 1f, 0.38f), null, 10);
            highlightMats[HighlightKind.LastMove] = GameAssets.Overlay(new Color(1f, 1f, 1f, 0.16f), null, 9);
            for (int n = 0; n < BoardGraph.NodeCount; n++)
            {
                Vector3 p = BoardLayout.Node(n);
                var go = BoardGraph.IsCrossing(n)
                    ? MeshKit.Disc("Hl" + n, t, new Vector3(p.x, LineY + 0.0012f, p.z), 0f, 0.42f, highlightMats[HighlightKind.Move])
                    : MeshKit.Quad("Hl" + n, t, new Vector3(p.x, LineY + 0.0012f, p.z), 0.9f, 0.9f, 0, highlightMats[HighlightKind.Move]);
                go.SetActive(false);
                highlights[n] = go;
            }
        }

        public void ClearHighlights()
        {
            foreach (var h in highlights.Values) h.SetActive(false);
        }

        public void Highlight(int node, HighlightKind kind)
        {
            GameObject go;
            if (!highlights.TryGetValue(node, out go)) return;
            if (kind == HighlightKind.None) { go.SetActive(false); return; }
            go.GetComponent<MeshRenderer>().sharedMaterial = highlightMats[kind];
            // Attack targets are occupied: draw the marker over the piece so it stays visible.
            var p = go.transform.localPosition;
            go.transform.localPosition = new Vector3(p.x, kind == HighlightKind.Attack ? 0.2f : LineY + 0.0012f, p.z);
            go.SetActive(true);
        }
    }
}
