using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// 棋譜再現 controls (1.5.0, after the game only): ◀◀ ◀ TURN n / N ▶ ▶▶ and the 「敵駒開示」 switch.
    /// Shared by the PC top bar and the Android top bar; each passes the area it has room for.
    /// View only: the buttons change the displayed TURN, nothing is played, saved or branched.
    /// The arrows are drawn as triangles (not font glyphs), so they look the same with every UI font.
    /// </summary>
    public static class ReplayBar
    {
        private static Texture2D triangle;

        /// <summary>Left-pointing triangle, white on transparent.</summary>
        private static Texture2D Triangle
        {
            get
            {
                if (triangle != null) return triangle;
                const int s = 32;
                triangle = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[s * s];
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float half = Mathf.Abs(y + 0.5f - s / 2f);     // distance from the middle row
                        float edge = (x + 0.5f) / s * (s / 2f);        // tip at the left
                        float a = Mathf.Clamp01(edge - half + 0.5f);
                        px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                triangle.SetPixels32(px);
                triangle.Apply();
                return triangle;
            }
        }

        /// <param name="area">Where the controls go (GUI units).</param>
        /// <param name="toggleWidth">Width of the 「敵駒開示」 switch at the right end.</param>
        /// <param name="labelFont">Font size of 「TURN n / N」.</param>
        public static void Draw(UiKit ui, GameController game, Rect area, float toggleWidth, int labelFont)
        {
            int n = game.ReplayLength, t = game.ReplayTurn;
            const float gap = 4f;
            float h = area.height;
            float bw = Mathf.Min(56f, Mathf.Max(40f, (area.width - toggleWidth - 6 * gap) * 0.14f));
            float lw = Mathf.Max(60f, area.width - toggleWidth - 4 * bw - (toggleWidth > 0f ? 5 : 4) * gap);
            float x = area.x;
            bool was = GUI.enabled;

            GUI.enabled = was && t > 0;
            if (Arrow(ui, new Rect(x, area.y, bw, h), true, 2, "replay.first")) game.ReplayGo(0);
            x += bw + gap;
            if (Arrow(ui, new Rect(x, area.y, bw, h), true, 1, "replay.prev")) game.ReplayGo(t - 1);
            x += bw + gap;
            GUI.enabled = was;

            var label = new Rect(x, area.y, lw, h);
            UiKit.Spot("replay.turn", label);
            GUI.Label(label, "TURN " + t + " / " + n, new GUIStyle(ui.Big) { fontSize = labelFont, alignment = TextAnchor.MiddleCenter });
            x += lw + gap;

            GUI.enabled = was && t < n;
            if (Arrow(ui, new Rect(x, area.y, bw, h), false, 1, "replay.next")) game.ReplayGo(t + 1);
            x += bw + gap;
            if (Arrow(ui, new Rect(x, area.y, bw, h), false, 2, "replay.last")) game.ReplayGo(n);
            GUI.enabled = was;

            if (toggleWidth <= 0f) return;                   // the caller places the switch itself
            var toggle = new Rect(area.xMax - toggleWidth, area.y, toggleWidth, h);
            UiKit.Spot("replay.reveal", toggle);
            bool on = game.PostGameReveal;
            if (GUI.Button(toggle, on ? "敵駒開示 ON" : "敵駒開示 OFF", on ? ui.Selected : ui.Button)) game.SetPostGameReveal(!on);
        }

        private static bool Arrow(UiKit ui, Rect r, bool left, int count, string spot)
        {
            UiKit.Spot(spot, r);
            bool clicked = GUI.Button(r, GUIContent.none, ui.Button);
            if (Event.current.type == EventType.Repaint)
            {
                float s = Mathf.Min(r.height * 0.42f, 18f), w = s * 0.8f;
                float total = count * w - (count - 1) * w * 0.15f;
                float x0 = r.center.x - total / 2f;
                var saved = GUI.color;
                GUI.color = GUI.enabled ? new Color(0.96f, 0.9f, 0.78f, 1f) : new Color(0.96f, 0.9f, 0.78f, 0.3f);
                for (int i = 0; i < count; i++)
                {
                    var tr = new Rect(x0 + i * w * 0.85f, r.center.y - s / 2f, w, s);
                    if (left) GUI.DrawTexture(tr, Triangle);
                    else GUI.DrawTextureWithTexCoords(tr, Triangle, new Rect(1, 0, -1, 1));
                }
                GUI.color = saved;
            }
            return clicked;
        }
    }
}
