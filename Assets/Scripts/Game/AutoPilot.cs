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
    /// Automated in-player verification (-autotest DIR [-toggleModes] [-expectPersisted] -dataDir DIR).
    /// The game itself (seeds, human driver, CPU) is identical to 1.1.1, so its final fingerprint must
    /// match 1.1.1's and must not depend on anything the presentation does.
    ///  - start-up is 対戦; logo; face textures; picking; placement editing through the click handler;
    ///  - presets: 5 slots, default and remembered names, exact placement round trip, invalid data
    ///    rejected, CPU seeds/formation untouched; persistence across a restart (run B checks run A);
    ///  - UI state stress test (setup and in game): hundreds of random mode/overlay/setting operations,
    ///    with invariants after each (no UI exception, no hidden overlay blocking input, pause follows
    ///    the help exactly, active UI matches the mode, board operable, game state unchanged);
    ///  - clock: starts at 対局開始, runs with 設定 open and in 研究, stops only in あそびかた, frozen at the end;
    ///  - combat effects 通常/簡易 at ×1/×2/×4 with sounds in sync with the visual events; SE on/off, volume;
    ///  - loss areas (counts, order, back faces, common transform/shadow rules, layout, heading gap);
    ///  - tooltip: observation setting, research reveal shows the truth and reverts; never in 対戦;
    ///  - every frame: enemy faces appear only in 研究 with 「CPU駒の正体を表示」 on, never in loss areas;
    ///  - screenshots; writes autotest_report.txt (exit code 0 = pass).
    /// </summary>
    public sealed partial class AutoPilot : MonoBehaviour
    {
        private GameController game;
        private Presentation presentation;
        private PlayUi playUi;
        private ResearchUi researchUi;
        private HelpUi helpUi;
        private string dir;
        private bool toggleModes, expectPersisted;
        private readonly List<string> log = new List<string>();
        private int violations, failures, framesChecked, enemyRendererChecks, graveyardChecks, toggles;
        private bool combatShot, helpDone, modeRoundTripDone, researchShotsDone, clockDone, stressDone, earlyShot;
        private CpuPlayer humanDriver;
        public const string PersistedPresetName = "自動検証の配置";
        public const int PersistedVolume = 37;

        public void Begin(GameController controller, string outputDir, bool toggle)
        {
            game = controller;
            presentation = GetComponent<Presentation>();
            playUi = GetComponent<PlayUi>();
            researchUi = GetComponent<ResearchUi>();
            helpUi = GetComponent<HelpUi>();
            dir = outputDir;
            toggleModes = toggle;
            expectPersisted = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-expectPersisted") >= 0;
            Directory.CreateDirectory(dir);
            StartCoroutine(Run());
        }

        private void Fail(string message)
        {
            failures++;
            log.Add("FAIL: " + message);
        }

        private bool Shots { get { return !toggleModes; } }

        private IEnumerator Run()
        {
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            if (presentation.Mode != PresentationMode.Play) Fail("start-up mode is not 対戦");
            else log.Add("start-up mode: 対戦 (Play)");
            if (expectPersisted) CheckPersistedFromPreviousRun();
            yield return new WaitForSeconds(1.0f);
            CheckLogo();
            CheckFaceTextures();
            if (Shots) yield return Shot("01_play_setup.png");

            // Reference state of this run's setup, restored after tests that intentionally change it.
            var seeds = new[] { game.Settings.PlayerFormationSeed, game.Settings.CpuFormationSeed, game.Settings.CpuDecisionSeed };
            var baseline = SnapshotSettings();
            yield return CheckPresets();
            if (!expectPersisted) PersistForNextRun();
            yield return StressTest("setup", 160, true);
            RestoreSetup(seeds);
            RestoreSettings(baseline);
            yield return WaitFrames(3);

            CheckPicking();
            yield return CheckPlacementEditing();
            game.AutoArrange();   // back to the seed formation so the run stays reproducible
            if (game.ElapsedSeconds != 0) Fail("clock ran before 対局開始");
            game.StartGame();
            if (game.ElapsedSeconds > 0.25) Fail("clock did not start at 対局開始");
            yield return CheckLayouts();
            yield return CheckTooltipDrawing();
            humanDriver = new CpuPlayer(GameController.Human, game.Settings.PlayerFormationSeed, 777);
            game.PlyFinished += OnPly;
            yield return new WaitForSeconds(0.4f);

            float deadline = Time.realtimeSinceStartup + 1500f;
            while (!game.IsFinished && Time.realtimeSinceStartup < deadline)
            {
                if (Shots && !earlyShot && game.Ply >= 4 && game.Phase == Phase.PlayerTurn) { earlyShot = true; yield return Shot("03_play_early.png"); }
                if (Shots && !combatShot && game.Phase == Phase.Animating && game.View.History.Count > 0 && game.View.History.Last().Combat != null && presentation.Mode == PresentationMode.Play)
                {
                    combatShot = true;
                    yield return Shot("03b_play_combat.png");
                }
                if (!helpDone && game.Ply >= 6 && game.Phase == Phase.CpuThinking) yield return CheckHelpPauses();
                if (!modeRoundTripDone && game.Ply >= 10 && game.Phase == Phase.PlayerTurn) yield return CheckModeRoundTrip();
                if (!clockDone && game.Ply >= 12 && game.Phase == Phase.PlayerTurn) yield return CheckClock();
                if (!stressDone && game.Ply >= 14 && game.Phase == Phase.PlayerTurn) { stressDone = true; var b = SnapshotSettings(); yield return StressTest("in-game", 160, false); RestoreSettings(b); }
                if (!researchShotsDone && game.Ply >= 20 && game.Phase == Phase.PlayerTurn) yield return ResearchShots();
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
            double end = game.ElapsedSeconds;
            yield return new WaitForSecondsRealtime(1.0f);
            if (System.Math.Abs(game.ElapsedSeconds - end) > 1e-6) Fail("clock kept running after the end");
            log.Add("clock frozen at the end: " + UiKit.FormatClock(end));
            CheckGraveyard();
            if (Shots) yield return Shot("10_play_final.png");
            if (playUi.ResultDrawCount == 0) Fail("end-game result modal was not drawn");
            if (playUi.LastResultText == null || !playUi.LastResultText.Contains("TURN") || !playUi.LastResultText.Contains("経過")) Fail("result modal lacks TURN/経過: " + playUi.LastResultText);
            CheckSoundSync();
            yield return CheckSoundSettings();
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
            if (r.Combat != null) RecordEffectCombo();
            // Cycle effect style and speed so combats are seen at ×1/×2/×4 in both 通常 and 簡易.
            float[] speeds = { 1f, 2f, 4f };
            game.Settings.EffectSpeed = speeds[(r.Ply / 2) % 3];
            game.Settings.Effect = (r.Ply / 6) % 2 == 0 ? EffectMode.Normal : EffectMode.Simple;
        }

        private static IEnumerator WaitFrames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        // ------------------------------------------------------------------
        // Mode round trip, help pause, clock
        // ------------------------------------------------------------------

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

        /// <summary>While 「あそびかた」 is open the game (and the clock) must not advance, even if the CPU was thinking.</summary>
        private IEnumerator CheckHelpPauses()
        {
            helpDone = true;
            string before = game.Session.Fingerprint();
            int ply = game.Ply, reports = game.Cpu.Reports.Count;
            var phase = game.Phase;
            double clock = game.ElapsedSeconds;
            presentation.OpenHelp();
            helpUi.Selected = PieceType.Engineer;
            yield return new WaitForSecondsRealtime(1.5f);
            if (Shots) yield return Shot("05_help_engineer.png");
            helpUi.Selected = PieceType.Airplane;
            yield return new WaitForSecondsRealtime(0.5f);
            if (Shots) yield return Shot("05b_help_airplane.png");
            string during = game.Session.Fingerprint();
            if (during != before || game.Ply != ply || game.Cpu.Reports.Count != reports || game.Phase != phase)
                Fail("game advanced while 「あそびかた」 was open");
            if (System.Math.Abs(game.ElapsedSeconds - clock) > 1e-6) Fail("clock ran while 「あそびかた」 was open");
            presentation.CloseHelp();
            log.Add("help pause: open for ~2 s during " + phase + " at ply " + ply + ", state unchanged = " + (during == before) + ", clock stopped = " + (game.ElapsedSeconds == clock) + ", timeScale restored = " + (Time.timeScale == 1f));
        }

        /// <summary>The clock runs with 設定 open and in 研究 (also with the monitor/history open); it stops in あそびかた.</summary>
        private IEnumerator CheckClock()
        {
            clockDone = true;
            double t0 = game.ElapsedSeconds;
            presentation.SettingsOpen = true;
            yield return new WaitForSecondsRealtime(1.0f);
            if (Shots) yield return Shot("09_settings.png");
            double t1 = game.ElapsedSeconds;
            presentation.SettingsOpen = false;
            presentation.SetMode(PresentationMode.Research);
            presentation.MonitorOpen = true;
            researchUi.HistoryVisible = true;
            yield return new WaitForSecondsRealtime(1.0f);
            double t2 = game.ElapsedSeconds;
            presentation.MonitorOpen = false;
            researchUi.HistoryVisible = false;
            presentation.SetMode(PresentationMode.Play);
            presentation.OpenHelp();
            yield return new WaitForSecondsRealtime(1.0f);
            double t3 = game.ElapsedSeconds;
            presentation.CloseHelp();
            bool settingsRuns = t1 - t0 > 0.8, researchRuns = t2 - t1 > 0.8, helpStops = System.Math.Abs(t3 - t2) < 0.05;
            if (!settingsRuns) Fail("clock did not run with 設定 open: " + (t1 - t0));
            if (!researchRuns) Fail("clock did not run in 研究モード: " + (t2 - t1));
            if (!helpStops) Fail("clock ran in あそびかた: " + (t3 - t2));
            if (UiKit.FormatClock(65) != "1:05" || UiKit.FormatClock(18 * 60 + 42) != "18:42" || UiKit.FormatClock(3600 + 23 * 60 + 45) != "1:23:45") Fail("clock format");
            log.Add("clock: 設定 +" + (t1 - t0).ToString("0.00") + " s, 研究 +" + (t2 - t1).ToString("0.00") + " s, あそびかた +" + (t3 - t2).ToString("0.000") + " s; formats 1:05 / 18:42 / 1:23:45");
        }

        // ------------------------------------------------------------------
        // Research screenshots
        // ------------------------------------------------------------------

        private IEnumerator ResearchShots()
        {
            researchShotsDone = true;
            presentation.SetMode(PresentationMode.Research);
            presentation.MonitorOpen = false;
            yield return WaitFrames(10);
            if (!game.Graveyard.gameObject.activeSelf) Fail("loss areas hidden in 研究 with the monitor closed");
            if (Shots) yield return Shot("06_research_monitor_off.png");
            presentation.MonitorOpen = true;
            researchUi.MonitorTab = 0;
            yield return WaitFrames(10);
            if (game.Graveyard.gameObject.activeSelf) Fail("loss areas shown while the thinking monitor is open");
            if (Shots) yield return Shot("07_research_monitor_on.png");
            researchUi.MonitorTab = 1;
            presentation.RevealCpuPieces = true;
            yield return WaitFrames(10);
            if (!game.EnemiesRevealed) Fail("research reveal did not show CPU pieces");
            if (Shots) yield return Shot("08_research_reveal_on.png");
            presentation.RevealCpuPieces = false;
            presentation.MonitorOpen = false;
            researchUi.MonitorTab = 0;
            presentation.SetMode(PresentationMode.Play);
            yield return WaitFrames(5);
            if (game.EnemiesRevealed) Fail("CPU pieces still revealed after leaving 研究");
            log.Add("research: loss areas shown with monitor closed, hidden with monitor open; reveal on/off OK");
        }

        // ------------------------------------------------------------------
        // Presets and persistence
        // ------------------------------------------------------------------

        private IEnumerator CheckPresets()
        {
            var cpuBefore = game.Session.CpuFormation.Signature();
            var seedsBefore = game.Settings.CpuFormationSeed + "/" + game.Settings.CpuDecisionSeed;
            var cpuObj = game.Cpu;
            var own = game.PlayerFormation.Clone();
            var panel = playUi.Presets;
            panel.SelectedSlot = 2;
            panel.BeginSave();
            if (panel.NameDraft != game.Presets.NameForSave(2)) Fail("save dialog not prefilled with the current name");
            bool firstTime = game.Presets.Slot(2).IsEmpty;
            if (firstTime && panel.NameDraft != "プリセット3") Fail("first save should default to プリセット3: " + panel.NameDraft);
            yield return WaitFrames(3);
            if (Shots) yield return Shot("02_preset_ui.png");
            panel.CommitSave();                                   // unedited name
            panel.BeginSave();
            panel.NameDraft = "地雷籠城（検証）";
            panel.CommitSave();
            panel.BeginSave();
            if (panel.NameDraft != "地雷籠城（検証）") Fail("changed name is not the next default: " + panel.NameDraft);
            panel.Saving = false;

            game.Settings.PlayerFormationSeed += 17;
            game.AutoArrange();
            if (game.PlayerFormation.Signature() == own.Signature()) Fail("test needs a different arrangement");
            panel.Load();
            if (game.PlayerFormation.Signature() != own.Signature()) Fail("loaded preset does not reproduce the saved placement");
            if (game.Session.CpuFormation.Signature() != cpuBefore || !ReferenceEquals(game.Cpu, cpuObj) || game.Settings.CpuFormationSeed + "/" + game.Settings.CpuDecisionSeed != seedsBefore)
                Fail("preset load changed CPU seeds/formation/player");

            // Research mode uses the same presets.
            presentation.SetMode(PresentationMode.Research);
            researchUi.Presets.SelectedSlot = 2;
            game.AutoArrange();
            researchUi.Presets.Load();
            if (game.PlayerFormation.Signature() != own.Signature()) Fail("research mode cannot load the same preset");
            presentation.SetMode(PresentationMode.Play);

            // Invalid data on disk is rejected, never applied.
            string good = File.ReadAllText(UserData.PresetPath, Encoding.UTF8);
            File.WriteAllText(UserData.PresetPath, good.Replace("General", "Dragon"), new UTF8Encoding(false));
            var reread = UserData.LoadPresets();
            if (!reread.Slot(2).IsEmpty || reread.LoadErrors.Count == 0) Fail("corrupt preset slot was not rejected");
            File.WriteAllText(UserData.PresetPath, "AFMS-PRESETS 99\n" + good, new UTF8Encoding(false));
            if (UserData.LoadPresets().LoadErrors.Count == 0) Fail("unknown preset format version accepted");
            File.WriteAllText(UserData.PresetPath, good, new UTF8Encoding(false));
            if (UserData.LoadPresets().Load(2, Side.South).Signature() != own.Signature()) Fail("preset file does not round-trip");
            log.Add("presets: default/remembered names, exact placement round trip, both modes, invalid data rejected, CPU untouched");
        }

        /// <summary>Run A leaves a named preset and a volume; run B (a new process) must find them.</summary>
        private void PersistForNextRun()
        {
            game.Presets.Save(3, PersistedPresetName, game.PlayerFormation);
            UserData.SavePresets(game.Presets);
            // The volume is saved at the very end (CheckSoundSettings), after all tests that restore settings.
            File.WriteAllText(Path.Combine(UserData.Directory, "expected_preset.txt"), game.PlayerFormation.Signature());
            log.Add("persist for restart: preset 4 = " + PersistedPresetName + ", volume " + PersistedVolume);
        }

        private void CheckPersistedFromPreviousRun()
        {
            var slot = game.Presets.Slot(3);
            string expected = File.Exists(Path.Combine(UserData.Directory, "expected_preset.txt")) ? File.ReadAllText(Path.Combine(UserData.Directory, "expected_preset.txt")) : null;
            if (slot.IsEmpty || slot.Name != PersistedPresetName || expected == null || game.Presets.Load(3, Side.South).Signature() != expected)
                Fail("preset did not survive the restart");
            if (game.Settings.SfxVolume != PersistedVolume) Fail("volume did not survive the restart: " + game.Settings.SfxVolume);
            log.Add("after restart: preset 「" + slot.Name + "」 and volume " + game.Settings.SfxVolume + " restored");
        }

        // ------------------------------------------------------------------
        // Settings snapshots
        // ------------------------------------------------------------------

        private sealed class SettingsSnapshot
        {
            public EffectMode Effect; public float Speed; public bool Numbers, Tooltip, Sfx; public int Volume;
            public bool Monitor, Reveal, History;
        }

        private SettingsSnapshot SnapshotSettings()
        {
            var s = game.Settings;
            return new SettingsSnapshot
            {
                Effect = s.Effect, Speed = s.EffectSpeed, Numbers = s.ShowEnemyNumbers, Tooltip = s.ObservationTooltip, Sfx = s.SfxOn, Volume = s.SfxVolume,
                Monitor = presentation.MonitorOpen, Reveal = presentation.RevealCpuPieces, History = researchUi.HistoryVisible,
            };
        }

        private void RestoreSettings(SettingsSnapshot b)
        {
            var s = game.Settings;
            s.Effect = b.Effect; s.EffectSpeed = b.Speed; s.ShowEnemyNumbers = b.Numbers; s.ObservationTooltip = b.Tooltip; s.SfxOn = b.Sfx; s.SfxVolume = b.Volume;
            presentation.MonitorOpen = b.Monitor; presentation.RevealCpuPieces = b.Reveal; researchUi.HistoryVisible = b.History;
            presentation.SettingsOpen = false;
            if (presentation.HelpOpen) presentation.CloseHelp();
            presentation.SetMode(PresentationMode.Play);
            UserData.SaveSettings(s);
        }

        private void RestoreSetup(int[] seeds)
        {
            game.Settings.PlayerFormationSeed = seeds[0];
            game.Settings.CpuFormationSeed = seeds[1];
            game.Settings.CpuDecisionSeed = seeds[2];
            game.RefreshCpu();
            game.AutoArrange();
        }

        // ------------------------------------------------------------------
        // Loss areas, logo, textures, per-frame face check, picking, editing
        // ------------------------------------------------------------------

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
            CheckGraveTransforms();
        }

        /// <summary>Both loss areas follow the same physical rules: on the table, same orientation, scale and shadows.</summary>
        private void CheckGraveTransforms()
        {
            foreach (var v in game.Graveyard.OwnViews.Concat(game.Graveyard.EnemyViews))
            {
                var t = v.transform;
                if (Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.01f || Mathf.Abs(t.localScale.x - GraveyardView.Scale) > 1e-4f
                    || Mathf.Abs(t.localPosition.y - GraveyardView.TableY) > 1e-4f
                    || v.Renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.On || !v.Renderer.receiveShadows)
                    Fail("loss piece transform/shadow differs from the common rule (#" + v.Number + (v.IsOwn ? " own" : " enemy") + ")");
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
            // Board: faces only in 研究 with the reveal switch on. Loss areas: never.
            bool revealAllowed = presentation.EnemyRevealActive;
            foreach (var v in game.PieceViews)
            {
                if (v.IsOwn || revealAllowed) continue;
                CheckBack(v, back, "board");
            }
            foreach (var v in game.Graveyard.EnemyViews) CheckBack(v, back, "loss area");
        }

        private void CheckBack(PieceView v, Texture back, string where)
        {
            foreach (var m in v.Materials)
            {
                enemyRendererChecks++;
                var tex = m.mainTexture;
                if (tex != back && tex != GameAssets.Side)
                {
                    violations++;
                    if (violations < 20) log.Add("VIOLATION: enemy piece #" + v.Number + " (" + where + ") uses texture " + (tex != null ? tex.name : "null") + " in " + presentation.Mode);
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
            sb.AppendLine("AF-MilitaryShogi autotest " + GameBootstrap.Version + (toggleModes ? " (-toggleModes)" : "") + (expectPersisted ? " (-expectPersisted)" : ""));
            sb.AppendLine("seeds P/C/D = " + game.Settings.PlayerFormationSeed + "/" + game.Settings.CpuFormationSeed + "/" + game.Settings.CpuDecisionSeed);
            sb.AppendLine("CPU = " + CpuProfile.StrengthName(game.Settings.Strength) + " / " + CpuProfile.TemperamentName(game.Settings.Temperament) + " / style " + FormationStyles.JapaneseName(game.Cpu.Style));
            sb.AppendLine("result = " + game.ResultText() + " after " + game.Ply + " plies, clock " + UiKit.FormatClock(game.ElapsedSeconds));
            sb.AppendLine("frames checked = " + framesChecked + ", enemy material checks = " + enemyRendererChecks + ", violations = " + violations);
            sb.AppendLine("CPU decisions = " + game.Cpu.Reports.Count + ", avg think ms = " + (game.Cpu.Reports.Count > 0 ? game.Cpu.Reports.Average(r => r.ThinkMilliseconds).ToString("0.0") : "-"));
            sb.AppendLine("combats = " + game.Combats.Count + ", loss-area checks = " + graveyardChecks + ", mode toggles = " + toggles + ", UI errors = " + UiGuard.ErrorCount + ", watchdog repairs = " + presentation.WatchdogRepairs + ", failures = " + failures);
            sb.AppendLine("FINAL_FINGERPRINT=" + game.Session.Fingerprint());
            sb.AppendLine(pass ? "PASS" : "FAIL");
            sb.AppendLine();
            foreach (var l in log) sb.AppendLine(l);
            File.WriteAllText(Path.Combine(dir, "autotest_report.txt"), sb.ToString(), Encoding.UTF8);
            Application.Quit(pass ? 0 : 1);
        }
    }
}
