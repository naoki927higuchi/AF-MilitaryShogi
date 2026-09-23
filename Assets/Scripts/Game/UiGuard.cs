using System;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Defensive wrapper for IMGUI panels.
    ///
    /// 1.1.1 incident (研究モードの暗幕・ボタン無反応): ResearchUi.DrawBeliefs threw a
    /// NullReferenceException every frame (a past-turn report index survived into a new setup where
    /// no report exists). An exception inside GUILayout.BeginArea aborts OnGUI in every event pass,
    /// leaves the GUI clip/matrix stack unbalanced (panels vanish, a darkened panel background
    /// remains) and prevents button clicks from completing, while board input (handled in Update)
    /// kept working. The cause is fixed in ResearchUi; this guard makes any future exception
    /// recoverable: it logs once, lets the owner reset its transient UI state, releases IMGUI
    /// hot/keyboard control and ends the GUI pass cleanly via ExitGUI.
    /// </summary>
    public static class UiGuard
    {
        public static int ErrorCount { get; private set; }
        public static string LastError { get; private set; }

        public static void Run(string owner, Action draw, Action recover)
        {
            try
            {
                draw();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception e)
            {
                ErrorCount++;
                string message = owner + ": " + e.GetType().Name + ": " + e.Message;
                if (message != LastError)
                {
                    Debug.LogError("UI recovered from " + message + "\n" + e.StackTrace);
                    UserData.AppendErrorLog(message + " | " + e.StackTrace.Replace('\n', ' '));
                }
                LastError = message;
                try { if (recover != null) recover(); } catch (Exception) { }
                GUIUtility.hotControl = 0;
                GUIUtility.keyboardControl = 0;
                GUI.matrix = Matrix4x4.identity;
                GUI.enabled = true;
                GUIUtility.ExitGUI();
            }
        }
    }
}
