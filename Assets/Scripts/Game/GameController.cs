using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MilitaryShogi.Cpu;
using MilitaryShogi.Engine;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    public enum Phase { Setup, PlayerTurn, CpuThinking, Animating, Finished }

    public sealed class GameSettings
    {
        public int PlayerFormationSeed = 1001;
        public int CpuFormationSeed = 2002;
        public int CpuDecisionSeed = 3003;
        public FormationStyle? CpuStyle;           // null = derived from the CPU formation seed
        public bool EffectsOn = true;
        public float EffectSpeed = 1f;
        public bool ShowEnemyNumbers = true;
    }

    /// <summary>A combat line as the human remembers it: own kind, enemy number, result. Never the enemy kind.</summary>
    public sealed class CombatRecord
    {
        public int Ply;
        public bool PlayerAttacked;
        public PieceType OwnType;
        public int OwnNumber;
        public int EnemyNumber;
        public CombatOutcome Outcome;          // from the attacker's point of view
        public bool PlayerWon { get { return PlayerAttacked ? Outcome == CombatOutcome.AttackerWins : Outcome == CombatOutcome.DefenderWins; } }
        public bool Tie { get { return Outcome == CombatOutcome.Tie; } }
    }

    public sealed class Popup
    {
        public Vector3 World;
        public string Text;
        public Color Color;
        public float Until;
    }

    /// <summary>
    /// Game flow for human (South) vs CPU (North). The human's screen is driven only by the
    /// human's PlayerView; the CPU gets only the North PlayerView. The Match (referee) is
    /// the only object holding true kinds and is never handed to the presentation or the CPU.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public const Side Human = Side.South;
        public const Side Computer = Side.North;

        public GameSettings Settings = new GameSettings();
        public Phase Phase { get; private set; }
        public CpuPlayer Cpu { get; private set; }
        public PlayerView View { get; private set; }
        public Formation PlayerFormation { get; private set; }
        public FormationStyle PlayerStyle { get; private set; }
        public int SelectedNode { get; private set; } = -1;
        public int HoverNode { get; private set; } = -1;
        public readonly List<CombatRecord> Combats = new List<CombatRecord>();
        public readonly List<Popup> Popups = new List<Popup>();
        public string StatusText { get; private set; } = "";
        public event Action<ObservedMove> PlyFinished;
        public event Action GameStarted;
        public Camera MainCamera;
        public BoardView Board;

        private Match match;
        private Formation cpuFormation;
        private Transform pieceRoot;
        private readonly Dictionary<int, PieceView> pieces = new Dictionary<int, PieceView>();       // by piece id (during play)
        private readonly Dictionary<int, PieceView> setupPieces = new Dictionary<int, PieceView>();  // by node (during setup)
        private readonly List<MoveTarget> targets = new List<MoveTarget>();
        private Task<DecisionReport> thinking;
        private float thinkingSince;

        public IEnumerable<PieceView> PieceViews { get { return Phase == Phase.Setup ? setupPieces.Values : pieces.Values; } }

        public void Initialise(Camera cam, BoardView board)
        {
            MainCamera = cam;
            Board = board;
            pieceRoot = new GameObject("Pieces").transform;
            pieceRoot.SetParent(transform, false);
            NewSetup();
        }

        // ------------------------------------------------------------------
        // Setup phase
        // ------------------------------------------------------------------

        public void NewSetup()
        {
            StopAllCoroutines();
            thinking = null;
            match = null;
            View = null;
            Combats.Clear();
            Popups.Clear();
            Cpu = new CpuPlayer(Computer, Settings.CpuFormationSeed, Settings.CpuDecisionSeed, Settings.CpuStyle);
            cpuFormation = Cpu.CreateFormation();
            AutoArrange();
            Phase = Phase.Setup;
            StatusText = "初期配置：自軍の駒をクリックし、移動先または交換先をクリック（置けないマスは選べません）";
        }

        /// <summary>おまかせ配置 from the player formation seed (style also derived from that seed).</summary>
        public void AutoArrange()
        {
            PlayerStyle = FormationStyles.FromSeed(Settings.PlayerFormationSeed);
            PlayerFormation = FormationGenerator.Generate(Human, PlayerStyle, Settings.PlayerFormationSeed);
            SelectedNode = -1;
            BuildSetupViews();
        }

        /// <summary>Re-create the CPU with the current seeds/style (setup phase only).</summary>
        public void RefreshCpu()
        {
            if (Phase != Phase.Setup) return;
            Cpu = new CpuPlayer(Computer, Settings.CpuFormationSeed, Settings.CpuDecisionSeed, Settings.CpuStyle);
            cpuFormation = Cpu.CreateFormation();
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
            foreach (int node in cpuFormation.Pieces.Keys)
            {
                var v = PieceView.CreateEnemy(pieceRoot, -1, 0, false);
                v.PlaceAt(node);
                setupPieces[node] = v;
            }
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
                // Clicking another own piece that is not a valid swap target just selects it.
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
            PlacementRules.Validate(PlayerFormation);
            match = new Match(PlayerFormation.Clone(), cpuFormation);
            View = match.GetView(Human);
            Cpu.Observe(match.GetView(Computer));
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
            SelectedNode = -1;
            Phase = Phase.PlayerTurn;
            StatusText = "あなたの手番です";
            if (GameStarted != null) GameStarted();
        }

        // ------------------------------------------------------------------
        // Play
        // ------------------------------------------------------------------

        private void Update()
        {
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
                    if (thinking != null && thinking.IsCompleted && Time.time - thinkingSince >= (Settings.EffectsOn ? 0.35f / Settings.EffectSpeed : 0f))
                    {
                        if (thinking.IsFaulted) { Debug.LogException(thinking.Exception); StatusText = "CPU思考エラー: " + thinking.Exception.InnerException?.Message; thinking = null; return; }
                        var report = thinking.Result;
                        thinking = null;
                        Execute(Computer, report.Chosen.Command);
                    }
                    break;
            }
            Popups.RemoveAll(p => p.Until < Time.time);
        }

        /// <summary>Screen rectangles occupied by IMGUI panels (set by GameUi each frame).</summary>
        public readonly List<Rect> UiRects = new List<Rect>();

        private bool MouseOverUi()
        {
            var m = Input.mousePosition;
            var p = new Vector2(m.x, Screen.height - m.y);
            return UiRects.Any(r => r.Contains(p));
        }

        private void UpdateHover()
        {
            HoverNode = PickAtScreen(Input.mousePosition);
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
            var record = match.Apply(side, command);
            Phase = Phase.Animating;
            StartCoroutine(Play(record));
        }

        private IEnumerator Play(ObservedMove record)
        {
            var before = View;
            View = match.GetView(Human);
            Cpu.Observe(match.GetView(Computer));   // keep the monitor's beliefs current
            if (record.Combat != null) Combats.Add(ToCombatRecord(record, before));

            yield return Animate(record);

            ShowLastMove();
            if (PlyFinished != null) PlyFinished(record);
            if (match.Status == GameStatus.Finished)
            {
                Phase = Phase.Finished;
                StatusText = ResultText();
                yield break;
            }
            if (match.ToMove == Computer)
            {
                Phase = Phase.CpuThinking;
                StatusText = "CPU思考中…";
                thinkingSince = Time.time;
                var cpuView = match.GetView(Computer);
                var cpu = Cpu;
                thinking = Task.Run(() => cpu.Decide(cpuView));
            }
            else
            {
                Phase = Phase.PlayerTurn;
                StatusText = "あなたの手番です";
            }
        }

        private CombatRecord ToCombatRecord(ObservedMove r, PlayerView before)
        {
            bool playerAttacked = r.Mover == Human;
            int ownId = playerAttacked ? r.Combat.AttackerId : r.Combat.DefenderId;
            int enemyId = playerAttacked ? r.Combat.DefenderId : r.Combat.AttackerId;
            var own = before.OwnById(ownId);
            return new CombatRecord
            {
                Ply = r.Ply, PlayerAttacked = playerAttacked, OwnType = own.Type, OwnNumber = own.Number,
                EnemyNumber = before.EnemyById(enemyId).Number, Outcome = r.Combat.Outcome,
            };
        }

        /// <summary>
        /// Plays one ply. Every piece kind gets the same motion and the same clash; the only
        /// thing that differs is the publicly known result (win / lose / tie).
        /// </summary>
        private IEnumerator Animate(ObservedMove r)
        {
            var mover = pieces[r.PieceId];
            float speed = Mathf.Max(0.1f, Settings.EffectSpeed);
            bool fx = Settings.EffectsOn;
            var path = r.Path.Select(BoardLayout.Node).ToList();
            bool arc = r.Jumped > 0;   // flying over pieces is visible to everyone

            if (r.Combat == null)
            {
                if (fx) yield return mover.MoveAlong(path, 0.16f / speed, arc);
                mover.PlaceAt(r.To);
                yield break;
            }

            var defender = pieces[r.Combat.DefenderId];
            Vector3 target = BoardLayout.Node(r.To);
            if (fx)
            {
                // Approach to just in front of the defender.
                var approach = new List<Vector3>(path);
                Vector3 from = path.Count > 1 ? path[path.Count - 2] : BoardLayout.Node(r.From);
                approach[approach.Count - 1] = Vector3.Lerp(from, target, 0.55f);
                yield return mover.MoveAlong(approach, 0.16f / speed, arc);
                StartCoroutine(Flash(Vector3.Lerp(mover.transform.localPosition, target, 0.5f), 0.45f / speed));
                StartCoroutine(defender.Shake(0.4f / speed, 0.035f));
                yield return mover.Shake(0.4f / speed, 0.035f);
            }
            AddPopup(target, r);
            switch (r.Combat.Outcome)
            {
                case CombatOutcome.AttackerWins:
                    if (fx) yield return defender.Defeat(0.35f / speed); else defender.gameObject.SetActive(false);
                    if (fx) yield return mover.MoveAlong(new[] { target }, 0.12f / speed, false);
                    mover.PlaceAt(r.To);
                    defender.Node = -1;
                    break;
                case CombatOutcome.DefenderWins:
                    if (fx) yield return mover.Defeat(0.35f / speed); else mover.gameObject.SetActive(false);
                    mover.Node = -1;
                    break;
                default:
                    if (fx) { StartCoroutine(defender.Defeat(0.35f / speed)); yield return mover.Defeat(0.35f / speed); }
                    else { defender.gameObject.SetActive(false); mover.gameObject.SetActive(false); }
                    mover.Node = -1; defender.Node = -1;
                    break;
            }
            if (fx) yield return new WaitForSeconds(0.25f / speed);
        }

        private void AddPopup(Vector3 world, ObservedMove r)
        {
            bool playerAttacked = r.Mover == Human;
            var o = r.Combat.Outcome;
            string text; Color c;
            if (o == CombatOutcome.Tie) { text = "相打ち"; c = new Color(1f, 0.85f, 0.3f); }
            else if ((o == CombatOutcome.AttackerWins) == playerAttacked) { text = "勝ち"; c = new Color(0.5f, 1f, 0.55f); }
            else { text = "負け"; c = new Color(1f, 0.45f, 0.4f); }
            Popups.Add(new Popup { World = world + Vector3.up * 0.3f, Text = text, Color = c, Until = Time.time + 1.4f / Mathf.Max(0.1f, Settings.EffectSpeed) });
        }

        /// <summary>Neutral clash flash (expanding ring), identical for every combat.</summary>
        private IEnumerator Flash(Vector3 at, float seconds)
        {
            var mat = GameAssets.Overlay(new Color(1f, 0.95f, 0.8f, 0.9f), null, 20);
            var ring = MeshKit.Disc("Clash", transform, at + Vector3.up * 0.25f, 0.8f, 1f, mat);
            float t = 0;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = t / seconds;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.75f, k);
                mat.color = new Color(1f, 0.95f, 0.8f, 0.9f * (1 - k));
                yield return null;
            }
            Destroy(ring);
            Destroy(mat);
        }

        public string ResultText()
        {
            if (match == null || match.Status != GameStatus.Finished) return "";
            string reason = match.EndReason == EndReason.HeadquartersCaptured ? "総司令部占領" : match.EndReason == EndReason.NoLegalMoves ? "相手に動かせる駒がない" : "手数制限";
            if (match.Winner == null) return "引き分け（" + reason + "）";
            return (match.Winner == Human ? "あなたの勝ち" : "CPUの勝ち") + "（" + reason + "）";
        }

        public bool IsFinished { get { return match != null && match.Status == GameStatus.Finished; } }
        public int Ply { get { return match != null ? match.Ply : 0; } }
        public Side ToMove { get { return match != null ? match.ToMove : Human; } }
    }
}
