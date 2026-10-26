using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PressOn.EditorTools
{
    /// <summary>
    /// Menu: Press-On > Build Demo Scene.
    /// Creates a ready-to-run scene: the client, a squeezable orb (patient view),
    /// a timeline (therapist view), the haptic coach, the recorder and a debug panel.
    /// </summary>
    public static class PressOnSceneBuilder
    {
        const string Root = "Assets/PressOn";
        const string ScenePath = Root + "/Scenes/PressOnDemo.unity";
        const string MaterialPath = Root + "/Materials/PressOnOrb.mat";

        [MenuItem("Press-On/Build Demo Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Camera: soft blush background, looking at both views
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 1f, -9f);
                cam.transform.rotation = Quaternion.identity;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.99f, 0.92f, 0.95f);
            }

            // Client + logic
            var system = new GameObject("Press-On");
            system.AddComponent<PressOnClient>();
            var coach = system.AddComponent<HapticCoach>();
            var recorder = system.AddComponent<SessionRecorder>();
            var panel = system.AddComponent<PressOnDebugPanel>();
            panel.coach = coach;
            panel.recorder = recorder;

            // Patient view: the orb
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Patient View (Orb)";
            orb.transform.position = new Vector3(-1.8f, 1.2f, 0f);
            orb.transform.localScale = Vector3.one * 2.4f;
            Object.DestroyImmediate(orb.GetComponent<Collider>());
            orb.GetComponent<Renderer>().sharedMaterial = GetOrCreateOrbMaterial();
            orb.AddComponent<PressureOrb>();

            // Therapist view: the timeline
            var timeline = new GameObject("Therapist View (Timeline)");
            timeline.transform.position = new Vector3(0.6f, 0.2f, 0f);
            timeline.AddComponent<LineRenderer>();
            var tl = timeline.AddComponent<PressureTimeline>();
            tl.width = 3.6f;
            tl.height = 2f;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log("[Press-On] Demo scene ready. Start the bridge (python bridge/press_on_bridge.py --sim) and press Play.");
        }

        static Material GetOrCreateOrbMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = "PressOnOrb", color = new Color(0.36f, 0.78f, 0.74f) };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.35f);

            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes) if (s.path == path) return;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes) { new EditorBuildSettingsScene(path, true) };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
