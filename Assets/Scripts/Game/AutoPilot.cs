using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MilitaryShogi.Cpu;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Automated in-player verification (-autotest DIR):
    ///  - checks mouse picking for every node and placement editing through the click handler,
    ///  - plays a full game in the real scene; the human side is chosen by a second CpuPlayer
    ///    (which also sees only the South PlayerView) and entered through the click handler,
    ///  - every frame checks that no enemy piece renderer uses anything but the common back
    ///    texture (so an enemy face can never be on screen, including during combat effects),
    ///  - saves screenshots of setup, a combat, mid-game and the final position,
    ///  - writes autotest_report.txt and quits (exit code 0 = pass).
    /// </summary>
    public sealed class AutoPilot : MonoBehaviour
    {
        private GameController game;
        private string dir;
        private readonly List<string> log = new List<string>();
        private int violations;
        private int framesChecked;
        private int enemyRendererChecks;
        private bool combatShot;
        private CpuPlayer humanDriver;

        public void Begin(GameController controller, string outputDir)
        {
            game = controller;
            dir = outputDir;
            Directory.CreateDirectory(dir);
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSeconds(1.0f);
            yield return Shot("01_setup.png");
            CheckPicking();
            yield return CheckPlacementEditing();
            game.AutoArrange();   // back to the seed formation so the run stays reproducible
            game.StartGame();
            humanDriver = new CpuPlayer(GameController.Human, game.Settings.PlayerFormationSeed, 777);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("02_start.png");
            game.PlyFinished += OnPly;

            float deadline = Time.realtimeSinceStartup + 900f;
            while (!game.IsFinished && Time.realtimeSinceStartup < deadline)
            {
                if (game.Phase == Phase.PlayerTurn)
                {
                    // Play the human side through the click handler: select the piece, then the target.
                    var cmd = humanDriver.Decide(game.View).Chosen.Command;
                    int from = game.View.OwnById(cmd.PieceId).Node;
                    game.ClickNode(from);
                    if (game.SelectedNode != from) Fail("click did not select own piece at " + BoardGraph.Describe(from));
                    game.ClickNode(cmd.To);
                    if (game.Phase == Phase.PlayerTurn) Fail("click on a legal target did not move: " + cmd);
                }
                if (!combatShot && game.Phase == Phase.Animating && game.Popups.Count > 0)
                {
                    combatShot = true;
                    yield return Shot("03_combat.png");
                }
                yield return null;
            }
            yield return new WaitForSeconds(0.8f);
            yield return Shot("08_final.png");
            Finish();
        }

        private int failures;

        private void Fail(string message)
        {
            failures++;
            log.Add("FAIL: " + message);
        }

        /// <summary>Every node's screen position must map back to the same node through the mouse routine.</summary>
        private void CheckPicking()
        {
            int ok = 0;
            for (int n = 0; n < BoardGraph.NodeCount; n++)
            {
                var sp = game.MainCamera.WorldToScreenPoint(BoardLayout.Node(n) + Vector3.up * 0.1f);
                int picked = game.PickAtScreen(sp);
                if (picked == n) ok++; else Fail("pick " + BoardGraph.Describe(n) + " -> " + (picked >= 0 ? BoardGraph.Describe(picked) : "none"));
            }
            log.Add("picking round trip: " + ok + "/" + BoardGraph.NodeCount);
        }

        /// <summary>Manual placement through clicks: an illegal target is refused, a legal swap is applied.</summary>
        private IEnumerator CheckPlacementEditing()
        {
            var f = game.PlayerFormation;
            int mine = f.Pieces.First(p => p.Value == PieceType.Mine).Key;
            int arm = BoardGraph.CampCells(GameController.Human).First(BoardGraph.IsArmEnd);
            string before = f.Signature();
            game.ClickNode(mine);
            if (game.SelectedNode != mine) Fail("setup click did not select the mine");
            if (game.PlacementTargets(mine).Contains(arm)) Fail("gateway offered as target for a mine");
            game.ClickNode(arm);
            if (game.PlayerFormation.Signature() != before) Fail("mine was placed on a gateway");
            game.ClickNode(-1);
            yield return null;
            int general = game.PlayerFormation.Pieces.First(p => p.Value == PieceType.General).Key;
            int lieutenant = game.PlayerFormation.Pieces.First(p => p.Value == PieceType.SecondLieutenant).Key;
            game.ClickNode(general);
            game.ClickNode(lieutenant);
            var after = game.PlayerFormation;
            if (after.Pieces[lieutenant] != PieceType.General || after.Pieces[general] != PieceType.SecondLieutenant) Fail("legal swap by clicks was not applied");
            if (!PlacementRules.IsValid(after)) Fail("formation invalid after editing");
            log.Add("placement editing by clicks: refused illegal, applied legal swap");
            yield return new WaitForSeconds(0.3f);
            yield return Shot("01b_setup_edited.png");
        }

        private void OnPly(ObservedMove r)
        {
            log.Add("TURN " + r.Ply + " " + r.Mover + " #" + r.PieceId + " " + BoardGraph.Describe(r.From) + "->" + BoardGraph.Describe(r.To)
                + (r.Combat != null ? " combat " + r.Combat.Outcome : ""));
            var ui = GetComponent<GameUi>();
            if (r.Ply == 30) StartCoroutine(Shot("04_turn30.png"));
            if (r.Ply == 31) { ui.MonitorVisible = true; ui.MonitorTab = 0; }
            if (r.Ply == 33) StartCoroutine(Shot("05_monitor_decision.png"));
            if (r.Ply == 34) ui.MonitorTab = 1;
            if (r.Ply == 35) StartCoroutine(Shot("06_monitor_beliefs.png"));
            if (r.Ply == 36) { ui.MonitorVisible = false; ui.HistoryVisible = true; }
            if (r.Ply == 37) StartCoroutine(Shot("07_history.png"));
            if (r.Ply == 38) ui.HistoryVisible = false;
            if (r.Ply > 30) game.Settings.EffectSpeed = 4f;   // keep the run short once the effects are recorded
        }

        private void LateUpdate()
        {
            if (game == null) return;
            framesChecked++;
            var back = GameAssets.Back;
            foreach (var v in game.PieceViews)
            {
                if (v.IsOwn) continue;
                foreach (var m in v.Materials)
                {
                    enemyRendererChecks++;
                    var tex = m.mainTexture;
                    bool ok = tex == back || tex == GameAssets.Side;
                    if (!ok)
                    {
                        violations++;
                        log.Add("VIOLATION: enemy piece #" + v.Number + " uses texture " + (tex != null ? tex.name : "null"));
                    }
                }
            }
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
            yield return null;
        }

        private void Finish()
        {
            var sb = new StringBuilder();
            bool pass = violations == 0 && failures == 0 && game.IsFinished;
            sb.AppendLine("AF-MilitaryShogi autotest " + GameBootstrap.Version);
            sb.AppendLine("seeds P/C/D = " + game.Settings.PlayerFormationSeed + "/" + game.Settings.CpuFormationSeed + "/" + game.Settings.CpuDecisionSeed);
            sb.AppendLine("CPU style = " + FormationStyles.JapaneseName(game.Cpu.Style));
            sb.AppendLine("result = " + game.ResultText() + " after " + game.Ply + " plies");
            sb.AppendLine("frames checked = " + framesChecked + ", enemy material checks = " + enemyRendererChecks + ", violations = " + violations);
            sb.AppendLine("CPU decisions = " + game.Cpu.Reports.Count + ", avg think ms = " + (game.Cpu.Reports.Count > 0 ? game.Cpu.Reports.Average(r => r.ThinkMilliseconds).ToString("0.0") : "-"));
            sb.AppendLine("combats = " + game.Combats.Count + ", scripted-click failures = " + failures);
            sb.AppendLine(pass ? "PASS" : "FAIL");
            sb.AppendLine();
            foreach (var l in log) sb.AppendLine(l);
            File.WriteAllText(Path.Combine(dir, "autotest_report.txt"), sb.ToString(), Encoding.UTF8);
            Application.Quit(pass ? 0 : 1);
        }
    }
}
