using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EpicTransport.Editor
{
    public class TransportEditorHelper
    {
        [InitializeOnLoadMethod]
        public static void DeleteObsoleteScripts()
        {
            #region Delete TransportAndroidUtils.cs
            string[] tauFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "TransportAndroidUtils.cs", SearchOption.AllDirectories);
            string tau = string.Empty;
            foreach (string tauFile in tauFiles.Where(path => path.Contains(Path.Combine("Editor", "TransportAndroidUtils.cs")))) { tau = tauFile; break; }

            if (!string.IsNullOrWhiteSpace(tau))
            {
                Debug.Log($"Deleting obsolete script: {tau}");
                File.Delete(tau);
                File.Delete(tau + ".meta");
            }
            #endregion
        }
    }
}
