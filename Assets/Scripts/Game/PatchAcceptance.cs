using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MilitaryShogi.Observation;
using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    public sealed partial class AutoPilot
    {
        private readonly HashSet<string> effectCombos = new HashSet<string>();

        private static bool ContainsRect(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;
        }

        // ------------------------------------------------------------------
        // Layout: board + both loss areas as one group, centred in the play area
        // ------------------------------------------------------------------

        private static PlayerView LossFixture(Formation formation, int ownLost, int enemyLost, out ObservedMove[] history)
        {
            var own = formation.Pieces.Select((p, i) => new OwnPieceView(i, i + 1, p.Value, i < ownLost ? -1 : p.Key, p.Key)).ToArray();
            var enemy = Enumerable.Range(0, 31).Select(i => new EnemyPieceView(100 + i, i + 1, i < enemyLost ? -1 : 32 + i, 32 + i)).ToArray();
            var owners = Enumerable.Repeat(MoveRules.Empty, BoardGraph.NodeCount).ToArray();
            history = Enumerable.Range(0, enemyLost).Select(i => new ObservedMove(i + 1, GameController.Human, i, 0, 32, new[] { 32 }, 0, owners,
                new ObservedCombat(i, 100 + i, CombatOutcome.Tie))).ToArray();
            return new PlayerView(GameController.Human, 31, GameController.Human, GameStatus.Playing, null, EndReason.None, own, enemy, history, owners);
        }

        private IEnumerator CheckLayouts()
        {
            game.SetPaused(true);
            string before = game.Session.Fingerprint();
            ObservedMove[] history;
            // Rendering-only fixtures (no session or CPU mutation): 22 own losses, then 31 on each side.
            game.Graveyard.Sync(LossFixture(game.PlayerFormation, 22, 9, out history));
            yield return WaitFrames(4);
            CheckGraveTransforms();
            if (Shots) yield return Shot("04_play_own_losses_22.png");
            var fixture = LossFixture(game.PlayerFormation, 31, 31, out history);
            game.Graveyard.Sync(fixture);
            yield return WaitFrames(3);
            CheckGraveTransforms();
            foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
            {
                Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(0.4f);
                foreach (var mode in new[] { PresentationMode.Play, PresentationMode.Research })
                    foreach (bool monitor in mode == PresentationMode.Play ? new[] { false } : new[] { false, true })
                    {
                        presentation.SetMode(mode);
                        presentation.MonitorOpen = monitor;
                        yield return WaitFrames(6);
                        CheckPicking();
                        var cam = game.MainCamera;
                        if (Mathf.Abs(cam.transform.eulerAngles.x - CameraFit.Pitch) > 0.01f || Mathf.Abs(cam.fieldOfView - 36f) > 0.01f) Fail("camera perspective changed");
                        bool graves = presentation.GraveyardsVisible;
                        if (graves != game.Graveyard.gameObject.activeSelf) Fail("loss-area visibility does not follow the rule in " + mode + " monitor=" + monitor);
                        var e = CameraFit.Extent(cam, CameraFit.GroupCorners(graves));
                        if (e.xMin < -0.001f || e.xMax > 1.001f || e.yMin < -0.001f || e.yMax > 1.001f) Fail("visual group outside its area " + e);
                        float centre = (e.yMin + e.yMax) / 2f, hcentre = (e.xMin + e.xMax) / 2f;
                        if (Mathf.Abs(centre - 0.5f) > 0.01f) Fail("visual group not vertically centred: " + centre);
                        if (Mathf.Abs(hcentre - 0.5f) > 0.02f) Fail("visual group not horizontally centred: " + hcentre);
                        bool fills = e.height > 0.9f || e.width > 0.9f;
                        if (!fills) Fail("visual group does not use the area: " + e);
                        if (mode == PresentationMode.Play) CheckPlayLossArea(cam, fixture, history);
                        log.Add("layout " + mode + (mode == PresentationMode.Research ? (monitor ? " monitor ON" : " monitor OFF") : "") + " " + Screen.width + "x" + Screen.height
                            + ": group x " + e.xMin.ToString("0.000") + "–" + e.xMax.ToString("0.000") + ", y " + e.yMin.ToString("0.000") + "–" + e.yMax.ToString("0.000")
                            + " (centre " + centre.ToString("0.000") + "), losses " + (graves ? "shown" : "hidden") + ", picking 66, pitch/FOV fixed");
                        if (Shots && resolution.x == 1600 && mode == PresentationMode.Play) yield return Shot("04b_play_31_losses.png");
                    }
            }
            game.Graveyard.Clear();
            presentation.MonitorOpen = false;
            presentation.SetMode(PresentationMode.Play);
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.4f);
            if (game.Session.Fingerprint() != before) Fail("layout fixture changed the game session");
            game.SetPaused(false);
        }

        private void CheckPlayLossArea(Camera cam, PlayerView fixture, ObservedMove[] history)
        {
            var area = cam.pixelRect;
            area.y = Screen.height - area.yMax;
            foreach (var v in game.Graveyard.OwnViews.Concat(game.Graveyard.EnemyViews))
            {
                var r = EnemyTooltip.ScreenBounds(cam, v.WorldBounds, 1);
                if (!ContainsRect(area, r)) Fail("31-loss fixture outside viewport " + r);
            }
            if (game.Graveyard.OwnViews.Count != 31 || game.Graveyard.EnemyViews.Count != 31) Fail("31-loss fixture count");
            var kinds = game.Graveyard.OwnViews.Select(v => fixture.OwnById(v.Id).Type).ToArray();
            if (!kinds.SequenceEqual(kinds.OrderBy(t => t))) Fail("31-loss fixture own sort order");
            if (!game.Graveyard.EnemyIdsShown.SequenceEqual(history.Select(m => m.Combat.DefenderId))) Fail("31-loss fixture enemy removal order");
            // Both blocks are mirror images: same rows, same depth, same size on screen (within perspective).
            for (int i = 0; i < 3; i++)
            {
                var a = game.Graveyard.OwnViews[i].transform.position;
                var b = game.Graveyard.EnemyViews[i].transform.position;
                if (Mathf.Abs(a.y - b.y) > 1e-4f || Mathf.Abs(a.z - b.z) > 1e-4f) Fail("loss blocks at different height/depth");
            }
            // The heading sits clearly above the first row: gap ≥ 40% of a piece's on-screen height.
            for (int side = 0; side < 2; side++)
            {
                var first = (side == 0 ? game.Graveyard.OwnViews : game.Graveyard.EnemyViews)[0];
                var pr = EnemyTooltip.ScreenBounds(cam, first.WorldBounds, 1);
                var hr = playUi.HeadingScreenRects[side];
                float gap = pr.yMin - hr.yMax;
                if (gap < pr.height * 0.4f) Fail("heading too close to first loss row: gap " + gap + " px, piece " + pr.height + " px");
                if (side == 0) log.Add("loss heading gap " + gap.ToString("0") + " px over a " + pr.height.ToString("0") + " px piece");
            }
            // Size relative to board pieces (≥78%).
            var boardPiece = game.PieceViews.First(v => v.IsOwn);
            float ratio = game.Graveyard.OwnViews[0].WorldBounds.size.x / boardPiece.WorldBounds.size.x;
            if (ratio < 0.78f) Fail("loss pieces smaller than 78%: " + ratio);
            CheckTooltipPlacement(Screen.width / UiKit.Scale, Screen.height / UiKit.Scale);
        }

        private void CheckTooltipPlacement(float w, float h)
        {
            var screen = new Rect(8, 8, w - 16, h - 16);
            var panels = new[] { new Rect(0, 0, w, 72), new Rect(w - 400, 80, 400, h - 80) };
            foreach (var anchor in new[] { new Rect(24, 220, 55, 65), new Rect(w - 80, 220, 55, 65), new Rect(w / 2, 72, 55, 65), new Rect(w / 2, h - 90, 55, 65) })
            {
                var r = EnemyTooltip.Place(anchor, new Vector2(370, 240), screen, panels);
                float gap = EnemyTooltip.Clearance(anchor);
                var exclusion = Rect.MinMaxRect(anchor.xMin - gap, anchor.yMin - gap, anchor.xMax + gap, anchor.yMax + gap);
                if (!ContainsRect(screen, r) || r.Overlaps(exclusion)) Fail("tooltip clips or covers large piece/cursor exclusion: " + r);
                if (panels.Any(p => r.Overlaps(p))) Fail("tooltip unnecessarily overlaps monitor/header");
            }
            var left = new Rect(20, 300, 50, 60);
            var right = new Rect(w - 70, 300, 50, 60);
            var a = EnemyTooltip.Place(left, new Vector2(370, 180), screen, new Rect[0]);
            var b = EnemyTooltip.Place(right, new Vector2(370, 180), screen, new Rect[0]);
            if (a.xMin < left.xMax + EnemyTooltip.Clearance(left) - 1) Fail("left-edge tooltip should go right");
            if (b.xMax > right.xMin - EnemyTooltip.Clearance(right) + 1) Fail("right-edge tooltip should flip left");
        }

        // ------------------------------------------------------------------
        // Tooltip: observation setting and research reveal
        // ------------------------------------------------------------------

        private IEnumerator CheckTooltipDrawing()
        {
            game.SetPaused(true);
            string before = game.Session.Fingerprint();
            var target = game.View.Enemy.First(e => e.Alive);
            game.Tooltip.PreviewPieceId = target.Id;
            var truth = game.Session.ResearchTrueKind(target.Id).Value;
            string truthName = PieceCatalog.JapaneseName(truth);

            presentation.SetMode(PresentationMode.Play);
            game.Settings.ObservationTooltip = true;
            yield return WaitFrames(4);
            string t = game.Tooltip.LastText;
            if (string.IsNullOrEmpty(t) || !t.Contains("正体不明") || !t.Contains("まだ一度も動いていない")) Fail("public tooltip not drawn in 対戦");
            if (t != null && (t.Contains("%") || t.Contains("真値"))) Fail("probability/truth leaked into the 対戦 tooltip");
            if (Shots) yield return Shot("tooltip_Play.png");
            presentation.RevealCpuPieces = true;   // switch left on from research must not matter in 対戦
            yield return WaitFrames(4);
            if (game.Tooltip.LastText == null || game.Tooltip.LastText.Contains("真値") || game.EnemiesRevealed) Fail("truth shown in 対戦");
            presentation.RevealCpuPieces = false;
            game.Settings.ObservationTooltip = false;
            yield return WaitFrames(4);
            if (game.Tooltip.LastText != null) Fail("tooltip drawn with 敵駒の観測情報 OFF");

            presentation.SetMode(PresentationMode.Research);
            yield return WaitFrames(4);
            if (string.IsNullOrEmpty(game.Tooltip.LastText)) Fail("research tooltip affected by the 対戦 observation setting");
            presentation.RevealCpuPieces = true;
            yield return WaitFrames(4);
            t = game.Tooltip.LastText;
            var view = game.PieceViews.First(v => v.Id == target.Id);
            if (t == null || !t.Contains("Enemy #" + target.Number) || !t.Contains(truthName)) Fail("research reveal tooltip lacks the true kind: " + t);
            if (view.ShowsBack) Fail("research reveal did not show the face on the board");
            if (game.Graveyard.EnemyViews.Any(v => !v.ShowsBack)) Fail("reveal touched the loss area");
            if (Shots) yield return Shot("tooltip_Research_reveal.png");
            presentation.RevealCpuPieces = false;
            yield return WaitFrames(4);
            t = game.Tooltip.LastText;
            if (t == null || t.Contains("真値") || !view.ShowsBack) Fail("reveal OFF did not revert to 判明/正体不明");
            game.Settings.ObservationTooltip = true;
            game.Tooltip.PreviewPieceId = -1;
            presentation.SetMode(PresentationMode.Play);
            if (before != game.Session.Fingerprint()) Fail("tooltip/reveal changed CPU/session state");
            log.Add("tooltip: 対戦 public only, OFF hides it (研究 unaffected), 研究 reveal shows 「" + truthName + "」 and reverts");
            game.SetPaused(false);
            yield return WaitFrames(3);
        }

        // ------------------------------------------------------------------
        // UI state stress test
        // ------------------------------------------------------------------

        private IEnumerator StressTest(string label, int steps, bool setup)
        {
            if (!setup) game.SetPaused(true);
            var rnd = new System.Random(setup ? 120 : 121);   // presentation-only randomness, never the game's
            int errors0 = UiGuard.ErrorCount;
            string fp = game.Session.Fingerprint();
            var rng = Random.state;
            var ops = new List<string>();
            var names = new[] { "toggle", "help", "settings", "monitor", "history", "effect", "speed", "numbers", "reveal", "reportIndex", "tooltip", "sfx", "confirmNew",
                "seedApply", "seedRandom", "presetSave", "presetLoad", "newSetup", "setupAfterPast" };
            int limit = setup ? names.Length : 13;   // in game only non-state operations
            if (setup) yield return Repro111();
            for (int i = 0; i < steps; i++)
            {
                string op = names[rnd.Next(limit)];
                ops.Add(op);
                switch (op)
                {
                    case "toggle": presentation.Toggle(); break;
                    case "help": if (presentation.HelpOpen) presentation.CloseHelp(); else presentation.OpenHelp(); break;
                    case "settings": if (!presentation.HelpOpen) presentation.SettingsOpen = !presentation.SettingsOpen; break;
                    case "monitor": presentation.MonitorOpen = !presentation.MonitorOpen; break;
                    case "history": researchUi.HistoryVisible = !researchUi.HistoryVisible; break;
                    case "effect": game.Settings.Effect = game.Settings.Effect == EffectMode.Normal ? EffectMode.Simple : EffectMode.Normal; break;
                    case "speed": game.Settings.EffectSpeed = game.Settings.EffectSpeed >= 4 ? 1 : game.Settings.EffectSpeed * 2; break;
                    case "numbers": game.Settings.ShowEnemyNumbers = !game.Settings.ShowEnemyNumbers; break;
                    case "reveal": presentation.RevealCpuPieces = !presentation.RevealCpuPieces; break;
                    case "reportIndex": researchUi.ReportIndex = rnd.Next(-1, 40); break;
                    case "tooltip": game.Settings.ObservationTooltip = !game.Settings.ObservationTooltip; break;
                    case "sfx": game.Settings.SfxOn = !game.Settings.SfxOn; break;
                    case "confirmNew": playUi.ConfirmNewOpen = !playUi.ConfirmNewOpen; break;
                    case "seedApply": game.Settings.PlayerFormationSeed = rnd.Next(1, 999999); game.AutoArrange(); break;
                    case "seedRandom": game.Settings.CpuFormationSeed = rnd.Next(1, 999999); game.RefreshCpu(); break;
                    case "presetSave": researchUi.Presets.SelectedSlot = rnd.Next(3); researchUi.Presets.BeginSave(); researchUi.Presets.CommitSave(); break;
                    case "presetLoad": playUi.Presets.SelectedSlot = rnd.Next(3); playUi.Presets.Load(); break;
                    case "newSetup": game.NewSetup(false); break;
                    case "setupAfterPast": researchUi.ReportIndex = 5; presentation.MonitorOpen = true; game.NewSetup(false); break;
                }
                yield return null;
                yield return null;
                CheckUiInvariants(label + " step " + i + " (" + op + ")", errors0);
                if (failures > 30) break;
            }
            // Close everything and make sure the board takes clicks again.
            if (presentation.HelpOpen) presentation.CloseHelp();
            presentation.SettingsOpen = false;
            playUi.ConfirmNewOpen = false;
            yield return WaitFrames(3);
            var centre = game.MainCamera.WorldToScreenPoint(Vector3.zero);
            if (game.IsOverUi(new Vector2(centre.x, Screen.height - centre.y))) Fail(label + ": board centre still covered after closing all overlays");
            if (Time.timeScale != 1f && !game.Paused) Fail(label + ": time scale left at " + Time.timeScale);
            if (!setup)
            {
                if (game.Session.Fingerprint() != fp) Fail(label + ": stress operations changed the game state");
                if (!Random.state.Equals(rng)) Fail(label + ": stress operations changed UnityEngine.Random.state");
                game.SetPaused(false);
            }
            log.Add("UI stress " + label + ": " + ops.Count + " random operations (" + string.Join(",", ops.Distinct().OrderBy(s => s)) + "), UI errors " + (UiGuard.ErrorCount - errors0)
                + ", watchdog repairs " + presentation.WatchdogRepairs + (setup ? "" : ", game state unchanged"));
        }

        private void CheckUiInvariants(string where, int errors0)
        {
            if (UiGuard.ErrorCount != errors0) { Fail(where + ": UI exception " + UiGuard.LastError); return; }
            bool helpPause = (game.PauseReasons & GameController.PauseReason.Help) != 0;
            if (helpPause != presentation.HelpOpen) Fail(where + ": pause does not follow あそびかた");
            if (presentation.HelpOpen && presentation.SettingsOpen) Fail(where + ": help and settings open together");
            int frame = Time.frameCount - 1;
            bool play = presentation.Mode == PresentationMode.Play;
            // Exactly the UI of the current mode draws (OnGUI repaint happened last frame).
            if (play && (playUi.LastDrawFrame < frame - 1 || researchUi.LastDrawFrame >= frame)) Fail(where + ": 対戦 UI not the active UI");
            if (!play && (researchUi.LastDrawFrame < frame - 1 || playUi.LastDrawFrame >= frame)) Fail(where + ": 研究 UI not the active UI");
            if (presentation.HelpOpen && helpUi.LastDrawFrame < frame - 1) Fail(where + ": help open but not drawn (invisible overlay)");
            bool modal = presentation.HelpOpen || presentation.SettingsOpen || (play && playUi.ConfirmNewOpen) || game.Phase == Phase.Finished;
            var centre = game.MainCamera.WorldToScreenPoint(Vector3.zero);
            if (!modal && game.IsOverUi(new Vector2(centre.x, Screen.height - centre.y))) Fail(where + ": board centre covered by UI without a visible modal");
        }

        /// <summary>
        /// The 1.1.1 incident: 研究モード with the monitor showing a past turn, then a new setup. The past-turn
        /// index survived into the setup (no reports) and every OnGUI threw, leaving a half-drawn dark overlay
        /// and dead header buttons.
        /// </summary>
        private IEnumerator Repro111()
        {
            int errors0 = UiGuard.ErrorCount;
            presentation.SetMode(PresentationMode.Research);
            presentation.MonitorOpen = true;
            researchUi.MonitorTab = 1;
            researchUi.ReportIndex = 3;
            game.NewSetup(false);
            yield return WaitFrames(5);
            researchUi.ReportIndex = 3;          // even if something sets it again in setup
            yield return WaitFrames(5);
            if (UiGuard.ErrorCount != errors0) Fail("1.1.1 repro: research setup still throws: " + UiGuard.LastError);
            if (researchUi.LastDrawFrame < Time.frameCount - 2) Fail("1.1.1 repro: research UI not drawn");
            presentation.MonitorOpen = false;
            researchUi.MonitorTab = 0;
            presentation.SetMode(PresentationMode.Play);
            log.Add("1.1.1 repro (monitor on a past turn → new setup in 研究): no UI exception, UI drawn");
        }

        // ------------------------------------------------------------------
        // Sound
        // ------------------------------------------------------------------

        private void RecordEffectCombo()
        {
            effectCombos.Add(game.Settings.Effect + " x" + game.Settings.EffectSpeed.ToString("0"));
        }

        /// <summary>Sounds start in the same frame as the visual events they belong to, at every speed.</summary>
        private void CheckSoundSync()
        {
            var audio = game.Audio;
            if (audio == null || !audio.HasAllClips) { Fail("sound clips missing"); return; }
            var marks = game.VisualEvents;
            int clash = 0, topple = 0, place = 0;
            float worstTopple = 0;
            foreach (var e in audio.Log)
            {
                if (e.Kind == Sfx.Clash)
                {
                    clash++;
                    if (!marks.Any(m => m.Key == "contact" && Mathf.Abs(m.Value - e.Time) < 1e-4f)) Fail("clash sound not in the contact frame at t=" + e.Time);
                }
                else if (e.Kind == Sfx.Place)
                {
                    place++;
                    if (!marks.Any(m => m.Key == "land" && Mathf.Abs(m.Value - e.Time) < 1e-4f)) Fail("place sound not in the landing frame at t=" + e.Time);
                }
                else if (e.Kind == Sfx.Topple) topple++;
            }
            // Each topple sound starts so that its impact (offset into the clip) lands with the piece;
            // when the whole fall is shorter than the offset (×4 簡易) it starts with the fall.
            foreach (var t in game.ToppleTimings)
            {
                float expectedLead = Mathf.Min(AudioDirector.ToppleImpactOffset, t.z - t.x);
                float err = Mathf.Abs((t.z - t.y) - expectedLead);
                worstTopple = Mathf.Max(worstTopple, err);
                if (err > 0.035f) Fail("topple sound off the landing by " + err.ToString("0.000") + " s");
            }
            if (clash == 0 || topple == 0 || place == 0) Fail("sounds not played: clash " + clash + ", topple " + topple + ", place " + place);
            foreach (var need in new[] { "Normal x1", "Normal x2", "Normal x4", "Simple x1", "Simple x2", "Simple x4" })
                if (!effectCombos.Contains(need) && !toggleModes) Fail("no combat seen with " + need);
            log.Add("sound sync: " + place + " place / " + clash + " clash / " + topple + " topple sounds in frame with the visuals; worst topple impact error " + (worstTopple * 1000).ToString("0") + " ms; combats seen with " + string.Join(", ", effectCombos.OrderBy(s => s)));
        }

        private IEnumerator CheckSoundSettings()
        {
            var audio = game.Audio;
            var s = game.Settings;
            bool on0 = s.SfxOn; int vol0 = s.SfxVolume;
            s.SfxOn = true; s.SfxVolume = 50;
            yield return WaitFrames(2);
            if (Mathf.Abs(audio.CurrentVolume - 0.5f) > 1e-3f) Fail("volume 50% not applied: " + audio.CurrentVolume);
            audio.Play(Sfx.Select);
            if (!audio.Log.Last().Audible) Fail("SE ON but not audible");
            s.SfxOn = false;
            yield return WaitFrames(2);
            audio.Play(Sfx.Select);
            if (audio.Log.Last().Audible || audio.CurrentVolume != 0f) Fail("SE OFF still audible");
            s.SfxOn = true; s.SfxVolume = 0;
            audio.Play(Sfx.Select);
            if (audio.Log.Last().Audible) Fail("volume 0 still audible");
            // Persistence round trip of the settings file.
            s.SfxOn = on0; s.SfxVolume = 64; s.Effect = EffectMode.Simple; s.EffectSpeed = 2; s.ObservationTooltip = false;
            UserData.SaveSettings(s);
            var r = new GameSettings();
            UserData.LoadSettings(r);
            if (r.SfxVolume != 64 || r.Effect != EffectMode.Simple || r.EffectSpeed != 2 || r.ObservationTooltip || r.SfxOn != on0) Fail("settings file does not round-trip");
            s.SfxVolume = expectPersisted ? vol0 : PersistedVolume; s.Effect = EffectMode.Normal; s.EffectSpeed = 1; s.ObservationTooltip = true;
            UserData.SaveSettings(s);
            log.Add("sound settings: ON/OFF, volume 0/50% applied; settings file round trip (effect, speed, tooltip, SE, volume)");
        }
    }
}
