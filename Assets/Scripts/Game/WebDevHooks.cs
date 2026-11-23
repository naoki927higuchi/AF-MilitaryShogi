#if AFMS_WEBDEV
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Web check build only (Build-Web.ps1 -Dev → bin/WebDev-&lt;Version&gt;/, define AFMS_WEBDEV). The
    /// browser has no command line, so the referee screens that are hard to reach by play are opened
    /// with keys, through the same routines as a real game. Not compiled into the public build.
    ///   7: 投了の確認（占領できる駒がなくなった）   8: 膠着の確認   9 / 0: 審判の短いメッセージ（CPUへの介入）
    /// 7 and 8 work on the player's turn only.
    /// </summary>
    public sealed class WebDevHooks : MonoBehaviour
    {
        private GameController game;

        public void Bind(GameController controller) { game = controller; }

        private void Update()
        {
            if (game == null) return;
            if (Input.GetKeyDown(KeyCode.Alpha7)) game.TestJudgeNotice(JudgeNoticeKind.NoCapturers);
            if (Input.GetKeyDown(KeyCode.Alpha8)) game.TestJudgeNotice(JudgeNoticeKind.Stalemate);
            if (Input.GetKeyDown(KeyCode.Alpha9)) game.TestRefereeNote(false);
            if (Input.GetKeyDown(KeyCode.Alpha0)) game.TestRefereeNote(true);
        }

        private void OnGUI()
        {
            GUI.depth = -200;
            GUI.Label(new Rect(6, Screen.height - 20, 600, 20), "WebDev (check build)  7/8: judge notice  9/0: referee note");
        }
    }
}
#endif
