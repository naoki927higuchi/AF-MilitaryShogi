using System;
using System.Collections.Generic;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Common input rule for modal UI (設定, あそびかた, 新規対局の確認, and later e.g. 投了確認):
    /// while any modal is open, only the frontmost one receives pointer input.
    ///
    /// Why this exists (1.2.1 acceptance): IMGUI has no occlusion between controls or between
    /// MonoBehaviours, and the 3D board only avoided the rectangles the panels reported. So
    /// controls behind a panel still got hover/press/click (「研究モードへ」 behind あそびかた switched
    /// the mode), and the board took clicks anywhere outside the panel.
    ///
    ///  - Background IMGUI is drawn inside <see cref="Background"/>: while blocked, pointer and key
    ///    events are hidden from those controls and they draw no hover or pressed state.
    ///    The frontmost modal is drawn outside it and gets the real pointer.
    ///  - The board and other Update-based input ask <see cref="PointerBlocked"/>.
    ///  - A click outside the modal therefore does nothing and the modal stays open.
    ///  - When a modal closes, pointer input stays blocked until every mouse button has been
    ///    released, so the click that closed it can never reach what was underneath.
    ///  - Keyboard focus is dropped when a modal opens, so a background text field keeps no keys.
    ///
    /// Modal is only about input. Pausing the game is separate (あそびかた pauses via
    /// <see cref="Presentation.OpenHelp"/>; 設定 does not pause and the clock keeps running).
    /// </summary>
    public static class ModalInput
    {
        private sealed class Entry
        {
            public string Name;
            public int Layer;
            public Func<bool> IsOpen;
        }

        private static readonly List<Entry> modals = new List<Entry>();
        private static bool lastOpen, holdUntilRelease;
        private static int holdFrame;

        /// <summary>Register a modal (a later registration with the same name replaces it). Higher layer = further in front.</summary>
        public static void Register(string name, int layer, Func<bool> isOpen)
        {
            modals.RemoveAll(m => m.Name == name);
            modals.Add(new Entry { Name = name, Layer = layer, IsOpen = isOpen });
        }

        /// <summary>Name of the frontmost open modal, or null.</summary>
        public static string Top
        {
            get
            {
                Entry top = null;
                foreach (var m in modals)
                    if (m.IsOpen() && (top == null || m.Layer > top.Layer)) top = m;
                return top != null ? top.Name : null;
            }
        }

        public static bool AnyOpen { get { Refresh(); return lastOpen; } }

        public static bool IsTop(string name) { return Top == name; }

        /// <summary>True while background UI and the board must not receive pointer input.</summary>
        public static bool PointerBlocked { get { Refresh(); return lastOpen || holdUntilRelease; } }

        /// <summary>Whether the modal is being held after a close (for tests).</summary>
        public static bool HoldingAfterClose { get { Refresh(); return holdUntilRelease; } }

        private static void Refresh()
        {
            bool open = Top != null;
            if (open && !lastOpen) GUIUtility.keyboardControl = 0;
            if (!open && lastOpen) { holdUntilRelease = true; holdFrame = Time.frameCount; }
            lastOpen = open;
            if (holdUntilRelease && Time.frameCount > holdFrame && !AnyMouseButtonHeld()) holdUntilRelease = false;
        }

        private static bool AnyMouseButtonHeld()
        {
            return Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
        }

        /// <summary>
        /// Scope for drawing background IMGUI. With <paramref name="blocked"/> the current pointer/key
        /// event is hidden from the controls in the scope and restored afterwards.
        /// </summary>
        public static BackgroundScope Background(bool blocked) { return new BackgroundScope(blocked); }

        /// <summary>Background scope that blocks exactly when <see cref="PointerBlocked"/> is set.</summary>
        public static BackgroundScope Background() { return new BackgroundScope(PointerBlocked); }

        public struct BackgroundScope : IDisposable
        {
            // Pointer events are turned into Ignore for the background, so no control there can take
            // them. (Moving the pointer off-screen is not enough: GUILayout areas / GUI clips
            // recompute Event.mousePosition from the real pointer.) On Repaint a dummy hot control
            // is set, which stops background controls from drawing hover or pressed states.
            private const int DummyHotControl = int.MaxValue - 1207;
            private readonly bool typeChanged, hotChanged;
            private readonly EventType savedType;
            private readonly int savedHot;

            public BackgroundScope(bool blocked)
            {
                typeChanged = hotChanged = false;
                savedType = EventType.Ignore;
                savedHot = 0;
                var e = Event.current;
                if (!blocked || e == null) return;
                if (e.isMouse || e.isKey || e.type == EventType.ScrollWheel || e.type == EventType.ContextClick
                    || e.type == EventType.DragUpdated || e.type == EventType.DragPerform || e.type == EventType.DragExited)
                {
                    savedType = e.type;
                    e.type = EventType.Ignore;
                    typeChanged = true;
                }
                else if (e.type == EventType.Repaint)
                {
                    savedHot = GUIUtility.hotControl;
                    GUIUtility.hotControl = DummyHotControl;
                    hotChanged = true;
                }
            }

            public void Dispose()
            {
                var e = Event.current;
                if (typeChanged && e != null && e.type == EventType.Ignore) e.type = savedType;
                if (hotChanged && GUIUtility.hotControl == DummyHotControl) GUIUtility.hotControl = savedHot;
            }
        }
    }
}
