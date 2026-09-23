using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// The referee's one-time notice when the player has lost every piece that can capture the
    /// headquarters (大将〜少佐): 「続行」 or 「投了」. It is a rule notice from the referee, not the CPU
    /// suggesting resignation. Registered in <see cref="ModalInput"/> as "judge" (frontmost); the game
    /// and the clock are paused while it is open (GameController, PauseReason.Judge). Used in 対戦, 研究
    /// and on Android. Nothing is shown about the CPU's pieces (that would leak hidden kinds).
    /// </summary>
    public sealed class JudgeUi : MonoBehaviour
    {
        private GameController game;
        private UiKit ui;
        /// <summary>Android: larger buttons.</summary>
        public bool Touch;
        public int LastDrawFrame { get; private set; } = -1;

        public void Bind(GameController controller) { game = controller; }

        private void Update()
        {
            // Android back / Esc: same as 「続行」 (the safe choice).
            if (game != null && game.ResignNoticeOpen && Input.GetKeyDown(KeyCode.Escape)) game.ContinueAfterNotice();
        }

        private void OnGUI()
        {
            if (game == null || !game.ResignNoticeOpen) return;
            UiGuard.Run("JudgeUi", DrawGui, () => game.ContinueAfterNotice());
        }

        private void DrawGui()
        {
            if (ui == null) ui = new UiKit();
            if (Event.current.type == EventType.Repaint) LastDrawFrame = Time.frameCount;
            GUI.depth = -90;
            float scale = UiKit.Scale;
            float vw = Screen.width / scale, vh = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            ui.Fill(new Rect(0, 0, vw, vh), new Color(0, 0, 0, 0.45f));
            // Heights are measured, so the text wraps cleanly on narrow phone screens too.
            float w = Mathf.Min(520, vw - 24), button = Touch ? 52 : 44, inner = w - 40;
            const string title = "総司令部を占領できる駒がなくなりました";
            const string body = "大将〜少佐がすべて失われました。総司令部占領による勝利はできません。\n続行すれば、敵の動かせる駒をなくすなど他の勝ち筋で最後まで戦えます。";
            var header = new GUIStyle(ui.Header) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            var small = new GUIStyle(ui.Small) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            var center = new GUIStyle(ui.Label) { alignment = TextAnchor.UpperCenter };
            float th = header.CalcHeight(new GUIContent(title), inner), bh = small.CalcHeight(new GUIContent(body), inner);
            float h = 12 + 22 + th + 10 + bh + 12 + 26 + 16 + button + 18;
            var r = new Rect((vw - w) / 2, (vh - h) / 2, w, h);
            ui.Fill(r, new Color(0.07f, 0.05f, 0.04f, 0.98f));
            ui.Fill(new Rect(r.x, r.y, r.width, 2), new Color(0.75f, 0.55f, 0.28f, 0.9f));
            float y = r.y + 12;
            GUI.Label(new Rect(r.x + 20, y, inner, 22), "審判より", small); y += 22;
            GUI.Label(new Rect(r.x + 20, y, inner, th), title, header); y += th + 10;
            GUI.Label(new Rect(r.x + 20, y, inner, bh), body, small); y += bh + 12;
            GUI.Label(new Rect(r.x + 20, y, inner, 26), "投了しますか？", center);
            float bw = Mathf.Min(180, (r.width - 60) / 2);
            var cont = new Rect(r.x + r.width / 2 - bw - 10, r.yMax - button - 18, bw, button);
            var resign = new Rect(r.x + r.width / 2 + 10, r.yMax - button - 18, bw, button);
            UiKit.Spot("judge.panelTL", new Rect(r.x, r.y, 0, 0));
            UiKit.Spot("judge.panelBR", new Rect(r.xMax, r.yMax, 0, 0));
            UiKit.Spot("judge.continue", cont);
            UiKit.Spot("judge.resign", resign);
            if (GUI.Button(cont, "続行", ui.BigButton)) game.ContinueAfterNotice();
            if (GUI.Button(resign, "投了", ui.BigButton)) game.ResignFromNotice();
        }
    }
}
