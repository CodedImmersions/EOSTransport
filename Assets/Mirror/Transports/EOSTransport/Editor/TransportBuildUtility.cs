using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EpicTransport.Editor
{
    //TODO: add logging
#if UNITY_ANDROID
    public class TransportBuildUtility : IPreprocessBuildWithReport, IPostGenerateGradleAndroidProject
#else
    public class TransportBuildUtility : IPreprocessBuildWithReport
#endif
    {
        public int callbackOrder => -10;

#region General
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Is64Bit(report.summary.platformGroup, report.summary.platform))
            {
                throw new BuildFailedException("EOSSDK 1.19.0.3+ requires a 64-bit build target. Please switch to a 64-bit platform.");
            }

#if UNITY_ANDROID
            if (report.summary.platformGroup == BuildTargetGroup.Android)
            {
                string monoGradlePath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "Bee", "Android", "Prj", "Mono2x", "Gradle");
                if (Directory.Exists(monoGradlePath)) Directory.Delete(monoGradlePath, true);

                string il2cppGradlePath = Path.Combine(Directory.GetCurrentDirectory(), "Library", "Bee", "Android", "Prj", "IL2CPP", "Gradle");
                if (Directory.Exists(il2cppGradlePath)) Directory.Delete(il2cppGradlePath, true);

                (string jdk, int jdkMajor) = GetCurrentJDKVersion();
                if (jdkMajor == 0) Debug.LogError("Error parsing OpenJDK version.");

                if (jdkMajor < 11)
                {
                    int unityMajor = Application.unityVersion.StartsWith("2021") ? 2021 : 2022;
                    string jdk11UnityVersion = unityMajor == 2021 ? "2021.3.45f2" : "2022.3.62f3";


                    EditorUtility.DisplayDialog(
                        "EOSTransport JDK Incompatibility",
                        $"EOSTransport does not support anything below OpenJDK 11, but your OpenJDK version is '{jdk}'.\n" +
                        $"This is because your Unity version ({Application.unityVersion}) is too old to support JDK 11. To stay in the {unityMajor} LTS, please upgrade to {jdk11UnityVersion}. " +
                        $"Else, please upgrade to the latest Unity 6 version.", "OK");

                    throw new BuildFailedException($"EOSTransport does not support OpenJDK version '{jdk}', and only supports OpenJDK 11+. Please upgrade to a Unity version that has OpenJDK 11 or higher, such as 2021.3.45f2, 2022.3.62f3, or 6000.0.68f1.");
                }
            }
#endif
        }

        private bool Is64Bit(BuildTargetGroup group, BuildTarget target)
        {
            return group switch
            {
                BuildTargetGroup.Standalone => target switch
                {
                    BuildTarget.StandaloneWindows64 or BuildTarget.StandaloneOSX or BuildTarget.StandaloneLinux64 => true,
                    BuildTarget.StandaloneWindows => false,
                    _ => true,
                },
                BuildTargetGroup.Android => PlayerSettings.Android.targetArchitectures.HasFlag(AndroidArchitecture.ARM64) || PlayerSettings.Android.targetArchitectures.HasFlag(AndroidArchitecture.X86_64),
                BuildTargetGroup.iOS => true,
                BuildTargetGroup.PS4 or BuildTargetGroup.PS5 => true,
                BuildTargetGroup.XboxOne => true,
                BuildTargetGroup.Switch => true,
                _ => true,
            };
        }
#endregion

#region Android
#if UNITY_ANDROID
        public void OnPostGenerateGradleAndroidProject(string path) //'path' leads to the unityLibrary folder
        {
            GradlePropertiesData properties = ParseGradleProperties(path);

            AddClientID(ref path);

            ReplaceEosDepsVariables(ref path, properties);
            AddCoreLibraryDesugaringToUnityApps(ref path, properties);
            ModifyRootBuildGradle(ref path, properties);
            AddAndroidX(ref path);

            ModifyEosSdkAarDesugarVersion(ref path, properties);
        }

        private void AddClientID(ref string path)
        {
#region setting strings
            string clientid = PlayerPrefs.GetString("EOSTransport Client ID");
            if (string.IsNullOrWhiteSpace(clientid)) throw new BuildFailedException("No Client ID is set to be added to the strings.xml build file, so the build cannot continue. Please enter playmode, let EOS log in successfully, then try building again.");

            string stringsfoldpath = Path.Combine(path, "src", "main", "res", "values");
            string stringspath = Path.Combine(stringsfoldpath, "strings.xml");
            if (!Directory.Exists(stringsfoldpath)) Directory.CreateDirectory(stringsfoldpath);

            XmlDocument xml = new XmlDocument();
            if (File.Exists(stringspath)) xml.Load(stringspath);
            else xml.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources></resources>");

            XmlNode res = xml.SelectSingleNode("resources");

            XmlNode old = res.SelectSingleNode("string[@name='eos_login_protocol_scheme']");
            if (old != null) res.RemoveChild(old);

            XmlElement @new = xml.CreateElement("string");
            @new.SetAttribute("name", "eos_login_protocol_scheme");

            //IMPORTANT: YOU MUST KEEP ToLower(), as EOS reqires the ID to be LOWER CASE. Read more in the link below.
            //https://dev.epicgames.com/docs/epic-online-services/platforms/android#7-how-to-receive-login-callback
            @new.InnerText = $"eos.{clientid.ToLower()}";

            res.AppendChild(@new);
            xml.Save(stringspath);
#endregion
        }

        private void AddCoreLibraryDesugaringToUnityApps(ref string path, GradlePropertiesData properties)
        {
            //'unityLibrary' app
            string unityLibraryGradlePath = Path.Combine(path, "build.gradle");
            if (File.Exists(unityLibraryGradlePath))
            {
                string unityLibraryContents = File.ReadAllText(unityLibraryGradlePath);
                string result = AddDesugaring(unityLibraryContents, properties);
                File.WriteAllText(unityLibraryGradlePath, result);
            }
            else Debug.LogWarning($"Could not find build.gradle in 'unityLibrary' app at path '{unityLibraryGradlePath}'. Core Library Desugaring will not be added, which could cause the Gradle build to fail.");


            //'launcher' app
            string launcherGradlePath = Path.Combine(Directory.GetParent(path).FullName, "launcher", "build.gradle");
            if (File.Exists(launcherGradlePath))
            {
                string launcherContents = File.ReadAllText(launcherGradlePath);
                string result = AddDesugaring(launcherContents, properties);
                File.WriteAllText(launcherGradlePath, result);
            }
            else Debug.LogWarning($"Could not find build.gradle in 'launcher' app at path '{launcherGradlePath}'. Core Library Desugaring will not be added, which could cause the Gradle build to fail.");
        }

        private string AddDesugaring(string content, GradlePropertiesData properties)
        {
            string result = content;

#region Dependencies
            if (!content.Contains("coreLibraryDesugaring"))
            {
                int dependenciesIndex = content.IndexOf("dependencies");
                if (dependenciesIndex != -1)
                {
                    int openBrace = content.IndexOf('{', dependenciesIndex);
                    if (openBrace != -1)
                    {
                        string dependency = $"\n    coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:{properties.desugarLibVersion}'";
                        result = result.Insert(openBrace + 1, dependency);
                    }
                }
                else
                {
                    Debug.LogWarning("Could not find dependencies block in build.gradle");
                }
            }
#endregion

#region Compile Options
            if (content.Contains("compileOptions"))
            {
                int compileOptionsIndex = result.IndexOf("compileOptions");
                int openBrace = result.IndexOf('{', compileOptionsIndex);
                if (openBrace != -1)
                {
                    string flag = "\n        coreLibraryDesugaringEnabled true";
                    result = result.Insert(openBrace + 1, flag);
                }
            }
            else
            {
                int androidIndex = result.IndexOf("android");
                if (androidIndex != -1)
                {
                    int openBrace = result.IndexOf('{', androidIndex);
                    if (openBrace != -1)
                    {
                        string block = $@"
                            compileOptions {{
                                coreLibraryDesugaringEnabled true
                                sourceCompatibility JavaVersion.{properties.javaCompatibilityVersion}
                                targetCompatibility JavaVersion.{properties.javaCompatibilityVersion}
                            }}";
                        result = result.Insert(openBrace + 1, block);
                    }
                }
            }
#endregion

            return result;
        }

        private void ReplaceEosDepsVariables(ref string path, GradlePropertiesData properties)
        {
            string gradlePath = Path.Combine(path, "eos-dependencies.androidlib", "build.gradle");

            if (!File.Exists(gradlePath)) return;

            string gradleContent = File.ReadAllText(gradlePath);
            string processedContent = gradleContent
                .Replace("**AGPVERSION**", properties.agpVersion)
                .Replace("**COMPILESDKVERSION**", properties.compileSdkVersion.ToString())
                .Replace("**MINSDKVERSION**", properties.minSdkVersion.ToString())
                .Replace("**TARGETSDKVERSION**", properties.targetSdkVersion.ToString())
                .Replace("**BUILDTOOLSVERSION**", $"'{properties.buildToolsVersion}'")
                .Replace("**JAVAVERSION**", properties.javaCompatibilityVersion)
                .Replace("**DESUGARVERSION**", properties.desugarLibVersion);

            File.WriteAllText(gradlePath, processedContent);

        }

        private void AddAndroidX(ref string path)
        {
            string gradlePropertiesPath = Path.Combine(Directory.GetParent(path).FullName, "gradle.properties");
            if (!File.Exists(gradlePropertiesPath)) return;
            
            string lines = File.ReadAllText(gradlePropertiesPath);
            if (lines.Length > 0)
            {
                if (!lines.Contains("android.useAndroidX=true"))
                {
                    lines += "\nandroid.useAndroidX=true";
                    File.WriteAllText(gradlePropertiesPath, lines);
                }
                else if (lines.Contains("android.useAndroidX=false"))
                {
                    lines = lines.Replace("android.useAndroidX=false", "android.useAndroidX=true");
                    File.WriteAllText(gradlePropertiesPath, lines);
                }
            }
        }

        private void ModifyRootBuildGradle(ref string path, GradlePropertiesData properties)
        {
            string gradlePath = Path.Combine(Directory.GetParent(path).FullName, "build.gradle");
            string gradleContents = File.ReadAllText(gradlePath);

            //remove plugins block to get 'classpath' to work
            gradleContents = Regex.Replace(gradleContents, @"plugins\s*\{[^}]*\}\s*", "", RegexOptions.Singleline);

            string addition = rootBuildGradleAddition.Replace("**AGPVERSION**", properties.agpVersion);

            //uncomment R8 if AGP 7 or below
            if (properties.agpMajor <= 7)
                addition = addition.Replace("//classpath 'com.android.tools:r8:8.1.56'", "classpath 'com.android.tools:r8:8.1.56'");

            gradleContents = addition + gradleContents;

            File.WriteAllText(gradlePath, gradleContents);
        }

        private void ModifyEosSdkAarDesugarVersion(ref string path, GradlePropertiesData properties)
        {
            string aarPath = Path.Combine(path, "libs", "eos-sdk.aar");
            if (!File.Exists(aarPath))
            {
                Debug.LogWarning($"Could not find 'eos-sdk.aar' at path '{aarPath}'. The Core Library Desugaring version will not be updated, which could cause the Gradle build to fail.");
                return;
            }

            using (ZipArchive archive = ZipFile.Open(aarPath, ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry = archive.GetEntry("META-INF/com/android/build/gradle/aar-metadata.properties");

                if (entry == null)
                {
                    Debug.LogWarning("AAR metadata entry not found");
                    return;
                }

                using Stream entryStream = entry.Open();
                using StreamReader reader = new StreamReader(entryStream);
                string[] contents = reader.ReadToEnd().Split('\n');
                reader.Close();
                reader.Dispose();

                entry.Delete();

                for (int i = 0; i < contents.Length; i++)
                {
                    if (contents[i].Contains("desugarJdkLib"))
                    {
                        contents[i] = $"desugarJdkLib=com.android.tools:desugar_jdk_libs:{properties.desugarLibVersion}";
                        //TODO: logging here
                    }
                }

                ZipArchiveEntry newEntry = archive.CreateEntry("META-INF/com/android/build/gradle/aar-metadata.properties");
                using StreamWriter writer = new StreamWriter(newEntry.Open());
                writer.Write(string.Join('\n', contents));
            }
        }

        private (string, int) GetCurrentJDKVersion()
        {
            string jdkReleaseFilePath = Path.Combine(AndroidExternalToolsSettings.jdkRootPath, "release");
            if (!File.Exists(jdkReleaseFilePath))
            {
                Debug.LogWarning($"Could not find JDK release file at '{jdkReleaseFilePath}'.");
                return (null, 0);
            }

            string content = File.ReadAllText(jdkReleaseFilePath);
            Match match = Regex.Match(content, @"JAVA_VERSION=""(\d+)\.?(\d+)?\.?(\d+)?");

            if (!match.Success)
            {
                Debug.LogWarning("Could not parse JAVA_VERSION from release file.");
                return (null, 0);
            }

            return (match.Groups[0].Value.Replace("JAVA_VERSION=\"", "").TrimEnd('"'), int.Parse(match.Groups[1].Value));
        }

        private GradlePropertiesData ParseGradleProperties(string unityLibraryPath)
        {
            GradlePropertiesData properties = new GradlePropertiesData();
#if UNITY_6000_0_OR_NEWER
            string filePath = Path.Combine(Directory.GetParent(unityLibraryPath).FullName, "gradle.properties");
            if (!File.Exists(filePath)) return default;

            string[] lines = File.ReadAllLines(filePath);

            foreach (string line in lines)
            {
                if (line.Contains("unity.buildToolsVersion"))
                    properties.buildToolsVersion = GetPropertyValue(line);
                else if (line.Contains("unity.minSdkVersion"))
                    properties.minSdkVersion = uint.Parse(GetPropertyValue(line));
                else if (line.Contains("unity.targetSdkVersion"))
                    properties.targetSdkVersion = uint.Parse(GetPropertyValue(line));
                else if (line.Contains("unity.compileSdkVersion"))
                    properties.compileSdkVersion = uint.Parse(GetPropertyValue(line));
                else if (line.Contains("unity.agpVersion"))
                    AGPVersion(line);
                else if (line.Contains("unity.javaCompatabilityVersion"))
                    properties.javaCompatibilityVersion = GetPropertyValue(line);
            }
#else
            SetGeneralInfo();
            properties.agpVersion = AGPVersion(Path.Combine(Directory.GetParent(unityLibraryPath).FullName, "build.gradle"));
            properties.javaCompatibilityVersion = JdkVersion();
#endif

            properties.desugarLibVersion = SetCoreLibDesugaringVersion();

            return properties;

#region Extra Methods
#if UNITY_6000_0_OR_NEWER
            string GetPropertyValue(string line) => line.Split('=')[1].Trim();

            void AGPVersion(string line)
            {
                string value = GetPropertyValue(line);
                properties.agpVersion = value;

                string[] parts = value.Split('.');
                if (parts.Length >= 2)
                {
                    properties.agpMajor = uint.Parse(parts[0]);
                    properties.agpMinor = uint.Parse(parts[1]);
                }
            }
#else
            //build tools version, min sdk, target sdk, compile sdk
            void SetGeneralInfo()
            {
                string filePath = Path.Combine(unityLibraryPath, "build.gradle");
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"Cannot get Android plugin general info. Reason: path '{filePath}' does not exist.");
                    return;
                }

                string[] lines = File.ReadAllLines(filePath);

                foreach (string line in lines)
                {
                    if (line.Contains("buildToolsVersion"))
                        properties.buildToolsVersion = ParseValue(line);
                    else if (line.Contains("minSdkVersion"))
                        properties.minSdkVersion = uint.Parse(ParseValue(line));
                    else if (line.Contains("targetSdkVersion"))
                        properties.targetSdkVersion = uint.Parse(ParseValue(line));
                    else if (line.Contains("compileSdkVersion"))
                        properties.compileSdkVersion = uint.Parse(ParseValue(line));
                }
            }

            //agp version
            string AGPVersion(string rootBuildGradlePath)
            {
                if (!File.Exists(rootBuildGradlePath))
                {
                    Debug.LogError($"Cannot get Android Gradle Plugin version. Reason: path '{rootBuildGradlePath}' does not exist.");
                    return null;
                }

                string content = File.ReadAllText(rootBuildGradlePath);

                //id 'com.android.application' version 'X.X.X' apply false
                Match pluginsMatch = Regex.Match(content, @"id\s+'com\.android\.(?:application|library)'\s+version\s+'([\d.]+)'");
                if (pluginsMatch.Success)
                {
                    string value = pluginsMatch.Groups[1].Value;
                    string[] parts = value.Split('.');
                    if (parts.Length >= 2)
                    {
                        properties.agpMajor = uint.Parse(parts[0]);
                        properties.agpMinor = uint.Parse(parts[1]);
                    }
                    Debug.Log($"Got AGP version: {value}");
                    return value;
                }

                //classpath 'com.android.tools.build:gradle:X.X.X'
                Match classpathMatch = Regex.Match(content, @"classpath\s+['""]com\.android\.tools\.build:gradle:([\d.]+)['""]");
                if (classpathMatch.Success)
                {
                    string value = classpathMatch.Groups[1].Value;
                    string[] parts = value.Split('.');
                    if (parts.Length >= 2)
                    {
                        properties.agpMajor = uint.Parse(parts[0]);
                        properties.agpMinor = uint.Parse(parts[1]);
                    }
                    Debug.Log($"Got AGP version: {value}");
                    return value;
                }

                Debug.LogWarning("AGP match unsuccessful. Defaulting to 7.4.2.");
                return "7.4.2";
            }

            string JdkVersion()
            {
                string filePath = Path.Combine(unityLibraryPath, "build.gradle");
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"Cannot get OpenJDK version. Reason: path '{filePath}' does not exist.");
                    return null;
                }

                string content = File.ReadAllText(filePath);

                //matching "sourceCompatibility JavaVersion.VERSION_X"
                Match match = Regex.Match(content, @"(?:sourceCompatibility|targetCompatibility)\s+(?:JavaVersion\.)?(VERSION_\d+(?:_\d+)?)", RegexOptions.IgnoreCase);

                if (match.Success) return match.Groups[1].Value;

                Debug.LogWarning("JDK match unsuccessful. Defaulting to OpenJDK 11.");
                return "VERSION_11";
            }

            string ParseValue(string line)
            {
                //'key=value'
                Match match = Regex.Match(line, @"=\s*['""]?([^'""]+?)['""]?\s*$");
                if (match.Success)
                {
                    string value = match.Groups[1].Value.Trim();
                    return value;
                }

                //'key value'
                match = Regex.Match(line, @"(buildToolsVersion|minSdkVersion|targetSdkVersion|compileSdkVersion)\s+['""]?([^'""]+?)['""]?\s*$");
                if (match.Success)
                {
                    string value = match.Groups[2].Value.Trim();
                    return value;
                }

                Debug.LogWarning($"Parsing line '{line}' failed.");
                return null;
            }
#endif

            string SetCoreLibDesugaringVersion()
            {
                if (properties.agpMajor >= 8)
                    return "2.1.5";
                else if (properties.agpMajor == 7 && properties.agpMinor >= 4)
                    return "2.0.3";
                else if (properties.agpMajor == 7 && properties.agpMinor == 3)
                    return "1.2.3";
                else if (properties.agpMajor >= 4)
                    return "1.1.9";
                else
                {
                    Debug.LogWarning($"AGP version '{properties.agpVersion}' is not fully implemented with EOSTransport. Your build could fail.");
                    return "1.1.9";
                }
            }
#endregion
        }

        private struct GradlePropertiesData
        {
            public string buildToolsVersion; //ex "36.0.0"
            public uint minSdkVersion; //ex 32
            public uint targetSdkVersion; //ex 34
            public uint compileSdkVersion; //ex 34

            public string agpVersion; //ex "8.10.0"
            public uint agpMajor; //ex 8
            public uint agpMinor; //ex 10

            public string javaCompatibilityVersion; //ex "VERSION_17"
            public string desugarLibVersion; //ex "2.0.3"
        }

        private const string rootBuildGradleAddition = @"allprojects {
    buildscript {
        repositories {
            google()
            mavenCentral()
        }

        dependencies {
            classpath 'com.android.tools.build:gradle:**AGPVERSION**'
            //uncomment if using AGP 7 or below (it should uncomment automatically)
            //classpath 'com.android.tools:r8:8.1.56'
        }
    }

    repositories {
        google()
        mavenCentral()
        maven {
            url = uri(""https://storage.googleapis.com/r8-releases/raw"")
        }
        flatDir {
            dirs ""${project(':unityLibrary').projectDir}/libs""
        }
    }
}
";
#endif
#endregion
    }
}
