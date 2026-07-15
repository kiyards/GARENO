using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerFork.LevelDesign.Editor
{
    [InitializeOnLoad]
    internal static class UnifiedLevelDesignSceneDebugRenderer
    {
        static UnifiedLevelDesignSceneDebugRenderer()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            UnifiedLevelDesignEditorSession session = UnifiedLevelDesignEditorSession.instance;
            LevelLayoutData layout = ResolveLayoutData(session);
            if (layout == null)
            {
                return;
            }

            GeneratedLevelMarker marker = ResolveMarker(session);
            Matrix4x4 localToWorld = marker != null
                ? marker.transform.localToWorldMatrix
                : Matrix4x4.identity;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            if (session.drawBounds)
            {
                DrawBounds(layout, localToWorld, session.boundsColor);
            }

            if (session.drawWallPaths)
            {
                DrawWallPaths(layout.WallPaths, localToWorld, session);
            }

            if (session.drawDoorMarkers)
            {
                DrawDoors(layout, localToWorld, session);
            }
        }

        private static void DrawBounds(LevelLayoutData layout, Matrix4x4 localToWorld, Color color)
        {
            Vector2 size = layout.SourceMetadata.levelSize;
            Vector3[] corners =
            {
                localToWorld.MultiplyPoint3x4(Vector3.zero),
                localToWorld.MultiplyPoint3x4(new Vector3(size.x, 0f, 0f)),
                localToWorld.MultiplyPoint3x4(new Vector3(size.x, 0f, size.y)),
                localToWorld.MultiplyPoint3x4(new Vector3(0f, 0f, size.y))
            };

            Handles.color = color;
            for (int index = 0; index < corners.Length; index++)
            {
                Vector3 from = corners[index];
                Vector3 to = corners[(index + 1) % corners.Length];
                Handles.DrawAAPolyLine(3f, from, to);
            }
        }

        private static void DrawWallPaths(IReadOnlyList<WallPathData> wallPaths, Matrix4x4 localToWorld, UnifiedLevelDesignEditorSession session)
        {
            for (int pathIndex = 0; pathIndex < wallPaths.Count; pathIndex++)
            {
                WallPathData wallPath = wallPaths[pathIndex];
                if (wallPath == null || wallPath.localPoints == null || wallPath.localPoints.Count < 2)
                {
                    continue;
                }

                Handles.color = wallPath.debugColor.a > 0f ? wallPath.debugColor : session.wallPathColor;
                Vector3[] points = new Vector3[wallPath.localPoints.Count + (wallPath.isClosed ? 1 : 0)];
                for (int pointIndex = 0; pointIndex < wallPath.localPoints.Count; pointIndex++)
                {
                    points[pointIndex] = localToWorld.MultiplyPoint3x4(wallPath.localPoints[pointIndex]);
                }

                if (wallPath.isClosed)
                {
                    points[^1] = points[0];
                }

                Handles.DrawAAPolyLine(4f, points);

                if (session.drawPathLabels)
                {
                    Vector3 labelPosition = points[0] + Vector3.up * 0.15f;
                    Handles.Label(labelPosition, $"{wallPath.displayName} ({wallPath.usage})");
                }
            }
        }

        private static void DrawDoors(LevelLayoutData layout, Matrix4x4 localToWorld, UnifiedLevelDesignEditorSession session)
        {
            Handles.color = session.doorColor;
            foreach (DoorOpeningData opening in layout.DoorOpenings)
            {
                if (opening == null)
                {
                    continue;
                }

                WallPathData wallPath = layout.WallPaths.Find(candidate => candidate.pathId == opening.wallPathId);
                if (wallPath == null || wallPath.localPoints == null || wallPath.localPoints.Count < 2)
                {
                    continue;
                }

                Vector3 worldPosition = EvaluatePositionAlongPath(wallPath, opening.distanceAlongPath, localToWorld);
                float radius = Mathf.Max(0.2f, opening.width * 0.1f);
                Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, radius, EventType.Repaint);

                if (session.drawPathLabels)
                {
                    Handles.Label(worldPosition + Vector3.up * 0.2f, opening.displayName);
                }
            }
        }

        private static Vector3 EvaluatePositionAlongPath(WallPathData wallPath, float distanceAlongPath, Matrix4x4 localToWorld)
        {
            float traversed = 0f;
            for (int index = 0; index < wallPath.localPoints.Count - 1; index++)
            {
                Vector3 from = wallPath.localPoints[index];
                Vector3 to = wallPath.localPoints[index + 1];
                float segmentLength = Vector3.Distance(from, to);
                if (segmentLength <= Mathf.Epsilon)
                {
                    continue;
                }

                if (traversed + segmentLength >= distanceAlongPath)
                {
                    float t = Mathf.InverseLerp(traversed, traversed + segmentLength, distanceAlongPath);
                    return localToWorld.MultiplyPoint3x4(Vector3.Lerp(from, to, t));
                }

                traversed += segmentLength;
            }

            return localToWorld.MultiplyPoint3x4(wallPath.localPoints[^1]);
        }

        private static LevelLayoutData ResolveLayoutData(UnifiedLevelDesignEditorSession session)
        {
            if (session.activeLayoutData != null)
            {
                return session.activeLayoutData;
            }

            if (Selection.activeObject is LevelLayoutData layoutData)
            {
                return layoutData;
            }

            if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out GeneratedLevelMarker marker))
            {
                return marker.LayoutData;
            }

            return null;
        }

        private static GeneratedLevelMarker ResolveMarker(UnifiedLevelDesignEditorSession session)
        {
            if (session.activeGeneratedRoot != null)
            {
                return session.activeGeneratedRoot;
            }

            if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out GeneratedLevelMarker marker))
            {
                return marker;
            }

            return null;
        }
    }
}
