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
    /// Automated in-player verification (-autotest DIR [-toggleModes]):
    ///  - start-up is 対戦 (play) mode; the title logo is drawn inside the top bar;
    ///  - mouse picking for every node and placement editing through the click handler;
    ///  - a full game; the human side is chosen by a second CpuPlayer (South view only) and entered
    ///    through the click handler;
    ///  - 対戦→研究→対戦 leaves the session fingerprint and UnityEngine.Random.state unchanged;
    ///    with -toggleModes the mode is flipped after every ply during the whole game, and the final
    ///    fingerprint must equal a run without toggling (compared by Run-AutoTest.ps1);
    ///  - 「あそびかた」 pauses the game (no ply, no CPU decision recorded while open);
    ///  - loss areas: counts match, own sorted by kind, enemy in removal order at index-only slots;
    ///  - every frame: no enemy piece (board or loss area) uses anything but the back texture;
    ///  - screenshots of both modes and the help; writes autotest_report.txt, exit code 0 = pass.
    /// </summary>
    public sealed class AutoPilot : MonoBehaviour
    {
        private GameController game;
        private Presentation presentation;
        private PlayUi playUi;
        private ResearchUi researchUi;
        private HelpUi helpUi;
        private string dir;
        private bool toggleModes;
        private readonly List<string> log = new List<string>();
        private int violations, failures, framesChecked, enemyRendererChecks, graveyardChecks, toggles;
        private bool combatShot, helpDone, modeRoundTripDone, researchShotsDone;
        private CpuPlayer humanDriver;

        public void Begin(GameController controller, string outputDir, bool toggle)
        {
            game = controller;
            presentation = GetComponent<Presentation>();
            playUi = GetComponent<PlayUi>();
            researchUi = GetComponent<ResearchUi>();
            helpUi = GetComponent<HelpUi>();
            dir = outputDir;
            toggleModes = toggle;
            Directory.CreateDirectory(dir);
            StartCoroutine(Run());
        }

        private void Fail(string message)
        {
            failures++;
            log.Add("FAIL: " + message);
        }

        private IEnumerator Run()
        {
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            if (presentation.Mode != PresentationMode.Play) Fail("start-up mode is not 対戦");
            else log.Add("start-up mode: 対戦 (Play)");
            yield return new WaitForSeconds(1.0f);
            CheckLogo();
            CheckFaceTextures();
            if (!toggleModes) yield return Shot("01_play_setup.png");
            CheckPicking();
            yield return CheckPlacementEditing();
            game.AutoArrange();   // back to the seed formation so the run stays reproducible
            game.StartGame();
            humanDriver = new CpuPlayer(GameController.Human, game.Settings.PlayerFormationSeed, 777);
            game.PlyFinished += OnPly;
            yield return new WaitForSeconds(0.4f);
            if (!toggleModes) yield return Shot("02_play_start.png");

            float deadline = Time.realtimeSinceStartup + 1200f;
            while (!game.IsFinished && Time.realtimeSinceStartup < deadline)
            {
                if (!toggleModes && !combatShot && game.Phase == Phase.Animating && game.Popups.Count > 0 && presentation.Mode == PresentationMode.Play)
                {
                    combatShot = true;
                    yield return Shot("03_play_combat.png");
                }
                if (!helpDone && game.Ply >= 6 && game.Phase == Phase.CpuThinking) yield return CheckHelpPauses();
                if (!modeRoundTripDone && game.Ply >= 10 && game.Phase == Phase.PlayerTurn) yield return CheckModeRoundTrip();
                if (game.Phase == Phase.PlayerTurn && !game.Paused)
                {
                    // Play the human side through the click handler: select the piece, then the target.
                    var cmd = humanDriver.Decide(game.View).Chosen.Command;
                    int from = game.View.OwnById(cmd.PieceId).Node;
                    game.ClickNode(from);
                    if (game.SelectedNode != from) Fail("click did not select own piece at " + BoardGraph.Describe(from));
                    game.ClickNode(cmd.To);
                    if (game.Phase == Phase.PlayerTurn) Fail("click on a legal target did not move: " + cmd);
                }
                yield return null;
            }
            // IsFinished turns true when the referee applies the last move; wait for its animation and loss-area update.
            while (game.Phase != Phase.Finished && Time.realtimeSinceStartup < deadline) yield return null;
            if (presentation.Mode != PresentationMode.Play) presentation.SetMode(PresentationMode.Play);
            yield return new WaitForSeconds(0.8f);
            CheckGraveyard();
            if (!toggleModes) yield return Shot("09_play_final.png");
            Finish();
        }

        private void OnPly(ObservedMove r)
        {
            log.Add("TURN " + r.Ply + " " + r.Mover + " #" + r.PieceId + " " + BoardGraph.Describe(r.From) + "->" + BoardGraph.Describe(r.To)
                + (r.Combat != null ? " combat " + r.Combat.Outcome : ""));
            CheckGraveyard();
            if (toggleModes)
            {
                // Flip the presentation constantly, including during CPU thinking and animations.
                presentation.Toggle();
                toggles++;
                return;
            }
            if (r.Ply > 24) game.Settings.EffectSpeed = 4f;   // keep the run short once the effects are recorded
            if (!researchShotsDone && r.Ply == 30) StartCoroutine(ResearchShots());
            if (r.Ply == 60) StartCoroutine(Shot("08_play_losses.png"));
        }

        private IEnumerator ResearchShots()
        {
            researchShotsDone = true;
            presentation.SetMode(PresentationMode.Research);
            researchUi.MonitorVisible = true;
            researchUi.MonitorTab = 0;
            yield return WaitFrames(8);
            yield return Shot("06_research_decision.png");
            researchUi.MonitorTab = 1;
            yield return WaitFrames(8);
            yield return Shot("07_research_beliefs.png");
            presentation.SetMode(PresentationMode.Play);
        }

        private static IEnumerator WaitFrames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>対戦→研究→対戦 while the game is idle (human to move): nothing in the session may change.</summary>
        private IEnumerator CheckModeRoundTrip()
        {
            modeRoundTripDone = true;
            string before = game.Session.Fingerprint();
            var rng = Random.state;
            int ply = game.Ply;
            presentation.SetMode(PresentationMode.Research);
            yield return WaitFrames(15);
            if (presentation.Mode != PresentationMode.Research) Fail("mode switch to 研究 failed");
            presentation.SetMode(PresentationMode.Play);
            yield return WaitFrames(15);
            string after = game.Session.Fingerprint();
            bool sameRng = Random.state.Equals(rng);
            if (before != after || ply != game.Ply) Fail("mode round trip changed the game state " + before + " -> " + after);
            if (!sameRng) Fail("mode round trip changed UnityEngine.Random.state");
            log.Add("mode round trip 対戦→研究→対戦 at ply " + ply + ": fingerprint " + before + " = " + after + ", Random.state unchanged = " + sameRng);
        }

        /// <summary>While 「あそびかた」 is open the game must not advance, even if the CPU was thinking.</summary>
        private IEnumerator CheckHelpPauses()
        {
            helpDone = true;
            string before = game.Session.Fingerprint();
            int ply = game.Ply, reports = game.Cpu.Reports.Count;
            var phase = game.Phase;
            presentation.OpenHelp();
            helpUi.Selected = PieceType.Engineer;
            yield return new WaitForSecondsRealtime(1.5f);
            if (!toggleModes) yield return Shot("04_help_engineer.png");
            helpUi.Selected = PieceType.Airplane;
            yield return new WaitForSecondsRealtime(0.5f);
            if (!toggleModes) yield return Shot("05_help_airplane.png");
            helpUi.Selected = PieceType.Flag;
            yield return new WaitForSecondsRealtime(0.3f);
            if (!toggleModes) yield return Shot("05b_help_flag.png");
            helpUi.Selected = PieceType.Tank;
            yield return new WaitForSecondsRealtime(0.3f);
            if (!toggleModes) yield return Shot("05c_help_tank.png");
            string during = game.Session.Fingerprint();
            if (during != before || game.Ply != ply || game.Cpu.Reports.Count != reports || game.Phase != phase)
                Fail("game advanced while 「あそびかた」 was open");
            presentation.CloseHelp();
            log.Add("help pause: open for ~2.6 s during " + phase + " at ply " + ply + ", state unchanged = " + (during == before) + ", timeScale restored = " + (Time.timeScale == 1f));
        }

        private void CheckGraveyard()
        {
            graveyardChecks++;
            var view = game.View;
            var g = game.Graveyard;
            int ownDead = view.Own.Count(p => !p.Alive), enemyDead = view.Enemy.Count(p => !p.Alive);
            if (g.OwnViews.Count != ownDead) Fail("own loss area shows " + g.OwnViews.Count + " but " + ownDead + " died");
            if (g.EnemyViews.Count != enemyDead) Fail("enemy loss area shows " + g.EnemyViews.Count + " but " + enemyDead + " died");
            var kinds = g.OwnViews.Select(v => (int)view.OwnById(v.Id).Type).ToList();
            for (int i = 1; i < kinds.Count; i++) if (kinds[i] < kinds[i - 1]) Fail("own losses not sorted by kind");
            if (!g.EnemyIdsShown.SequenceEqual(GameSession.EnemyDeathOrder(view))) Fail("enemy losses not in removal order");
            for (int i = 0; i < g.EnemyViews.Count; i++)
            {
                var v = g.EnemyViews[i];
                if ((v.transform.localPosition - GraveyardView.Slot(i, true)).sqrMagnitude > 1e-6f) Fail("enemy loss slot is not a function of the removal index");
                if (v.Materials.Any(m => m.mainTexture != GameAssets.Back && m.mainTexture != GameAssets.Side)) Fail("enemy loss piece shows a face");
            }
        }

        private void CheckLogo()
        {
            var r = playUi.LogoScreenRect;
            float topBar = 72f * UiKit.Scale;
            bool ok = playUi.Logo != null && playUi.Logo.name == "title_logo" && r.width > 50 && r.x >= 0 && r.xMax <= Screen.width && r.y >= 0 && r.yMax <= topBar + 0.5f;
            if (!ok) Fail("title logo missing or outside the top bar: " + r);
            log.Add("title logo " + (playUi.Logo != null ? playUi.Logo.width + "x" + playUi.Logo.height : "null") + " drawn at " + r + " (top bar " + topBar + " px)");
        }

        private void CheckFaceTextures()
        {
            int ok = 0;
            foreach (var t in PieceCatalog.AllTypes)
            {
                var tex = GameAssets.Face(t);
                if (tex != null && tex.name == "piece_" + t && tex.width >= 256) ok++; else Fail("face texture for " + t);
            }
            var own = game.PieceViews.Where(v => v.IsOwn).ToList();
            foreach (var v in own)
                if (!v.Materials[0].mainTexture.name.StartsWith("piece_") || v.Materials[0].mainTexture.name == "piece_back") Fail("own piece without face texture");
            log.Add("face textures: " + ok + "/16 loaded, " + own.Count + " own pieces show their face");
        }

        private void LateUpdate()
        {
            if (game == null) return;
            framesChecked++;
            var back = GameAssets.Back;
            foreach (var v in game.PieceViews.Concat(game.Graveyard.EnemyViews))
            {
                if (v.IsOwn) continue;
                foreach (var m in v.Materials)
                {
                    enemyRendererChecks++;
                    var tex = m.mainTexture;
                    if (tex != back && tex != GameAssets.Side)
                    {
                        violations++;
                        log.Add("VIOLATION: enemy piece #" + v.Number + " uses texture " + (tex != null ? tex.name : "null"));
                    }
                }
            }
        }

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
            sb.AppendLine("AF-MilitaryShogi autotest " + GameBootstrap.Version + (toggleModes ? " (-toggleModes)" : ""));
            sb.AppendLine("seeds P/C/D = " + game.Settings.PlayerFormationSeed + "/" + game.Settings.CpuFormationSeed + "/" + game.Settings.CpuDecisionSeed);
            sb.AppendLine("CPU = " + CpuProfile.StrengthName(game.Settings.Strength) + " / " + CpuProfile.TemperamentName(game.Settings.Temperament) + " / style " + FormationStyles.JapaneseName(game.Cpu.Style));
            sb.AppendLine("result = " + game.ResultText() + " after " + game.Ply + " plies");
            sb.AppendLine("frames checked = " + framesChecked + ", enemy material checks = " + enemyRendererChecks + ", violations = " + violations);
            sb.AppendLine("CPU decisions = " + game.Cpu.Reports.Count + ", avg think ms = " + (game.Cpu.Reports.Count > 0 ? game.Cpu.Reports.Average(r => r.ThinkMilliseconds).ToString("0.0") : "-"));
            sb.AppendLine("combats = " + game.Combats.Count + ", loss-area checks = " + graveyardChecks + ", mode toggles = " + toggles + ", failures = " + failures);
            sb.AppendLine("FINAL_FINGERPRINT=" + game.Session.Fingerprint());
            sb.AppendLine(pass ? "PASS" : "FAIL");
            sb.AppendLine();
            foreach (var l in log) sb.AppendLine(l);
            File.WriteAllText(Path.Combine(dir, "autotest_report.txt"), sb.ToString(), Encoding.UTF8);
            Application.Quit(pass ? 0 : 1);
        }
    }
}
