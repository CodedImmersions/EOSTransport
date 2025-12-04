using UnityEditor;
using UnityEngine;

namespace EpicTransport.Editor
{
    [CustomEditor(typeof(HMSkip))]
    public class HMSkipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            GUILayout.Space(5);

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontStyle = FontStyle.Bold;
            GUILayout.Label("This script tells the HM system to not back up this object.", style);
        }
    }
}
