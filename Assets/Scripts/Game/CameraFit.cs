using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Fixed high-angle perspective camera from the player's side. Pitch and field of view never
    /// change. The board – plus, when shown, both loss areas and their headings – is treated as one
    /// visual group: the camera distance is the smallest at which the whole group fits the available
    /// play area, and the image is then shifted (lens shift, perspective unchanged) so the group sits
    /// in the vertical centre of that area. The group always reserves space for 31 losses per side,
    /// so the board does not move as pieces are lost.
    /// </summary>
    public static class CameraFit
    {
        public const float Pitch = 54f;
        private static readonly Vector3 target = new Vector3(0f, 0f, -0.35f);

        public static IEnumerable<Vector3> BoardCorners()
        {
            float hw = BoardLayout.Width / 2f + 0.15f, hd = BoardLayout.Depth / 2f + 0.15f;
            yield return new Vector3(-hw, 0, -hd);
            yield return new Vector3(hw, 0, -hd);
            yield return new Vector3(-hw, 0, hd);
            yield return new Vector3(hw, 0, hd);
            yield return new Vector3(-hw, -BoardLayout.BoardThickness, -hd);
            yield return new Vector3(hw, -BoardLayout.BoardThickness, -hd);
        }

        public static List<Vector3> GroupCorners(bool includeGraveyards)
        {
            var corners = BoardCorners().ToList();
            if (includeGraveyards) corners.AddRange(GraveyardView.Corners());
            return corners;
        }

        /// <summary>Viewport-space extent (min/max) of the group for the camera's current pose, without lens shift.</summary>
        public static Rect Extent(Camera cam, List<Vector3> corners)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in corners)
            {
                var v = cam.WorldToViewportPoint(c);
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public static void Fit(Camera cam, bool includeGraveyards = false, float margin = 0.02f)
        {
            cam.ResetProjectionMatrix();
            cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            var corners = GroupCorners(includeGraveyards);
            float lo = 4f, hi = 80f;
            for (int i = 0; i < 44; i++)
            {
                float mid = (lo + hi) / 2f;
                cam.transform.position = target - cam.transform.forward * mid;
                var e = Extent(cam, corners);
                bool fits = e.xMin >= margin && e.xMax <= 1 - margin && e.height <= 1 - 2 * margin;
                if (fits) hi = mid; else lo = mid;
            }
            cam.transform.position = target - cam.transform.forward * hi;
            // Centre the group vertically in the area without changing pitch, FOV or perspective.
            var extent = Extent(cam, corners);
            float shift = 0.5f - (extent.yMin + extent.yMax) / 2f;
            var projection = cam.projectionMatrix;
            projection.m12 -= 2f * shift;
            cam.projectionMatrix = projection;
        }
    }
}
