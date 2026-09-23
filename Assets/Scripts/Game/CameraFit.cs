using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Fixed high-angle perspective camera from the player's side. Pitch and field of view are
    /// constant; only the distance along the view direction changes so that the whole board fits
    /// the free screen area (the far side appears narrower: a trapezoid).
    /// </summary>
    public static class CameraFit
    {
        public const float Pitch = 54f;
        private static readonly Vector3 target = new Vector3(0f, 0f, -0.35f);

        public static void Fit(Camera cam)
        {
            cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            float hw = BoardLayout.Width / 2f + 0.15f, hd = BoardLayout.Depth / 2f + 0.15f;
            var corners = new[]
            {
                new Vector3(-hw, 0, -hd), new Vector3(hw, 0, -hd), new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd),
                new Vector3(-hw, -BoardLayout.BoardThickness, -hd), new Vector3(hw, -BoardLayout.BoardThickness, -hd),
            };
            float lo = 4f, hi = 60f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) / 2f;
                cam.transform.position = target - cam.transform.forward * mid;
                bool fits = true;
                foreach (var c in corners)
                {
                    var v = cam.WorldToViewportPoint(c);
                    if (v.x < 0.02f || v.x > 0.98f || v.y < 0.02f || v.y > 0.98f) { fits = false; break; }
                }
                if (fits) hi = mid; else lo = mid;
            }
            cam.transform.position = target - cam.transform.forward * hi;
        }
    }
}
