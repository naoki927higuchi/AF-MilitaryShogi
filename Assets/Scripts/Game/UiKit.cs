using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>Shared IMGUI styles for the play-mode UI and 「あそびかた」.</summary>
    public sealed class UiKit
    {
        public const float VirtualHeight = 900f;
        public readonly GUIStyle Panel, Label, Small, Header, Big, Button, BigButton, Selected, Center, Popup, Heading, Clock, TextField;
        public readonly Texture2D White;

        public static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        public UiKit()
        {
            var font = GameAssets.UiFont;
            White = Solid(Color.white);
            var text = new Color(0.95f, 0.91f, 0.84f);
            Panel = new GUIStyle { normal = { background = Solid(new Color(0.06f, 0.045f, 0.035f, 0.9f)) }, padding = new RectOffset(14, 14, 10, 10) };
            Label = new GUIStyle { font = font, fontSize = 15, wordWrap = true, richText = true, normal = { textColor = text } };
            Small = new GUIStyle(Label) { fontSize = 13 };
            Header = new GUIStyle(Label) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.84f, 0.52f) } };
            Big = new GUIStyle(Label) { fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = false, normal = { textColor = new Color(1f, 0.9f, 0.7f) } };
            Center = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            Heading = new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.93f, 0.82f, 0.62f) } };
            Button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15, normal = { textColor = text }, hover = { textColor = Color.white } };
            BigButton = new GUIStyle(Button) { fontSize = 18, fontStyle = FontStyle.Bold };
            Selected = new GUIStyle(Button) { normal = { background = Solid(new Color(0.78f, 0.55f, 0.22f, 1f)), textColor = Color.white }, hover = { background = Solid(new Color(0.85f, 0.62f, 0.28f, 1f)), textColor = Color.white } };
            Popup = new GUIStyle(Label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            Clock = new GUIStyle(Label) { fontSize = 16, wordWrap = false, normal = { textColor = new Color(0.82f, 0.76f, 0.66f) } };
            TextField = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 15 };
        }

        /// <summary>Elapsed time as m:ss, or h:mm:ss from one hour on.</summary>
        public static string FormatClock(double seconds)
        {
            long s = (long)System.Math.Floor(System.Math.Max(0, seconds));
            long h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
            return h > 0 ? h + ":" + m.ToString("00") + ":" + sec.ToString("00") : m + ":" + sec.ToString("00");
        }

        /// <summary>Visual group boundary inside a GUILayout panel: space, a thin line, space.</summary>
        public static void Separator(UiKit ui)
        {
            GUILayout.Space(10);
            var r = GUILayoutUtility.GetRect(10, 1, GUILayout.ExpandWidth(true));
            ui.Fill(r, new Color(0.75f, 0.55f, 0.28f, 0.45f));
            GUILayout.Space(8);
        }

        public static float Scale { get { return Mathf.Clamp(Screen.height / VirtualHeight, 0.75f, 2.5f); } }

        /// <summary>A button that looks pressed when <paramref name="on"/>.</summary>
        public bool Choice(Rect r, string text, bool on) { return GUI.Button(r, text, on ? Selected : Button); }

        public void Line(Vector2 a, Vector2 b, float width, Color color)
        {
            var saved = GUI.matrix;
            var savedColor = GUI.color;
            Vector2 d = b - a;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, d.magnitude, width), White);
            GUI.matrix = saved;
            GUI.color = savedColor;
        }

        public void Fill(Rect r, Color color)
        {
            var saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, White);
            GUI.color = saved;
        }
    }
}
