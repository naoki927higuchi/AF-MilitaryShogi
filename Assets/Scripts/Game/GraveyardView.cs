using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Removed pieces lying flat on the table beside the board (play mode).
    /// Left: 自軍の損失 – faces shown, sorted by kind (大将…少尉, then special pieces), same kinds adjacent.
    /// Right: 敵軍の損失 – all show the common back, in the order they were removed. The slot of an
    /// enemy piece is a function of its death index only; no kind is ever passed to this class for
    /// enemy pieces, so the layout cannot reveal anything but the count.
    /// Both sides use the same grid: 3 columns x up to 11 rows, filled from the top-left to the right,
    /// and the same physical placement: lying on the table surface (not floating at board height),
    /// same orientation, scale and shadow settings. Only the face shown and the ordering differ.
    /// </summary>
    public sealed class GraveyardView : MonoBehaviour
    {
        public const int Columns = 3;
        public const int Rows = 11;
        public const float Scale = 0.86f;
        // Column/row pitch, distance of the first column from the board edge, first row, heading gap.
        private const float Dx = 0.76f, Dz = 0.84f, Gap = 0.5f, TopZ = 4.3f, HeadingGap = 1.35f;
        /// <summary>Loss pieces rest on the table, whose surface is one board thickness below the board top.</summary>
        public static float TableY { get { return -BoardLayout.BoardThickness; } }

        private readonly List<PieceView> own = new List<PieceView>();
        private readonly List<PieceView> enemy = new List<PieceView>();
        private readonly List<int> enemyIds = new List<int>();

        public IReadOnlyList<PieceView> OwnViews { get { return own; } }
        public IReadOnlyList<PieceView> EnemyViews { get { return enemy; } }
        /// <summary>Enemy ids in the order shown (= removal order). Only used by the auto-test to compare with history.</summary>
        public IReadOnlyList<int> EnemyIdsShown { get { return enemyIds; } }

        public static Vector3 Slot(int index, bool enemySide)
        {
            int col = index % Columns, row = index / Columns;
            float halfBoard = BoardLayout.Width / 2f;
            float left = enemySide ? halfBoard + Gap : -halfBoard - Gap - (Columns - 1) * Dx;
            return new Vector3(left + col * Dx, TableY, TopZ - row * Dz);
        }

        /// <summary>World point above a block's first row, for the small heading.</summary>
        public static Vector3 HeadingAnchor(bool enemySide)
        {
            var a = Slot(0, enemySide);
            var b = Slot(Columns - 1, enemySide);
            return new Vector3((a.x + b.x) / 2f, TableY, TopZ + HeadingGap);
        }

        /// <summary>Outer corners of both blocks (for fitting the camera).</summary>
        public static IEnumerable<Vector3> Corners()
        {
            foreach (bool e in new[] { false, true })
            {
                var first = Slot(0, e);
                var last = Slot(Columns * Rows - 1, e);
                foreach (float x in new[] { first.x - 0.45f, last.x + 0.45f })
                    foreach (float z in new[] { TopZ + HeadingGap + 0.45f, last.z - 0.5f })
                        foreach (float y in new[] { TableY, TableY + PieceMeshFactory.BaseHeight * Scale })
                            yield return new Vector3(x, y, z);
            }
        }

        public void Clear()
        {
            foreach (var v in own.Concat(enemy)) Destroy(v.gameObject);
            own.Clear();
            enemy.Clear();
            enemyIds.Clear();
        }

        public void Sync(PlayerView view)
        {
            // Own losses: kinds are the viewer's own knowledge.
            var lost = view.Own.Where(p => !p.Alive).OrderBy(p => (int)p.Type).ThenBy(p => p.Number).ToList();
            if (lost.Count != own.Count || lost.Where((p, i) => own[i].Id != p.Id).Any())
            {
                foreach (var v in own) Destroy(v.gameObject);
                own.Clear();
                for (int i = 0; i < lost.Count; i++)
                {
                    var v = PieceView.CreateOwn(transform, lost[i].Id, lost[i].Number, lost[i].Type, true);
                    Lay(v, Slot(i, false));
                    own.Add(v);
                }
            }
            // Enemy losses: appended in removal order, always the back texture.
            var order = GameSession.EnemyDeathOrder(view);
            for (int i = enemy.Count; i < order.Count; i++)
            {
                var v = PieceView.CreateEnemy(transform, order[i], view.EnemyById(order[i]).Number, true);   // same orientation as the own side
                Lay(v, Slot(i, true));
                enemy.Add(v);
                enemyIds.Add(order[i]);
            }
        }

        private static void Lay(PieceView v, Vector3 at)
        {
            v.Node = -1;
            v.transform.localPosition = at;
            v.transform.localRotation = Quaternion.identity;
            v.transform.localScale = Vector3.one * Scale;
            v.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            v.Renderer.receiveShadows = true;
            v.gameObject.SetActive(true);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
