using System.IO;
using HexWorldStudio.Generation;
using HexWorldStudio.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HexWorldStudio.DevTools
{
    /// <summary>
    /// Dev-only helper (not part of the HexWorldStudio package): renders
    /// the QuickStart scene from fixed seeds and camera angles into PNGs
    /// under <c>docs/images/</c>, for the README and for checking visual
    /// changes without opening the editor.
    /// </summary>
    /// <remarks>
    /// Run headless with the editor closed (no -nographics, it needs a GPU):
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod
    /// HexWorldStudio.DevTools.ExhibitionCapture.CaptureAll -quit</c>.
    /// Or use the menu item in the editor.
    /// </remarks>
    public static class ExhibitionCapture
    {
        private const string ScenePath = "Assets/HexWorldStudio/Examples/QuickStart/QuickStartScene.unity";
        private const int Width = 1920;
        private const int Height = 1080;

        private struct Shot
        {
            public string File;
            public ulong Seed;
            public ContinentShapeAlgorithm Map;
            public float FocusU, FocusV; // focus point as a fraction of map width / height
            public float CameraHeight, Pitch, Yaw;
        }

        private static readonly Shot[] Shots =
        {
            new Shot { File = "hero.png",        Seed = 20261118UL, Map = ContinentShapeAlgorithm.Continents,  FocusU = 0.50f, FocusV = 0.50f, CameraHeight = 22f, Pitch = 50f, Yaw = 0f },
            new Shot { File = "closeup.png",     Seed = 20261118UL, Map = ContinentShapeAlgorithm.Continents,  FocusU = 0.42f, FocusV = 0.55f, CameraHeight = 9f,  Pitch = 42f, Yaw = 25f },
            new Shot { File = "pangaea.png",     Seed = 7UL,        Map = ContinentShapeAlgorithm.Pangaea,     FocusU = 0.50f, FocusV = 0.50f, CameraHeight = 22f, Pitch = 55f, Yaw = 0f },
            new Shot { File = "archipelago.png", Seed = 42UL,       Map = ContinentShapeAlgorithm.Archipelago, FocusU = 0.50f, FocusV = 0.50f, CameraHeight = 22f, Pitch = 55f, Yaw = 0f },
            new Shot { File = "edge.png",        Seed = 20261118UL, Map = ContinentShapeAlgorithm.Continents,  FocusU = 0.50f, FocusV = 0.98f, CameraHeight = 22f, Pitch = 40f, Yaw = 0f },
        };

        [MenuItem("Tools/Hex World Studio/Capture README Screenshots")]
        public static void CaptureAll()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var bootstrap = Object.FindFirstObjectByType<WorldGeneratorBootstrap>();
            Camera cam = Camera.main;
            if (bootstrap == null || cam == null)
            {
                Debug.LogError("[ExhibitionCapture] QuickStart scene is missing its bootstrap or main camera.");
                return;
            }

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "images"));
            Directory.CreateDirectory(outDir);

            foreach (Shot shot in Shots)
            {
                bootstrap.ContinentShapeAlgorithm = shot.Map;
                bootstrap.Regenerate(shot.Seed);

                var map = bootstrap.LatestMap;
                var focus = new Vector3(
                    shot.FocusU * map.Width * HexMesh.HorizontalSpacing,
                    0f,
                    shot.FocusV * map.Height * HexMesh.VerticalSpacing);
                PlaceCamera(cam, focus, shot.CameraHeight, shot.Pitch, shot.Yaw);

                string path = Path.Combine(outDir, shot.File);
                Render(cam, path);
                Debug.Log("[ExhibitionCapture] wrote " + path);
            }

            // Leave the scene as it was on disk.
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // Same framing math as CivCameraController.ApplyCameraTransform.
        private static void PlaceCamera(Camera cam, Vector3 focus, float height, float pitch, float yaw)
        {
            float pitchRad = pitch * Mathf.Deg2Rad;
            float yawRad = yaw * Mathf.Deg2Rad;
            float horizontal = height / Mathf.Tan(pitchRad);
            cam.transform.position = focus + new Vector3(-Mathf.Sin(yawRad) * horizontal, height, -Mathf.Cos(yawRad) * horizontal);
            cam.transform.LookAt(focus, Vector3.up);
        }

        private static void Render(Camera cam, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var request = new RenderPipeline.StandardRequest { destination = rt };
            float aspect = cam.aspect;
            cam.aspect = (float)Width / Height;
            try
            {
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.aspect = aspect;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
