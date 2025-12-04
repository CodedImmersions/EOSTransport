#if UNITY_ANDROID
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace EpicTransport.Editor
{
    public class TransportAndroidUtils : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 14;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            #region setting strings
            string clientid = PlayerPrefs.GetString("EOSTransport Client ID");
            if (string.IsNullOrEmpty(clientid)) throw new BuildFailedException("the EOS client id is null... somehow. like, HOW DID YOU MANAGE TO DO THIS? HOW TO FIX you may ask??? Just hit play and LET EOS LOG IN, THEN BUILD AGAIN.");

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

        [InitializeOnLoadMethod]
        private static void GenerateTemplate()
        {

            string template = Path.Combine(Application.dataPath, "Plugins", "Android", "mainTemplate.gradle");
            if (!File.Exists(template))
            {
                Debug.Log("Adding eos-required main gradle template to project.");

                using (WebClient wc = new WebClient())
                {
                    string contents = wc.DownloadString("https://gist.githubusercontent.com/TheTechWiz5305/dc5161b48b3a335c3c5d08151b6d28cc/raw");
                    if (string.IsNullOrEmpty(contents))
                    {
                        Debug.LogWarning("cannot download required main gradle template. please check if you are connected to the internet.");
                        return;
                    }

                    string versfile = Path.Combine(AndroidExternalToolsSettings.jdkRootPath, "release");
                    if (File.Exists(versfile))
                    {
                        string[] lines = File.ReadAllLines(versfile);
                        foreach (string line in lines)
                        {
                            if (line.StartsWith("JAVA_VERSION"))
                            {
                                string jdkversion = Regex.Replace(line, @"^[0-9.]+$", "");
                                if (jdkversion.StartsWith("11")) contents = contents.Replace("JavaVersion.VERSION_17", "JavaVersion.VERSION_11");

                                File.WriteAllText(template, contents);
                            }
                            else continue;
                        }
                    }
                    else Debug.LogWarning("cannot add eos-required main gradle template to project: jdk version not found.");
                }
            }
        }
    }
}
#endif
