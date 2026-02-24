using UnityEngine;
using Newtonsoft.Json;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#endif

namespace EpicTransport
{
    /// <summary>
    /// EOSTransport login script that is safe to use in a public GitHub repo, as your keys are stored in a JSON file in the library folder instead of in a scene.
    /// </summary>
    public class GitHubSafeLoginScript : MonoBehaviour
    {
        [Header("Check 'Library/EOSTransport_LoginCreds.json' to edit settings!")]
        public int thisDoesNothing;


        public const string CredsFileName = "EOSTransport_LoginCreds.json";

        private void Start()
        {
            if (Application.isEditor)
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Library", GitHubSafeLoginScript.CredsFileName);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"Login Creds not found at '{path}'!");
                    return;
                }

                string data = File.ReadAllText(path);
                TransportInitializeOptions opts = JsonConvert.DeserializeObject<TransportInitializeOptions>(data);
                EOSManager.Initialize(opts);
            }
            else
            {
                string path = Path.Combine(Application.streamingAssetsPath, CredsFileName);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"Login Creds not found at '{path}'!");
                    return;
                }

                string data = File.ReadAllText(path);
                TransportInitializeOptions opts = JsonConvert.DeserializeObject<TransportInitializeOptions>(data);
                EOSManager.Initialize(opts);
            }
        }

#if UNITY_EDITOR
        private void OnValidate() => GitHubSafeLoginEditor.CreateTempJsonFile();
#endif
    }

#if UNITY_EDITOR
    public class GitHubSafeLoginEditor : IPostprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => -100;

        public static void CreateTempJsonFile()
        {
            string libraryFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Library", GitHubSafeLoginScript.CredsFileName);
            if (File.Exists(libraryFilePath)) return;

            File.WriteAllText(libraryFilePath, JsonConvert.SerializeObject(new TransportInitializeOptions(), Formatting.Indented));
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string libraryFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Library", GitHubSafeLoginScript.CredsFileName);
            if (!File.Exists(libraryFilePath)) return;

            string copyLocation = Path.Combine(path, "src", "main", "assets", GitHubSafeLoginScript.CredsFileName);
            File.Copy(libraryFilePath, copyLocation);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platformGroup == BuildTargetGroup.Android) return;

            string libraryFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Library", GitHubSafeLoginScript.CredsFileName);
            if (!File.Exists(libraryFilePath)) return;

            string copyLocation = Path.Combine(GetStreamingAssetsPath(report.summary.platform, report.summary.outputPath), GitHubSafeLoginScript.CredsFileName);
            File.Copy(libraryFilePath, copyLocation);
        }

        private string GetStreamingAssetsPath(BuildTarget target, string buildOutput)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneLinux64:
                    string dataFolder = Path.GetDirectoryName(buildOutput) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(buildOutput) + "_Data";
                    return Path.Combine(dataFolder, "StreamingAssets");

                case BuildTarget.StandaloneOSX:
                    string appBundlePath = Path.GetDirectoryName(buildOutput);
                    return Path.Combine(appBundlePath, "Contents/Resources/Data/StreamingAssets");

                case BuildTarget.iOS:
                    return Path.Combine(buildOutput, "Data/Raw");

                default:
                    Debug.LogWarning($"StreamingAssets path not defined for platform: {target}");
                    return null;
            }
        }
    }
#endif
}
