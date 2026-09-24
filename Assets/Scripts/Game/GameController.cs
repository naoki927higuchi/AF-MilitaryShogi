using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MilitaryShogi.Cpu;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    public enum Phase { Setup, PlayerTurn, CpuThinking, Animating, Finished }

    /// <summary>Referee notices to the player: no piece left that can capture the HQ, or a stalemate of repeated positions.</summary>
    public enum JudgeNoticeKind { NoCapturers, Stalemate }

    /// <summary>
    /// Game flow for human (South) vs CPU (North): input, CPU turns, animation. All game data lives
    /// in <see cref="GameSession"/>; the human's screen is driven only by the human's PlayerView and
    /// the CPU gets only the North PlayerView. Presentation modes are handled elsewhere and never
    /// call into the session.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public const Side Human = GameSession.Human;
        public const Side Computer = GameSession.Computer;

        public GameSettings Settings = new GameSettings();
        public GameSession Session { get; private set; }
        public Phase Phase { get; private set; }
        public int SelectedNode { get; private set; } = -1;
        public int HoverNode { get; private set; } = -1;
        public PlayerKnownFacts KnownFacts { get; private set; }
        public EnemyTooltip Tooltip { get; private set; }
        public string StatusText { get; private set; } = "";
        public event Action<ObservedMove> PlyFinished;
        public event Action GameStarted;
        public event Action SetupStarted;
        public AudioDirector Audio;
        public FormationPresets Presets { get; private set; }

        /// <summary>Game clock: from 対局開始 to the end of the game. Stops only while 「あそびかた」 is open.</summary>
        public double ElapsedSeconds { get; private set; }

        /// <summary>Visual events of combat animations (for checking SE sync).</summary>
        public readonly List<KeyValuePair<string, float>> VisualEvents = new List<KeyValuePair<string, float>>();
        /// <summary>Per topple: (fall start, topple sound start, landing) in Time.time, for the sync test.</summary>
        public readonly List<Vector3> ToppleTimings = new List<Vector3>();
        public Camera MainCamera;
        public BoardView Board;
        public GraveyardView Graveyard;

        /// <summary>
        /// Why the game is paused. Help: 「あそびかた」. Judge: the referee's resignation notice.
        /// Background: the app lost focus / went to the background (Android). Test: auto-test.
        /// Any reason stops CPU turns, animations and the clock.
        /// </summary>
        [Flags] public enum PauseReason { None = 0, Help = 1, Test = 2, Judge = 4, Background = 8 }
        private PauseReason pauseReasons;

        /// <summary>While true (「あそびかた」 open) nothing advances: no input, no CPU turn, no animation, no clock.</summary>
        public bool Paused { get { return pauseReasons != PauseReason.None; } }
        public PauseReason PauseReasons { get { return pauseReasons; } }

        private Transform pieceRoot;
        private readonly Dictionary<int, PieceView> pieces = new Dictionary<int, PieceView>();       // by piece id (during play)
        private readonly Dictionary<int, PieceView> setupPieces = new Dictionary<int, PieceView>();  // by node (during setup)
        private readonly List<MoveTarget> targets = new List<MoveTarget>();
        private Task<DecisionReport> thinking;
        private float thinkingSince;

        // Shortcuts into the session (read-only for the UI).
        public CpuPlayer Cpu { get { return Session.Cpu; } }
        public PlayerView View { get { return Session.View; } }
        public Formation PlayerFormation { get { return Session.PlayerFormation; } }
        public FormationStyle PlayerStyle { get { return Session.PlayerStyle; } }
        public List<CombatRecord> Combats { get { return Session.Combats; } }

        public IEnumerable<PieceView> PieceViews { get { return Phase == Phase.Setup ? setupPieces.Values : pieces.Values; } }

        public void Initialise(Camera cam, BoardView board, GraveyardView graveyard, AudioDirector audio)
        {
            MainCamera = cam;
            Audio = audio;
            Presets = UserData.LoadPresets();
            Tooltip = new EnemyTooltip(this);
            Board = board;
            Graveyard = graveyard;
            pieceRoot = new GameObject("Pieces").transform;
            pieceRoot.SetParent(transform, false);
            NewSetup(false);
        }

        /// <summary>Test fixtures only (layout screenshots): hold the game without opening the help.</summary>
        public void SetPaused(bool paused) { SetPause(PauseReason.Test, paused); }

        public void SetPause(PauseReason reason, bool on)
        {
            if (on) pauseReasons |= reason; else pauseReasons &= ~reason;
            Time.timeScale = Paused ? 0f : 1f;
        }

        // ------------------------------------------------------------------
        // Setup phase
        // ------------------------------------------------------------------

        /// <param name="randomizeSeeds">Play mode: fresh hidden seeds for every new game. Research mode keeps the entered seeds.</param>
        public void NewSetup(bool randomizeSeeds)
        {
            StopAllCoroutines();
            thinking = null;
            KnownFacts = null;
            ResetReplay();
            ResignNoticeOpen = false;
            resignNoticeShown = false;
            InspectedPieceId = -1;
            LastIntervention = null;
            RefereeNote = null;
            SetPause(PauseReason.Judge, false);
            if (randomizeSeeds)
            {
                // Seed choice is presentation-side randomness; the game itself only uses DeterministicRandom.
                var state = UnityEngine.Random.state;
                UnityEngine.Random.InitState(Environment.TickCount);
                Settings.PlayerFormationSeed = UnityEngine.Random.Range(1, 1000000);
                Settings.CpuFormationSeed = UnityEngine.Random.Range(1, 1000000);
                Settings.CpuDecisionSeed = UnityEngine.Random.Range(1, 1000000);
                UnityEngine.Random.state = state;
            }
            Session = new GameSession(Settings);
            SelectedNode = -1;
            ElapsedSeconds = 0;
            Phase = Phase.Setup;
            BuildSetupViews();
            if (Graveyard != null) Graveyard.Clear();
            StatusText = "初期配置：自軍の駒をクリックし、移動先または交換先をクリック（置けないマスは選べません）";
            if (SetupStarted != null) SetupStarted();
        }

        // ------------------------------------------------------------------
        // Presets (own placement, both modes, setup only)
        // ------------------------------------------------------------------

        public void SavePreset(int slot, string name)
        {
            Presets.Save(slot, name, PlayerFormation);
            UserData.SavePresets(Presets);
        }

        /// <summary>Apply a saved placement. Only the own placement changes (never CPU seeds, CPU formation or knowledge).</summary>
        public bool LoadPreset(int slot)
        {
            if (Phase != Phase.Setup) return false;
            Formation f;
            try { f = Presets.Load(slot, Human); }
            catch (ArgumentException e) { Debug.LogWarning("Preset rejected: " + e.Message); return false; }
            if (f == null) return false;
            Session.SetPlayerFormation(f);
            SelectedNode = -1;
            BuildSetupViews();
            return true;
        }

        // ------------------------------------------------------------------
        // Research reveal (研究モード「CPU駒の正体を表示」 only)
        // ------------------------------------------------------------------

        private bool revealEnemies;
        public bool EnemiesRevealed { get { return revealEnemies; } }

        /// <summary>
        /// Shows the true faces of CPU pieces on the board. Called by Presentation with true only in
        /// research mode with the switch on; the loss areas never reveal anything.
        /// </summary>
        public void SetEnemyReveal(bool on)
        {
            if (on == revealEnemies) return;
            revealEnemies = on;
            ApplyEnemyFaces();
        }

        private void ApplyEnemyFaces()
        {
            foreach (var kv in Phase == Phase.Setup ? setupPieces : pieces)
            {
                var v = kv.Value;
                if (v.IsOwn) continue;
                PieceType? kind = null;
                if (revealEnemies) kind = Phase == Phase.Setup ? Session.ResearchTrueKindAtSetupNode(kv.Key) : Session.ResearchTrueKind(v.Id);
                else if (PostGameReveal) kind = Session.ResearchTrueKind(v.Id);
                if (kind.HasValue) v.ShowResearchFace(kind.Value); else v.ShowBack();
            }
            // The loss areas show kinds only with the post-game reveal (never in play or with the research switch).
            if (Graveyard != null && Phase == Phase.Finished) Graveyard.ShowEnemyFaces(PostGameReveal ? (Func<int, PieceType?>)Session.ResearchTrueKind : null);
        }

        // ------------------------------------------------------------------
        // 棋譜再現 and 敵駒開示 (1.5.0, after the game has ended only)
        // ------------------------------------------------------------------

        private PlayerView replayView;
        private PlayerKnownFacts replayFacts;

        /// <summary>Displayed TURN while reviewing a finished game (0 = initial placement), or -1 before the end.</summary>
        public int ReplayTurn { get; private set; } = -1;
        /// <summary>Last TURN of the finished game (number of plies), 0 before the end.</summary>
        public int ReplayLength { get { return Phase == Phase.Finished && View != null ? GameReplay.Length(View) : 0; } }
        /// <summary>The position shown on the board: the replayed TURN after the game, else the live view.</summary>
        public PlayerView DisplayView { get { return replayView ?? View; } }
        /// <summary>What the player knew at the displayed TURN (never anything learned later).</summary>
        public PlayerKnownFacts DisplayFacts { get { return replayFacts ?? KnownFacts; } }
        /// <summary>
        /// 「敵駒開示」: true kinds of the CPU pieces on the board and in the loss area. Only after the game
        /// has ended and only while switched on; off at the start of every game, never saved.
        /// </summary>
        public bool PostGameReveal { get; private set; }

        public void SetPostGameReveal(bool on)
        {
            if (Phase != Phase.Finished) on = false;
            if (on == PostGameReveal) return;
            PostGameReveal = on;
            ApplyEnemyFaces();
        }

        private void ResetReplay()
        {
            replayView = null;
            replayFacts = null;
            ReplayTurn = -1;
            PostGameReveal = false;
        }

        /// <summary>Shows the finished game at TURN <paramref name="turn"/> (clamped). View only: nothing is played.</summary>
        public void ReplayGo(int turn)
        {
            if (Phase != Phase.Finished || View == null) return;
            int n = GameReplay.Length(View);
            turn = Mathf.Clamp(turn, 0, n);
            ReplayTurn = turn;
            replayView = GameReplay.ViewAt(View, turn);
            replayFacts = turn == n ? KnownFacts : new PlayerKnownFacts(replayView);
            SelectedNode = -1;
            targets.Clear();
            Board.ClearHighlights();
            foreach (var p in replayView.Own) Show(pieces[p.Id], p.Node);
            foreach (var e in replayView.Enemy) Show(pieces[e.Id], e.Node);
            if (Graveyard != null) Graveyard.Sync(replayView);
            ShowLastMove();
            if (InspectedPieceId >= 0 && !replayView.EnemyById(InspectedPieceId).Alive) InspectedPieceId = -1;
            ApplyEnemyFaces();
        }

        private static void Show(PieceView v, int node)
        {
            if (node >= 0) v.PlaceAt(node);
            else if (v.gameObject.activeSelf) v.gameObject.SetActive(false);
        }

        /// <summary>おまかせ配置 from the player formation seed.</summary>
        public void AutoArrange()
        {
            if (Phase != Phase.Setup) return;
            Session.AutoArrange();
            SelectedNode = -1;
            BuildSetupViews();
        }

        /// <summary>Re-create the CPU with the current seeds/profile/style (setup phase only).</summary>
        public void RefreshCpu()
        {
            if (Phase != Phase.Setup) return;
            Session.CreateCpu();
            BuildSetupViews();
        }

        private void ClearViews()
        {
            foreach (var v in pieces.Values) Destroy(v.gameObject);
            foreach (var v in setupPieces.Values) Destroy(v.gameObject);
            pieces.Clear();
            setupPieces.Clear();
            Board.ClearHighlights();
        }

        private void BuildSetupViews()
        {
            ClearViews();
            foreach (var p in PlayerFormation.Pieces)
            {
                var v = PieceView.CreateOwn(pieceRoot, -1, 0, p.Value, true);
                v.PlaceAt(p.Key);
                setupPieces[p.Key] = v;
            }
            // The CPU army is shown face down at its positions (positions are public).
            foreach (int node in Session.CpuFormation.Pieces.Keys)
            {
                var v = PieceView.CreateEnemy(pieceRoot, -1, 0, false);
                v.PlaceAt(node);
                setupPieces[node] = v;
            }
            if (revealEnemies) ApplyEnemyFaces();
        }

        private void SetupClick(int node)
        {
            if (SelectedNode < 0)
            {
                if (node >= 0 && BoardGraph.InCamp(node, Human) && PlayerFormation.Pieces.ContainsKey(node)) SelectSetup(node);
                return;
            }
            if (node == SelectedNode || node < 0) { SelectSetup(-1); return; }
            if (!PlacementTargets(SelectedNode).Contains(node))
            {
                if (PlayerFormation.Pieces.ContainsKey(node) && BoardGraph.InCamp(node, Human)) SelectSetup(node);
                return;
            }
            PlayerFormation.TryMoveOrSwap(SelectedNode, node);
            BuildSetupViews();
            SelectSetup(-1);
        }

        /// <summary>Cells where the selected piece may go (move to the empty cell or swap), honouring placement rules for both pieces.</summary>
        public List<int> PlacementTargets(int from)
        {
            var list = new List<int>();
            foreach (int n in BoardGraph.CampCells(Human))
            {
                if (n == from) continue;
                var probe = PlayerFormation.Clone();
                if (probe.TryMoveOrSwap(from, n)) list.Add(n);
            }
            return list;
        }

        private void SelectSetup(int node)
        {
            SelectedNode = node;
            Board.ClearHighlights();
            foreach (var v in setupPieces.Values) v.SetLifted(false);
            if (node < 0) return;
            if (Audio != null) Audio.Play(Sfx.Select);
            setupPieces[node].SetLifted(true);
            Board.Highlight(node, HighlightKind.Selected);
            foreach (int n in PlacementTargets(node)) Board.Highlight(n, HighlightKind.Placement);
        }

        // ------------------------------------------------------------------
        // Game start
        // ------------------------------------------------------------------

        public void StartGame()
        {
            if (Phase != Phase.Setup) return;
            Session.Start();
            KnownFacts = new PlayerKnownFacts(View);
            ClearViews();
            foreach (var p in View.Own)
            {
                var v = PieceView.CreateOwn(pieceRoot, p.Id, p.Number, p.Type, true);
                v.PlaceAt(p.Node);
                pieces[p.Id] = v;
            }
            foreach (var e in View.Enemy)
            {
                var v = PieceView.CreateEnemy(pieceRoot, e.Id, e.Number, false);   // no kind is passed – there is none to pass
                v.PlaceAt(e.Node);
                pieces[e.Id] = v;
            }
            if (Graveyard != null) Graveyard.Sync(View);
            SelectedNode = -1;
            ElapsedSeconds = 0;
            resignNoticeShown = false;
            Phase = Phase.PlayerTurn;
            if (revealEnemies) ApplyEnemyFaces();
            StatusText = "あなたの手番です";
            if (GameStarted != null) GameStarted();
        }

        // ------------------------------------------------------------------
        // Play
        // ------------------------------------------------------------------

        private void Update()
        {
            // Clock: runs from 対局開始 until the result is decided; only 「あそびかた」 (pause) stops it.
            // A single frame never adds more than 0.5 s, so returning from the background (Android) or a
            // stall cannot add the time the game was not running.
            if (!Paused && Session.Started && !IsFinished) ElapsedSeconds += Mathf.Min(Time.unscaledDeltaTime, 0.5f);
            if (Paused) return;
            // A modal UI (設定・あそびかた・確認) owns the pointer: no hover, selection or moves behind it.
            bool pointer = !ModalInput.PointerBlocked;
            UpdateHover(pointer);
            switch (Phase)
            {
                case Phase.Setup:
                    if (pointer && Input.GetMouseButtonDown(0) && !MouseOverUi()) SetupClick(HoverNode);
                    break;
                case Phase.PlayerTurn:
                    if (pointer && Input.GetMouseButtonDown(0) && !MouseOverUi()) PlayClick(HoverNode, HoverNode >= 0 || OnBoardAtScreen(Input.mousePosition));
                    else if (pointer && Input.GetMouseButtonDown(1)) Select(-1);
                    break;
                case Phase.Finished:
                    // 棋譜再現: observations of the tapped enemy piece (Android); no piece can be moved.
                    if (TapInspect && pointer && Input.GetMouseButtonDown(0) && !MouseOverUi()) Inspect(HoverNode);
                    break;
                case Phase.Animating:
                    if (TapInspect && pointer && Input.GetMouseButtonDown(0) && !MouseOverUi()) Inspect(HoverNode);
                    break;
                case Phase.CpuThinking:
                    if (TapInspect && pointer && Input.GetMouseButtonDown(0) && !MouseOverUi()) Inspect(HoverNode);
                    if (thinking != null && thinking.IsCompleted && Time.time - thinkingSince >= (Settings.Effect == EffectMode.Normal ? 0.35f : 0.15f) / Settings.EffectSpeed)
                    {
                        if (thinking.IsFaulted) { Debug.LogException(thinking.Exception); StatusText = "CPU思考エラー: " + thinking.Exception.InnerException?.Message; thinking = null; return; }
                        var report = thinking.Result;
                        thinking = null;
                        // Referee rule (not CPU thinking): after a stalemate intervention toward the CPU,
                        // a move that continues that repetition is replaced by the CPU's next-best move.
                        if (Session.ApplyRefereeToCpu(report)) ShowRefereeNote("審判：CPUは反復を続けない手を指しました");
                        Cpu.Record(report);
                        Execute(Computer, report.Chosen.Command);
                    }
                    break;
            }
        }

        /// <summary>Screen rectangles occupied by IMGUI panels (set by the active UI each frame).</summary>
        public readonly List<Rect> UiRects = new List<Rect>();

        private bool MouseOverUi()
        {
            var m = Input.mousePosition;
            return IsOverUi(new Vector2(m.x, Screen.height - m.y));
        }

        /// <summary>Whether a screen point (GUI coordinates, pixels) is covered by a UI panel this frame.</summary>
        public bool IsOverUi(Vector2 guiPoint) { return UiRects.Any(r => r.Contains(guiPoint)); }

        private void UpdateHover(bool pointer)
        {
            // Touch has no hover: the node under the finger only matters at the moment of the tap.
            if (TapInspect && !Input.GetMouseButtonDown(0)) { HoverNode = -1; return; }
            HoverNode = !pointer || MouseOverUi() ? -1 : PickAtScreen(Input.mousePosition);
        }

        /// <summary>Board node under a screen position (the same routine the mouse uses), or -1.</summary>
        public int PickAtScreen(Vector3 screen)
        {
            if (MainCamera == null || !MainCamera.pixelRect.Contains(screen)) return -1;
            var ray = MainCamera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0, 0.1f, 0));
            float d;
            return plane.Raycast(ray, out d) ? BoardLayout.Pick(ray.GetPoint(d)) : -1;
        }

        /// <summary>A left click on a node, exactly as the mouse handler performs it (used by the auto-test).</summary>
        /// <summary>A click on a node, or (node −1) off the board, exactly as the mouse/touch handler performs it (auto-test).</summary>
        public void ClickNode(int node)
        {
            if (Paused) return;
            if (Phase == Phase.Setup) SetupClick(node);
            else if (Phase == Phase.PlayerTurn) PlayClick(node, node >= 0);
            else if (Phase == Phase.Finished) Inspect(node);
        }

        /// <summary>A click on the board surface that is not on any square (band, margins), as the handler performs it.</summary>
        public void ClickBoardBetweenSquares()
        {
            if (!Paused && Phase == Phase.PlayerTurn) PlayClick(-1, true);
        }

        /// <summary>Whether a screen point lies on the board surface (squares, band or margins).</summary>
        public bool OnBoardAtScreen(Vector3 screen)
        {
            if (MainCamera == null || !MainCamera.pixelRect.Contains(screen)) return false;
            var ray = MainCamera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Vector3.zero);
            float d;
            if (!plane.Raycast(ray, out d)) return false;
            var hit = ray.GetPoint(d);
            return Mathf.Abs(hit.x) <= BoardLayout.Width / 2f && Mathf.Abs(hit.z) <= BoardLayout.Depth / 2f;
        }

        public PieceView PieceAtNode(int node)
        {
            if (node < 0) return null;
            return PieceViews.FirstOrDefault(v => v.gameObject.activeSelf && v.Node == node);
        }

        // ------------------------------------------------------------------
        // Android: tap an enemy piece to see its public observations (no hover on touch screens)
        // ------------------------------------------------------------------

        /// <summary>Android: a tap on an enemy piece shows its observations instead of hover.</summary>
        public bool TapInspect;
        /// <summary>Enemy piece whose observations are shown (Android), or -1.</summary>
        public int InspectedPieceId { get; private set; } = -1;

        private void Inspect(int node)
        {
            var shown = DisplayView;
            var enemy = node >= 0 && shown != null ? shown.Enemy.FirstOrDefault(e => e.Alive && e.Node == node) : null;
            InspectedPieceId = enemy != null ? enemy.Id : -1;
        }

        public void ClearInspection() { InspectedPieceId = -1; }

        /// <summary>Target nodes of the selected piece (player's turn).</summary>
        public List<int> PlayTargets() { return targets.Select(t => t.To).ToList(); }

        /// <summary>
        /// A click/tap on the board during the player's turn (1.4.0: tap-safe selection).
        /// With an own piece selected:
        ///  - legal target (empty or enemy) → move / attack;
        ///  - enemy piece that is not a target → deselect (Android: show its observations);
        ///  - another own piece → select it; the selected piece itself → keep the selection;
        ///  - any other square or the board surface between squares → nothing (selection kept);
        ///  - off the board → deselect.
        /// A tap is never "corrected" to a nearby legal target: a wrong move can decide the game.
        /// </summary>
        private void PlayClick(int node, bool onBoard)
        {
            var enemy = node >= 0 ? View.Enemy.FirstOrDefault(e => e.Alive && e.Node == node) : null;
            var mine = node >= 0 ? View.Own.FirstOrDefault(p => p.Node == node) : null;
            if (SelectedNode >= 0)
            {
                var hit = node >= 0 ? targets.FirstOrDefault(t => t.To == node) : default(MoveTarget);
                if (hit.Path != null)
                {
                    InspectedPieceId = -1;
                    var own = View.Own.First(p => p.Node == SelectedNode);
                    Execute(Human, new MoveCommand(own.Id, node));
                    return;
                }
                if (enemy != null) { Select(-1); if (TapInspect) InspectedPieceId = enemy.Id; return; }
                if (mine != null) { InspectedPieceId = -1; if (node != SelectedNode) Select(node); return; }
                if (onBoard) return;                         // missed square on the board: keep the selection
                Select(-1);                                  // off the board
                InspectedPieceId = -1;
                return;
            }
            // Nothing selected.
            if (mine != null) { InspectedPieceId = -1; Select(node); return; }
            if (enemy != null) { if (TapInspect) InspectedPieceId = enemy.Id; return; }
            InspectedPieceId = -1;                           // empty square / off the board: close observations
        }

        private void Select(int node)
        {
            SelectedNode = node;
            targets.Clear();
            Board.ClearHighlights();
            foreach (var v in pieces.Values) if (v.gameObject.activeSelf) v.SetLifted(false);
            ShowLastMove();
            if (node < 0) return;
            var own = View.Own.First(p => p.Node == node);
            if (Audio != null) Audio.Play(Sfx.Select);
            targets.AddRange(MoveRules.Generate(own.Type, Human, node, View.Owners));
            pieces[own.Id].SetLifted(true);
            Board.Highlight(node, HighlightKind.Selected);
            foreach (var t in targets) Board.Highlight(t.To, t.IsAttack ? HighlightKind.Attack : HighlightKind.Move);
            StatusText = targets.Count == 0 ? "この駒は動かせません" : "移動先を選んでください（右クリックで取り消し）";
        }

        private void ShowLastMove()
        {
            var shown = DisplayView;
            if (shown == null || shown.History.Count == 0) return;
            var last = shown.History[shown.History.Count - 1];
            Board.Highlight(last.From, HighlightKind.LastMove);
            Board.Highlight(last.To, HighlightKind.LastMove);
        }

        private void Execute(Side side, MoveCommand command)
        {
            SelectedNode = -1;
            targets.Clear();
            Board.ClearHighlights();
            PlayerView before;
            var record = Session.Apply(side, command, out before);
            Phase = Phase.Animating;
            StartCoroutine(Play(record));
        }

        private IEnumerator Play(ObservedMove record)
        {
            yield return Animate(record);
            while (Paused) yield return null;

            KnownFacts.Update(View);
            if (Graveyard != null) Graveyard.Sync(View);
            ShowLastMove();
            if (PlyFinished != null) PlyFinished(record);
            // Referee notice, once per game: the player has just lost the last piece that can capture the
            // headquarters. Only the player's own pieces are looked at (no hidden information). The CPU
            // is never told anything and never resigns.
            if (Session.Match.Status == GameStatus.Playing && !resignNoticeShown && !PlayerHasCapturer)
                yield return JudgeNotice(JudgeNoticeKind.NoCapturers);
            // Stalemate referee: the same public position has come back again and again.
            var intervention = Session.TakeIntervention();
            if (intervention != null && Session.Match.Status == GameStatus.Playing)
            {
                LastIntervention = intervention;
                if (intervention.Addresses(Computer)) ShowRefereeNote("審判：同じ局面の反復が続いています。CPUは今後この反復を続けません");
                if (intervention.Addresses(Human)) yield return JudgeNotice(JudgeNoticeKind.Stalemate);
            }
            if (Session.Match.Status == GameStatus.Finished)
            {
                EnterFinished();
                yield break;
            }
            if (Session.Match.ToMove == Computer)
            {
                Phase = Phase.CpuThinking;
                StatusText = "CPU思考中…";
                thinkingSince = Time.time;
                var cpuView = Session.CpuView();
                var cpu = Cpu;
                cpu.Observe(cpuView);                          // state changes happen here, on the main thread
                thinking = Task.Run(() => cpu.Think(cpuView)); // pure computation in the background
            }
            else
            {
                Phase = Phase.PlayerTurn;
                StatusText = "あなたの手番です";
            }
        }

        private float Speed { get { return Mathf.Max(0.1f, Settings.EffectSpeed); } }

        private void Mark(string name)
        {
            VisualEvents.Add(new KeyValuePair<string, float>(name, Time.time));
            if (VisualEvents.Count > 2000) VisualEvents.RemoveRange(0, 500);
        }

        /// <summary>
        /// Plays one ply. Every piece kind gets the same motion; only the publicly known result
        /// (which piece or pieces leave the board) differs. 通常 is a little more physical than 簡易;
        /// both keep the essential beats: approach → hit → reaction → loser(s) fall and leave.
        /// Sounds are started in the same frames as the matching visual events.
        /// </summary>
        private IEnumerator Animate(ObservedMove r)
        {
            var mover = pieces[r.PieceId];
            bool normal = Settings.Effect == EffectMode.Normal;
            float speed = Speed;
            var path = r.Path.Select(BoardLayout.Node).ToList();
            bool arc = r.Jumped > 0;   // flying over pieces is visible to everyone

            if (r.Combat == null)
            {
                yield return mover.MoveAlong(path, (normal ? 0.16f : 0.11f) / speed, arc);
                mover.PlaceAt(r.To);
                Mark("land");
                if (Audio != null) Audio.Play(Sfx.Place);
                yield break;
            }

            var defender = pieces[r.Combat.DefenderId];
            Vector3 target = BoardLayout.Node(r.To);
            Vector3 from = path.Count > 1 ? path[path.Count - 2] : BoardLayout.Node(r.From);
            Vector3 dir = target - from;
            dir.y = 0;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            var approach = new List<Vector3>(path);
            approach[approach.Count - 1] = Vector3.Lerp(from, target, normal ? 0.45f : 0.55f);
            yield return mover.MoveAlong(approach, (normal ? 0.16f : 0.11f) / speed, arc);

            if (normal)
            {
                // Wind up, then strike hard.
                yield return mover.Slide(-dir * 0.12f, 0.12f / speed);
                yield return mover.Slide(dir * 0.26f, 0.06f / speed);
            }
            Mark("contact");
            if (Audio != null) Audio.Play(Sfx.Clash);
            float react = (normal ? 0.34f : 0.16f) / speed;
            float amp = normal ? 0.05f : 0.022f;
            var outcome = r.Combat.Outcome;
            // Reaction: the losing side shakes harder; the winner also recoils.
            StartCoroutine(defender.Shake(react, outcome == CombatOutcome.DefenderWins ? amp * 0.5f : amp));
            yield return mover.Shake(react, outcome == CombatOutcome.AttackerWins ? amp * 0.5f : amp);
            if (normal) yield return new WaitForSeconds(0.1f / speed);   // 一拍

            float fall = (normal ? 0.42f : 0.24f) / speed;
            switch (outcome)
            {
                case CombatOutcome.AttackerWins:
                    yield return Topple(defender, dir, fall);
                    yield return mover.MoveAlong(new[] { target }, 0.12f / speed, false);
                    mover.PlaceAt(r.To);
                    defender.Node = -1;
                    break;
                case CombatOutcome.DefenderWins:
                    yield return Topple(mover, -dir, fall);
                    mover.Node = -1;
                    break;
                default:
                    // Both fall, slightly staggered so the two falls can be seen and heard.
                    StartCoroutine(Topple(defender, dir, fall));
                    yield return new WaitForSeconds((normal ? 0.14f : 0.08f) / speed);
                    yield return Topple(mover, -dir, fall);
                    mover.Node = -1;
                    defender.Node = -1;
                    break;
            }
            yield return new WaitForSeconds((normal ? 0.2f : 0.08f) / speed);
        }

        /// <summary>
        /// A loser tips over away from the hit and falls onto the board, then leaves for the loss area.
        /// The topple sound starts so that its main "falls on the board" hit coincides with the landing.
        /// </summary>
        private IEnumerator Topple(PieceView v, Vector3 away, float seconds)
        {
            float landing = seconds * 0.6f;
            float soundAt = Mathf.Max(0f, landing - AudioDirector.ToppleImpactOffset);
            bool played = false, landed = false;
            float t0 = Time.time, soundTime = 0, landTime = 0;
            var fall = v.Fall(away, seconds, landing);
            while (true)
            {
                if (!played && Time.time - t0 >= soundAt)
                {
                    played = true;
                    soundTime = Time.time;
                    if (Audio != null) Audio.Play(Sfx.Topple);
                }
                if (!landed && Time.time - t0 >= landing)
                {
                    landed = true;
                    landTime = Time.time;
                    Mark("fall-landing");
                }
                if (!fall.MoveNext()) break;
                yield return fall.Current;
            }
            if (!played && Audio != null) { Audio.Play(Sfx.Topple); soundTime = Time.time; }
            if (!landed) { Mark("fall-landing"); landTime = Time.time; }
            ToppleTimings.Add(new Vector3(t0, soundTime, landTime));
            if (ToppleTimings.Count > 1000) ToppleTimings.RemoveRange(0, 250);
        }

        // ------------------------------------------------------------------
        // Referee notice and resignation (1.3.0)
        // ------------------------------------------------------------------

        private bool resignNoticeShown;

        /// <summary>The referee's one-time notice 「総司令部を占領できる駒がなくなりました／投了しますか？」 is open.</summary>
        public bool ResignNoticeOpen { get; private set; }

        /// <summary>Whether the player still has 大将〜少佐 on the board (own pieces only).</summary>
        public bool PlayerHasCapturer { get { return View != null && View.Own.Any(p => p.Alive && PieceCatalog.CanCaptureHeadquarters(p.Type)); } }

        /// <summary>Which referee notice is open.</summary>
        public JudgeNoticeKind NoticeKind { get; private set; }

        /// <summary>Last stalemate intervention (for tests and the research monitor); public data only.</summary>
        public RefereeIntervention LastIntervention { get; private set; }

        /// <summary>A short, non-blocking referee message (e.g. when the CPU is told to break a repetition).</summary>
        public string RefereeNote { get; private set; }
        public float RefereeNoteUntil { get; private set; }

        private void ShowRefereeNote(string text)
        {
            RefereeNote = text;
            RefereeNoteUntil = Time.unscaledTime + 5f;
        }

        private IEnumerator JudgeNotice(JudgeNoticeKind kind)
        {
            if (kind == JudgeNoticeKind.NoCapturers) resignNoticeShown = true;
            NoticeKind = kind;
            ResignNoticeOpen = true;
            SetPause(PauseReason.Judge, true);     // game and clock stop while the notice is open
            StatusText = kind == JudgeNoticeKind.NoCapturers ? "審判：総司令部を占領できる駒がなくなりました" : "審判：同じ局面が繰り返されています";
            while (ResignNoticeOpen) yield return null;
            SetPause(PauseReason.Judge, false);
        }

        private void EnterFinished()
        {
            if (Audio != null) Audio.Play(Sfx.End);
            Phase = Phase.Finished;
            StatusText = ResultText();
            // 棋譜再現 starts at the last TURN; the CPU pieces stay face down until 「敵駒開示」 is switched on.
            ResetReplay();
            InspectedPieceId = -1;
            ReplayGo(GameReplay.Length(View));
        }

        /// <summary>
        /// Test hook (auto-test): show the referee notice on the player's turn through the same routine
        /// as a real game, then finish the game if the player resigned.
        /// </summary>
        public void TestJudgeNotice(JudgeNoticeKind kind = JudgeNoticeKind.NoCapturers)
        {
            if (Phase != Phase.PlayerTurn || (kind == JudgeNoticeKind.NoCapturers && resignNoticeShown)) return;
            StartCoroutine(TestJudgeNoticeRoutine(kind));
        }

        private IEnumerator TestJudgeNoticeRoutine(JudgeNoticeKind kind)
        {
            var phase = Phase;
            Phase = Phase.Animating;
            yield return JudgeNotice(kind);
            if (Session.Match.Status == GameStatus.Finished) EnterFinished();
            else { Phase = phase; StatusText = "あなたの手番です"; }
        }

        /// <summary>「続行」: close the notice; it is not shown again in this game.</summary>
        public void ContinueAfterNotice()
        {
            if (!ResignNoticeOpen) return;
            ResignNoticeOpen = false;
            StatusText = "対局を続行します";
        }

        /// <summary>「投了」: the player resigns, the CPU wins (EndReason.Resigned).</summary>
        public void ResignFromNotice()
        {
            if (!ResignNoticeOpen || Session.Match.Status != GameStatus.Playing) return;
            Session.ResignPlayer();
            ResignNoticeOpen = false;   // the ply coroutine continues and finishes the game
        }

        /// <summary>Result headline: 「あなたの勝ち」「CPUの勝ち」「引き分け」.</summary>
        public string ResultTitle()
        {
            var match = Session.Match;
            if (match == null || match.Status != GameStatus.Finished) return "";
            return match.Winner == null ? "引き分け" : match.Winner == Human ? "あなたの勝ち" : "CPUの勝ち";
        }

        /// <summary>Why the game ended, in words.</summary>
        public string ResultReason()
        {
            var match = Session.Match;
            if (match == null || match.Status != GameStatus.Finished) return "";
            switch (match.EndReason)
            {
                case EndReason.HeadquartersCaptured: return "総司令部占領";
                case EndReason.NoLegalMoves: return "相手に動かせる駒がない";
                case EndReason.NoCapturers: return "双方とも総司令部を占領できる駒がなくなりました";
                case EndReason.Resigned: return "投了（あなたが投了しました）";
                default: return "手数制限";
            }
        }

        public string ResultText()
        {
            var match = Session.Match;
            if (match == null || match.Status != GameStatus.Finished) return "";
            return ResultTitle() + "（" + ResultReason() + "）";
        }

        public bool IsFinished { get { return Session.Match != null && Session.Match.Status == GameStatus.Finished; } }
        public int Ply { get { return Session.Match != null ? Session.Match.Ply : 0; } }
        public Side ToMove { get { return Session.Match != null ? Session.Match.ToMove : Human; } }
    }
}
