using System;
using UnityEngine;

namespace GRPC.NET.Example
{
    public class GRPCExampleLogger : MonoBehaviour
    {
        private const int MAX_SIZE = 16382;
        private string m_LogContent = "";
        private Vector2 m_LogScrollView = Vector2.zero;

        public void WriteLogOutput(string s)
        {
            m_LogContent += s + "\n";
            while (m_LogContent.Length > MAX_SIZE)
            {
                var index = m_LogContent.IndexOf("\n", StringComparison.Ordinal);
                m_LogContent = m_LogContent.Substring(index + 1);
            }
            m_LogScrollView.y = int.MaxValue;
        }

        void GUIDisplayLog()
        {
            m_LogScrollView = GUILayout.BeginScrollView(m_LogScrollView);

            GUIStyle textStyle = new GUIStyle();
            textStyle.wordWrap = true;
            textStyle.richText = true;
            GUILayout.Label(m_LogContent, textStyle);
            GUILayout.EndScrollView();
        }

        void OnGUI()
        {
            const int pad = 10;

            var logArea = new Rect(pad, Screen.height * (1.0f / 2.0f), Screen.width - 2 * pad, Screen.height * (1.0f / 2.0f) - pad);
            GUILayout.BeginArea(logArea);
            GUIDisplayLog();
            GUILayout.EndArea();
        }
    }
}