using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FactoryTwinEditor
{
    /// <summary>
    /// Keeps the editor Game preview at a readable fit scale. This only changes
    /// the Unity editor preview and has no effect on a standalone build.
    /// </summary>
    [InitializeOnLoad]
    internal static class FactoryGameViewSetup
    {
        static FactoryGameViewSetup()
        {
            EditorApplication.delayCall += FitGamePreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.delayCall += FitGamePreview;
            }
        }

        [MenuItem("Factory Twin/Fit Game Preview")]
        private static void FitGamePreview()
        {
            System.Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
            {
                return;
            }

            Object[] gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
            FieldInfo zoomField = gameViewType.GetField("m_ZoomArea", BindingFlags.Instance | BindingFlags.NonPublic);
            if (zoomField == null)
            {
                return;
            }

            foreach (Object gameView in gameViews)
            {
                // High-DPI Windows displays can enable the editor's low-resolution
                // aspect-ratio mode. It renders fewer pixels and then enlarges them,
                // which is the main reason the Play preview looks blurred.
                PropertyInfo lowResolutionProperty = gameViewType.GetProperty(
                    "lowResolutionForAspectRatios", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (lowResolutionProperty != null && lowResolutionProperty.CanWrite)
                {
                    lowResolutionProperty.SetValue(gameView, false, null);
                }
                FieldInfo lowResolutionField = gameViewType.GetField(
                    "m_LowResolutionForAspectRatios", BindingFlags.Instance | BindingFlags.NonPublic);
                if (lowResolutionField != null)
                {
                    if (lowResolutionField.FieldType == typeof(bool))
                    {
                        lowResolutionField.SetValue(gameView, false);
                    }
                    else if (lowResolutionField.FieldType == typeof(bool[]))
                    {
                        bool[] values = lowResolutionField.GetValue(gameView) as bool[];
                        if (values != null)
                        {
                            for (int i = 0; i < values.Length; i++) values[i] = false;
                            lowResolutionField.SetValue(gameView, values);
                        }
                    }
                }

                FieldInfo defaultScaleField = gameViewType.GetField(
                    "m_defaultScale", BindingFlags.Instance | BindingFlags.NonPublic);
                defaultScaleField?.SetValue(gameView, 1f);

                object zoomArea = zoomField.GetValue(gameView);
                if (zoomArea == null)
                {
                    continue;
                }

                PropertyInfo scaleWithWindowProperty = zoomArea.GetType().GetProperty(
                    "scaleWithWindow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (scaleWithWindowProperty != null && scaleWithWindowProperty.CanWrite)
                {
                    scaleWithWindowProperty.SetValue(zoomArea, true, null);
                }

                ((EditorWindow)gameView).Repaint();
            }
        }
    }
}
