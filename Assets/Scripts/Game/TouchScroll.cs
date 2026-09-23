using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Vertical scrolling for IMGUI content on touch screens (IMGUI scroll views only scroll with bars
    /// or a wheel). A finger that moves vertically more than a few units becomes a scroll drag: the
    /// press is taken away from whatever control it started on, so dragging never also taps a button.
    /// The mouse wheel works too (PC layout checks). Usage per OnGUI:
    /// <code>scroll.Begin(viewport, contentHeight); …draw at y from 0…; scroll.End();</code>
    /// </summary>
    public sealed class TouchScroll
    {
        public float Offset;
        private bool pressed, dragging;
        private Vector2 start, last;
        private const float DragThreshold = 10f;

        public bool Dragging { get { return dragging; } }

        public void Begin(Rect viewport, float contentHeight)
        {
            var e = Event.current;
            float max = Mathf.Max(0, contentHeight - viewport.height);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (viewport.Contains(e.mousePosition)) { pressed = true; dragging = false; start = last = e.mousePosition; }
                    break;
                case EventType.MouseDrag:
                    if (pressed)
                    {
                        if (!dragging && Mathf.Abs(e.mousePosition.y - start.y) > DragThreshold && Mathf.Abs(e.mousePosition.y - start.y) > Mathf.Abs(e.mousePosition.x - start.x))
                        {
                            dragging = true;
                            GUIUtility.hotControl = 0;          // the button under the finger no longer gets the release
                        }
                        if (dragging) { Offset -= e.mousePosition.y - last.y; e.Use(); }
                        last = e.mousePosition;
                    }
                    break;
                case EventType.MouseUp:
                    if (dragging) e.Use();
                    pressed = dragging = false;
                    break;
                case EventType.ScrollWheel:
                    if (viewport.Contains(e.mousePosition)) { Offset += e.delta.y * 20f; e.Use(); }
                    break;
            }
            Offset = Mathf.Clamp(Offset, 0, max);
            GUI.BeginGroup(viewport);
            GUI.BeginGroup(new Rect(0, -Offset, viewport.width, Mathf.Max(contentHeight, viewport.height)));
        }

        public void End()
        {
            GUI.EndGroup();
            GUI.EndGroup();
        }
    }
}
