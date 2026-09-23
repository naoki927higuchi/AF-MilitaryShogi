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
        private static bool ContainsRect(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;
        }

        private IEnumerator CheckLayouts()
        {
            game.SetPaused(true);
            // A rendering-only fixture: 31 losses on each side, no session or CPU mutation.
            var own = game.PlayerFormation.Pieces.Select((p, i) => new OwnPieceView(i, i + 1, p.Value, -1, p.Key)).ToArray();
            var enemy = Enumerable.Range(0, 31).Select(i => new EnemyPieceView(100 + i, i + 1, -1, 32 + i)).ToArray();
            var owners = Enumerable.Repeat(MoveRules.Empty, BoardGraph.NodeCount).ToArray();
            var history = Enumerable.Range(0, 31).Select(i => new ObservedMove(i + 1, GameController.Human, i, 0, 32, new[] { 32 }, 0, owners,
                new ObservedCombat(i, 100 + i, CombatOutcome.Tie))).ToArray();
            var fixture = new PlayerView(GameController.Human, 31, GameController.Human, GameStatus.Playing, null, EndReason.None, own, enemy, history, owners);
            string before = game.Session.Fingerprint();
            game.Graveyard.Sync(fixture);
            yield return WaitFrames(3);
            foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
            {
                Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(0.4f);
                foreach (var mode in new[] { PresentationMode.Play, PresentationMode.Research })
                {
                    presentation.SetMode(mode);
                    researchUi.MonitorVisible = true;
                    yield return WaitFrames(6);
                    CheckPicking();
                    var cam = game.MainCamera;
                    if (Mathf.Abs(cam.transform.eulerAngles.x - CameraFit.Pitch) > 0.01f || Mathf.Abs(cam.fieldOfView - 36f) > 0.01f) Fail("camera perspective changed");
                    foreach (var c in CameraFit.BoardCorners())
                    {
                        var vp = cam.WorldToViewportPoint(c);
                        if (vp.x < -0.001f || vp.x > 1.001f || vp.y < -0.001f || vp.y > 1.001f) Fail("board outside reserved area " + vp);
                    }
                    float top = CameraFit.BoardCorners().Max(c => cam.WorldToViewportPoint(c).y);
                    if (top < 0.94f) Fail("unused space above board: top " + top);
                    if (mode == PresentationMode.Play)
                    {
                        var area = cam.pixelRect;
                        area.y = Screen.height - area.yMax;
                        foreach (var v in game.Graveyard.OwnViews.Concat(game.Graveyard.EnemyViews))
                        {
                            var r = EnemyTooltip.ScreenBounds(cam, v.WorldBounds, 1);
                            if (!ContainsRect(area, r)) Fail("31-loss fixture outside viewport " + r);
                        }
                        if (game.Graveyard.OwnViews.Count != 31 || game.Graveyard.EnemyViews.Count != 31) Fail("31-loss fixture count");
                        var kinds = game.Graveyard.OwnViews.Select(v => own.First(p => p.Id == v.Id).Type).ToArray();
                        if (!kinds.SequenceEqual(kinds.OrderBy(t => t))) Fail("31-loss fixture own sort order");
                        if (!game.Graveyard.EnemyIdsShown.SequenceEqual(history.Select(m => m.Combat.DefenderId))) Fail("31-loss fixture enemy removal order");
                        CheckTooltipPlacement(Screen.width / UiKit.Scale, Screen.height / UiKit.Scale);
                    }
                    log.Add("layout " + mode + " requested " + resolution + " actual " + Screen.width + "x" + Screen.height + ": picking 66 nodes, board top=" + top.ToString("0.000") + ", pitch/FOV fixed; 31 losses per side in Play");
                    if (!toggleModes && resolution.x == 1600) yield return Shot("layout_" + mode + "_31_losses.png");
                }
            }
            game.Graveyard.Clear();
            presentation.SetMode(PresentationMode.Play);
            researchUi.MonitorVisible = false;
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.4f);
            if (game.Session.Fingerprint() != before) Fail("layout fixture changed the game session");
            game.SetPaused(false);
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
            log.Add("tooltip edge/flip/up/down/monitor/large-cursor exclusion tests passed at virtual " + w + "x" + h);
        }

        private IEnumerator CheckTooltipDrawing()
        {
            game.SetPaused(true);
            string before = game.Session.Fingerprint();
            foreach (var mode in new[] { PresentationMode.Play, PresentationMode.Research })
            {
                presentation.SetMode(mode);
                researchUi.MonitorVisible = true;
                yield return WaitFrames(6);
                game.Tooltip.PreviewPieceId = game.View.Enemy.First(e => e.Alive).Id;
                yield return WaitFrames(3);
                if (string.IsNullOrEmpty(game.Tooltip.LastText) || !game.Tooltip.LastText.Contains("正体不明") || !game.Tooltip.LastText.Contains("まだ一度も動いていない")) Fail("public tooltip not drawn in " + mode);
                if (game.Tooltip.LastText != null && game.Tooltip.LastText.Contains("%")) Fail("probability leaked into human tooltip");
                if (!toggleModes) yield return Shot("tooltip_" + mode + ".png");
            }
            game.Tooltip.PreviewPieceId = -1;
            presentation.SetMode(PresentationMode.Play);
            if (before != game.Session.Fingerprint()) Fail("human tooltip changed CPU/session state");
            game.SetPaused(false);
            yield return WaitFrames(3);
        }
    }
}
