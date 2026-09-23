using System;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// Scene entry point. Builds camera, light, board, controller and UI from code so the
    /// scene file stays trivial. Command line:
    ///   -seeds P,C,D          player formation / CPU formation / CPU decision seeds
    ///   -autotest DIR         play automatically, verify, write screenshots + report into DIR, quit
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const string Version = "1.0.0";

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.shadowDistance = 30f;

            // Clears the whole window every frame; the board camera only covers the free area
            // between the UI columns, so without this old UI pixels would remain visible.
            var background = new GameObject("Background Camera").AddComponent<Camera>();
            background.clearFlags = CameraClearFlags.SolidColor;
            background.backgroundColor = new Color(0.045f, 0.032f, 0.025f);
            background.cullingMask = 0;
            background.depth = -10;

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.depth = 0;
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.045f, 0.032f, 0.025f);
            cam.fieldOfView = 36f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            // Fixed high-angle view from the player's side: perspective makes the far side narrower.
            cam.transform.position = new Vector3(0f, 13.6f, -10.4f);
            cam.transform.rotation = Quaternion.Euler(54f, 0f, 0f);

            var sun = new GameObject("Key Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            sun.transform.rotation = Quaternion.Euler(58f, -28f, 0f);
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.55f, 0.6f, 0.75f);
            fill.intensity = 0.25f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.3f, 0.27f);

            var boardGo = new GameObject("Board");
            var board = boardGo.AddComponent<BoardView>();
            board.Build();

            var controller = new GameObject("Game").AddComponent<GameController>();
            ApplySeedArguments(controller.Settings);
            controller.Initialise(cam, board);
            var ui = controller.gameObject.AddComponent<GameUi>();
            ui.Version = Version;
            ui.Bind(controller);

            string autotest = Argument("-autotest");
            if (autotest != null)
            {
                var pilot = controller.gameObject.AddComponent<AutoPilot>();
                pilot.Begin(controller, autotest);
            }
        }

        private static void ApplySeedArguments(GameSettings s)
        {
            string seeds = Argument("-seeds");
            if (seeds == null) return;
            var parts = seeds.Split(',');
            if (parts.Length != 3) return;
            s.PlayerFormationSeed = int.Parse(parts[0]);
            s.CpuFormationSeed = int.Parse(parts[1]);
            s.CpuDecisionSeed = int.Parse(parts[2]);
        }

        public static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
