using UnityEditor;
using UnityEngine;

namespace MultiplayerFork.LevelDesign.Editor
{
    internal static class GeneratedLevelHierarchy
    {
        internal static GeneratedLevelMarker CreateRoot(LevelLayoutData layoutData)
        {
            string layoutName = layoutData != null && !string.IsNullOrWhiteSpace(layoutData.LayoutName)
                ? layoutData.LayoutName
                : "NewLayout";

            GameObject root = new GameObject($"Generated_Level_{layoutName}");
            Undo.RegisterCreatedObjectUndo(root, "Create Generated Level Root");

            GeneratedLevelMarker marker = Undo.AddComponent<GeneratedLevelMarker>(root);
            marker.SyncFrom(layoutData);

            EnsureChild(root.transform, "LayoutData");
            Transform source = EnsureChild(root.transform, "Source");
            EnsureChild(source, "SourceImageReference");
            EnsureChild(source, "CalibrationData");
            EnsureChild(root.transform, "LogicalPreview");
            EnsureChild(root.transform, "Rooms");
            EnsureChild(root.transform, "Corridors");
            EnsureChild(root.transform, "WallPaths");
            Transform walls = EnsureChild(root.transform, "Walls");
            EnsureChild(walls, "GeneratedSplines");
            EnsureChild(root.transform, "Doors");
            EnsureChild(root.transform, "Floors");
            EnsureChild(root.transform, "Ceilings");
            EnsureChild(root.transform, "Decorations");
            EnsureChild(root.transform, "Validation");
            EnsureChild(root.transform, "Debug");
            EnsureChild(root.transform, "TracePaths");
            EnsureChild(root.transform, "TraceMarkers");

            Selection.activeGameObject = root;
            return marker;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
            {
                return child;
            }

            GameObject childObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(childObject, $"Create {name}");
            Undo.SetTransformParent(childObject.transform, parent, $"Parent {name}");
            childObject.transform.localPosition = Vector3.zero;
            childObject.transform.localRotation = Quaternion.identity;
            childObject.transform.localScale = Vector3.one;
            return childObject.transform;
        }
    }
}
