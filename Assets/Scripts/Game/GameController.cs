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

        [Flags] public enum PauseReason { None = 0, Help = 1, Test = 2 }
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
                if (kind.HasValue) v.ShowResearchFace(kind.Value); else v.ShowBack();
            }
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
            if (!Paused && Session.Started && !IsFinished) ElapsedSeconds += Time.unscaledDeltaTime;
            if (Paused) return;
            UpdateHover();
            switch (Phase)
            {
                case Phase.Setup:
                    if (Input.GetMouseButtonDown(0) && !MouseOverUi()) SetupClick(HoverNode);
                    break;
                case Phase.PlayerTurn:
                    if (Input.GetMouseButtonDown(0) && !MouseOverUi()) PlayClick(HoverNode);
                    else if (Input.GetMouseButtonDown(1)) Select(-1);
                    break;
                case Phase.CpuThinking:
                    if (thinking != null && thinking.IsCompleted && Time.time - thinkingSince >= (Settings.Effect == EffectMode.Normal ? 0.35f : 0.15f) / Settings.EffectSpeed)
                    {
                        if (thinking.IsFaulted) { Debug.LogException(thinking.Exception); StatusText = "CPU思考エラー: " + thinking.Exception.InnerException?.Message; thinking = null; return; }
                        var report = thinking.Result;
                        thinking = null;
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

        private void UpdateHover()
        {
            HoverNode = MouseOverUi() ? -1 : PickAtScreen(Input.mousePosition);
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
        public void ClickNode(int node)
        {
            if (Paused) return;
            if (Phase == Phase.Setup) SetupClick(node);
            else if (Phase == Phase.PlayerTurn) PlayClick(node);
        }

        public PieceView PieceAtNode(int node)
        {
            if (node < 0) return null;
            return PieceViews.FirstOrDefault(v => v.gameObject.activeSelf && v.Node == node);
        }

        private void PlayClick(int node)
        {
            if (node < 0) { Select(-1); return; }
            if (SelectedNode >= 0)
            {
                var hit = targets.FirstOrDefault(t => t.To == node);
                if (hit.Path != null)
                {
                    var own = View.Own.First(p => p.Node == SelectedNode);
                    Execute(Human, new MoveCommand(own.Id, node));
                    return;
                }
            }
            var mine = View.Own.FirstOrDefault(p => p.Node == node);
            Select(mine != null ? node : -1);
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
            if (View == null || View.History.Count == 0) return;
            var last = View.History[View.History.Count - 1];
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
            if (Session.Match.Status == GameStatus.Finished)
            {
                if (Audio != null) Audio.Play(Sfx.End);
                Phase = Phase.Finished;
                StatusText = ResultText();
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

        public string ResultText()
        {
            var match = Session.Match;
            if (match == null || match.Status != GameStatus.Finished) return "";
            string reason = match.EndReason == EndReason.HeadquartersCaptured ? "総司令部占領" : match.EndReason == EndReason.NoLegalMoves ? "相手に動かせる駒がない" : "手数制限";
            if (match.Winner == null) return "引き分け（" + reason + "）";
            return (match.Winner == Human ? "あなたの勝ち" : "CPUの勝ち") + "（" + reason + "）";
        }

        public bool IsFinished { get { return Session.Match != null && Session.Match.Status == GameStatus.Finished; } }
        public int Ply { get { return Session.Match != null ? Session.Match.Ply : 0; } }
        public Side ToMove { get { return Session.Match != null ? Session.Match.ToMove : Human; } }
    }
}
