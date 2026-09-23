using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Fixed high-angle perspective camera from the player's side. Pitch and field of view are
    /// constant; only the distance along the view direction changes so that the board (and in
    /// play mode the loss areas beside it) fits the free screen area. The far side appears narrower.
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

        public static void Fit(Camera cam, bool includeGraveyards = false, float margin = 0.02f)
        {
            cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            var corners = BoardCorners().ToList();
            if (includeGraveyards) corners.AddRange(GraveyardView.Corners());
            float lo = 4f, hi = 60f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) / 2f;
                cam.transform.position = target - cam.transform.forward * mid;
                bool fits = true;
                foreach (var c in corners)
                {
                    var v = cam.WorldToViewportPoint(c);
                    if (v.x < margin || v.x > 1 - margin || v.y < margin || v.y > 1 - margin) { fits = false; break; }
                }
                if (fits) hi = mid; else lo = mid;
            }
            cam.transform.position = target - cam.transform.forward * hi;
        }
    }
}
