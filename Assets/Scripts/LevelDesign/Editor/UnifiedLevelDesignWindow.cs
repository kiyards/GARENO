using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerFork.LevelDesign.Editor
{
    public sealed class UnifiedLevelDesignWindow : EditorWindow
    {
        private enum Tab
        {
            Procedural,
            ImageImport
        }

        private const string WindowTitle = "Unified Level Design Toolkit";
        private const string DefaultDataFolder = "Assets/LevelDesignToolkit/LayoutData";
        private const string SampleDataFolder = "Assets/LevelDesignToolkit/Samples";
        private const string ArtTestScenePath = "Assets/Scenes/ArtTestScene.unity";

        [SerializeField] private string newLayoutName = "NewLayout";
        [SerializeField] private DefaultAsset layoutDataFolderAsset;
        [SerializeField] private LayoutSourceMode pendingSourceMode = LayoutSourceMode.Procedural;

        private Vector2 scrollPosition;

        [MenuItem("Elenroth Tools/Level Design/Unified Level Design Toolkit")]
        public static void OpenWindow()
        {
            GetWindow<UnifiedLevelDesignWindow>(WindowTitle);
        }

        private void OnEnable()
        {
            if (layoutDataFolderAsset == null)
            {
                layoutDataFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(DefaultDataFolder);
            }
        }

        private void OnGUI()
        {
            UnifiedLevelDesignEditorSession session = UnifiedLevelDesignEditorSession.instance;

            DrawHeader(session);
            DrawTabs(session);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            switch ((Tab)session.activeTab)
            {
                case Tab.Procedural:
                    DrawProceduralTab(session);
                    break;
                case Tab.ImageImport:
                    DrawImageImportTab(session);
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader(UnifiedLevelDesignEditorSession session)
        {
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            session.activeLayoutData = (LevelLayoutData)EditorGUILayout.ObjectField("Active Layout Data", session.activeLayoutData, typeof(LevelLayoutData), false);
            session.activeGeneratedRoot = (GeneratedLevelMarker)EditorGUILayout.ObjectField("Active Generated Root", session.activeGeneratedRoot, typeof(GeneratedLevelMarker), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection"))
                {
                    PullSelectionIntoSession(session);
                }

                if (GUILayout.Button("Save Session"))
                {
                    session.Save();
                }
            }

            if (session.activeLayoutData != null)
            {
                EditorGUILayout.HelpBox(
                    $"State: {session.activeLayoutData.State} | Source: {session.activeLayoutData.SourceMetadata.sourceMode} | Seed: {session.activeLayoutData.SourceMetadata.generationSeed}",
                    MessageType.Info);
            }
        }

        private void DrawTabs(UnifiedLevelDesignEditorSession session)
        {
            string[] tabs =
            {
                "Procedural",
                "Image Import"
            };

            int selected = GUILayout.Toolbar(session.activeTab, tabs);
            if (selected != session.activeTab)
            {
                session.activeTab = selected;
                session.Save();
            }
        }

        private void DrawProceduralTab(UnifiedLevelDesignEditorSession session)
        {
            DrawSimpleSetup(session, LayoutSourceMode.Procedural);
            LevelLayoutData layout = RequireActiveLayoutData(session, LayoutSourceMode.Procedural);
            if (layout == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Quick Procedural Generator", EditorStyles.boldLabel);
            session.quickWallPrefab = (GameObject)EditorGUILayout.ObjectField("Wall Prefab", session.quickWallPrefab, typeof(GameObject), false);
            session.quickDoorPrefab = (GameObject)EditorGUILayout.ObjectField("Door Prefab", session.quickDoorPrefab, typeof(GameObject), false);
            session.quickLayoutSize = EditorGUILayout.Vector2Field("Layout Size", session.quickLayoutSize);
            session.quickMinRoomSize = EditorGUILayout.Vector2Field("Min Room Size", session.quickMinRoomSize);
            session.quickMaxRoomSize = EditorGUILayout.Vector2Field("Max Room Size", session.quickMaxRoomSize);
            session.quickRoomCount = EditorGUILayout.IntField("Room Count", session.quickRoomCount);
            session.quickGenerateCorridors = EditorGUILayout.Toggle("Generate Corridors", session.quickGenerateCorridors);
            using (new EditorGUI.DisabledScope(!session.quickGenerateCorridors))
            {
                session.quickCorridorWidth = EditorGUILayout.FloatField("Corridor Width", session.quickCorridorWidth);
            }

            session.quickWallPieceSpacing = EditorGUILayout.FloatField("Wall Piece Spacing", session.quickWallPieceSpacing);
            session.quickWallHeight = EditorGUILayout.FloatField("Placeholder Wall Height", session.quickWallHeight);
            session.quickWallThickness = EditorGUILayout.FloatField("Placeholder Wall Thickness", session.quickWallThickness);
            session.quickRoomPadding = EditorGUILayout.FloatField("Room Padding", session.quickRoomPadding);
            session.quickRandomizeSeedOnGenerate = EditorGUILayout.Toggle("Randomize Seed On Generate", session.quickRandomizeSeedOnGenerate);

            Undo.RecordObject(layout, "Edit Procedural Layout Metadata");
            layout.LayoutName = EditorGUILayout.TextField("Layout Name", layout.LayoutName);
            layout.SourceMetadata.levelSize = EditorGUILayout.Vector2Field("Level Size", layout.SourceMetadata.levelSize);
            layout.SourceMetadata.cellSize = EditorGUILayout.FloatField("Cell Size", layout.SourceMetadata.cellSize);
            layout.SourceMetadata.generationSeed = EditorGUILayout.IntField("Seed", layout.SourceMetadata.generationSeed);
            layout.SourceMetadata.notes = EditorGUILayout.TextField("Notes", layout.SourceMetadata.notes);
            layout.State = (GeneratedLayoutState)EditorGUILayout.EnumPopup("State", layout.State);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Randomized Base Layout", GUILayout.Height(28)))
                {
                    GenerateQuickProceduralLayout(session, layout, rebuildVisuals: false);
                }

                if (GUILayout.Button("Generate Layout + Scene Geometry", GUILayout.Height(28)))
                {
                    GenerateQuickProceduralLayout(session, layout, rebuildVisuals: true);
                }
            }

            if (GUILayout.Button("Randomize Seed"))
            {
                Undo.RecordObject(layout, "Randomize Layout Seed");
                layout.SourceMetadata.generationSeed = Random.Range(int.MinValue, int.MaxValue);
                EditorUtility.SetDirty(layout);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scene Debug", EditorStyles.boldLabel);
            session.drawBounds = EditorGUILayout.Toggle("Draw Bounds", session.drawBounds);
            session.drawWallPaths = EditorGUILayout.Toggle("Draw Layout Paths", session.drawWallPaths);
            session.drawDoorMarkers = EditorGUILayout.Toggle("Draw Door Markers", session.drawDoorMarkers);
            session.drawPathLabels = EditorGUILayout.Toggle("Draw Labels", session.drawPathLabels);

            EditorUtility.SetDirty(layout);
        }

        private void DrawImageImportTab(UnifiedLevelDesignEditorSession session)
        {
            DrawSimpleSetup(session, LayoutSourceMode.ImageImport);
            LevelLayoutData layout = RequireActiveLayoutData(session, LayoutSourceMode.ImageImport);
            if (layout == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Image Import", EditorStyles.boldLabel);
            session.defaultImageTraceProfile = (ImageTraceProfile)EditorGUILayout.ObjectField(
                "Trace Profile",
                session.defaultImageTraceProfile,
                typeof(ImageTraceProfile),
                false);
            session.quickWallPrefab = (GameObject)EditorGUILayout.ObjectField("Wall Prefab", session.quickWallPrefab, typeof(GameObject), false);
            session.quickDoorPrefab = (GameObject)EditorGUILayout.ObjectField("Door Prefab", session.quickDoorPrefab, typeof(GameObject), false);

            Undo.RecordObject(layout, "Edit Image Layout Metadata");
            layout.LayoutName = EditorGUILayout.TextField("Layout Name", layout.LayoutName);
            layout.SourceMetadata.sourceImageAssetPath = EditorGUILayout.TextField("Source Image Asset Path", layout.SourceMetadata.sourceImageAssetPath);
            layout.SourceMetadata.levelSize = EditorGUILayout.Vector2Field("World Size", layout.SourceMetadata.levelSize);
            layout.SourceMetadata.worldOrigin = EditorGUILayout.Vector3Field("World Origin", layout.SourceMetadata.worldOrigin);
            layout.SourceMetadata.worldEulerRotation = EditorGUILayout.Vector3Field("World Rotation", layout.SourceMetadata.worldEulerRotation);
            layout.SourceMetadata.horizontalFlip = EditorGUILayout.Toggle("Horizontal Flip", layout.SourceMetadata.horizontalFlip);
            layout.SourceMetadata.verticalFlip = EditorGUILayout.Toggle("Vertical Flip", layout.SourceMetadata.verticalFlip);
            layout.SourceMetadata.imageOpacity = EditorGUILayout.Slider("Image Opacity", layout.SourceMetadata.imageOpacity, 0f, 1f);
            layout.SourceMetadata.imageElevation = EditorGUILayout.FloatField("Image Elevation", layout.SourceMetadata.imageElevation);
            layout.State = (GeneratedLayoutState)EditorGUILayout.EnumPopup("State", layout.State);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Image Preview", GUILayout.Height(28)))
                {
                    if (TryLoadReadableSourceTexture(layout, out Texture2D previewTexture))
                    {
                        EnsureLayoutRootExists(session, layout);
                        UpdateSourceImagePreview(layout, previewTexture);
                    }
                }

                if (GUILayout.Button("Trace Image To Wall Paths", GUILayout.Height(28)))
                {
                    TraceImageLayout(session, layout, rebuildVisuals: false);
                }

                if (GUILayout.Button("Trace Image + Scene Geometry", GUILayout.Height(28)))
                {
                    TraceImageLayout(session, layout, rebuildVisuals: true);
                }
            }

            EditorGUILayout.HelpBox(
                "Use a black-and-white image where dark pixels are walls and bright pixels are empty space. The tracer converts those wall pixels into orthogonal wall paths, then can rebuild scene geometry with your assigned wall prefab.",
                MessageType.Info);

            EditorUtility.SetDirty(layout);
        }

        private void DrawSimpleSetup(UnifiedLevelDesignEditorSession session, LayoutSourceMode mode)
        {
            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            newLayoutName = EditorGUILayout.TextField("Layout Name", newLayoutName);
            layoutDataFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField("Layout Data Folder", layoutDataFolderAsset, typeof(DefaultAsset), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(mode == LayoutSourceMode.Procedural ? "New Procedural Layout" : "New Image Layout", GUILayout.Height(28)))
                {
                    pendingSourceMode = mode;
                    session.activeLayoutData = CreateLayoutAsset();
                    session.activeGeneratedRoot = null;
                }

                using (new EditorGUI.DisabledScope(session.activeLayoutData == null))
                {
                    if (GUILayout.Button("Create / Refresh Scene Root", GUILayout.Height(28)))
                    {
                        EnsureLayoutRootExists(session, session.activeLayoutData);
                    }
                }
            }

            if (mode == LayoutSourceMode.Procedural && GUILayout.Button("Create Example In ArtTestScene", GUILayout.Height(24)))
            {
                CreateExampleInArtTestScene(session);
            }

            EditorGUILayout.HelpBox(
                "Pick or create one layout asset, then use only this tab for that workflow. `Use Selection` also works if you click a layout asset or generated root in the project/scene.",
                MessageType.None);
        }

        private void DrawTraceEditorTab(UnifiedLevelDesignEditorSession session)
        {
            LevelLayoutData layout = RequireActiveLayoutData(session, null);
            if (layout == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Wall Paths", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Empty Wall Path"))
            {
                Undo.RecordObject(layout, "Add Wall Path");
                layout.WallPaths.Add(new WallPathData
                {
                    displayName = $"WallPath_{layout.WallPaths.Count + 1:000}"
                });
                EditorUtility.SetDirty(layout);
            }

            if (GUILayout.Button("Add Debug Rectangle"))
            {
                AddDebugRectangle(layout);
            }

            for (int pathIndex = 0; pathIndex < layout.WallPaths.Count; pathIndex++)
            {
                WallPathData wallPath = layout.WallPaths[pathIndex];
                if (wallPath == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");
                wallPath.displayName = EditorGUILayout.TextField("Name", wallPath.displayName);
                wallPath.usage = (WallPathUsage)EditorGUILayout.EnumPopup("Usage", wallPath.usage);
                wallPath.isClosed = EditorGUILayout.Toggle("Closed", wallPath.isClosed);
                wallPath.debugColor = EditorGUILayout.ColorField("Color", wallPath.debugColor);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add Point"))
                    {
                        Undo.RecordObject(layout, "Add Wall Path Point");
                        wallPath.localPoints.Add(Vector3.zero);
                        EditorUtility.SetDirty(layout);
                    }

                    if (GUILayout.Button("Remove Path"))
                    {
                        Undo.RecordObject(layout, "Remove Wall Path");
                        layout.WallPaths.RemoveAt(pathIndex);
                        EditorUtility.SetDirty(layout);
                        EditorGUILayout.EndVertical();
                        break;
                    }
                }

                for (int pointIndex = 0; pointIndex < wallPath.localPoints.Count; pointIndex++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        wallPath.localPoints[pointIndex] = EditorGUILayout.Vector3Field($"Point {pointIndex}", wallPath.localPoints[pointIndex]);
                        if (GUILayout.Button("X", GUILayout.Width(24)))
                        {
                            Undo.RecordObject(layout, "Remove Wall Path Point");
                            wallPath.localPoints.RemoveAt(pointIndex);
                            EditorUtility.SetDirty(layout);
                            break;
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Door Openings", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Door Opening"))
            {
                Undo.RecordObject(layout, "Add Door Opening");
                layout.DoorOpenings.Add(new DoorOpeningData
                {
                    displayName = $"Door_{layout.DoorOpenings.Count + 1:000}"
                });
                EditorUtility.SetDirty(layout);
            }

            for (int doorIndex = 0; doorIndex < layout.DoorOpenings.Count; doorIndex++)
            {
                DoorOpeningData opening = layout.DoorOpenings[doorIndex];
                if (opening == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");
                opening.displayName = EditorGUILayout.TextField("Name", opening.displayName);
                opening.wallPathId = EditorGUILayout.TextField("Wall Path Id", opening.wallPathId);
                opening.distanceAlongPath = EditorGUILayout.FloatField("Distance Along Path", opening.distanceAlongPath);
                opening.width = EditorGUILayout.FloatField("Width", opening.width);
                opening.height = EditorGUILayout.FloatField("Height", opening.height);
                opening.doorType = EditorGUILayout.TextField("Door Type", opening.doorType);
                opening.doorPrefab = (GameObject)EditorGUILayout.ObjectField("Door Prefab", opening.doorPrefab, typeof(GameObject), false);

                if (GUILayout.Button("Remove Door"))
                {
                    Undo.RecordObject(layout, "Remove Door Opening");
                    layout.DoorOpenings.RemoveAt(doorIndex);
                    EditorUtility.SetDirty(layout);
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawBuildTab(UnifiedLevelDesignEditorSession session)
        {
            EditorGUILayout.LabelField("Build Pipeline Stub", EditorStyles.boldLabel);
            session.defaultWallBuildProfile = (WallBuildProfile)EditorGUILayout.ObjectField(
                "Wall Build Profile",
                session.defaultWallBuildProfile,
                typeof(WallBuildProfile),
                false);
            EditorGUILayout.HelpBox(
                "For the quick prototype path, assign a wall prefab and optional door prefab in the Procedural tab. If left empty, the toolkit will spawn cube placeholders so you can still inspect and iterate on the generated layout.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Phase 1 stops before spline creation and modular wall placement. The current project already uses Unity Splines with `SplineContainer` plus an instantiated-wall component, so Phase 4 should adapt to that instead of replacing it.",
                MessageType.Info);
        }

        private void DrawValidationTab(UnifiedLevelDesignEditorSession session)
        {
            LevelLayoutData layout = session.activeLayoutData;
            if (layout == null)
            {
                EditorGUILayout.HelpBox("Select or create a layout asset first.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Validation Snapshot", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Wall Paths", layout.WallPaths.Count.ToString());
            EditorGUILayout.LabelField("Door Openings", layout.DoorOpenings.Count.ToString());
            EditorGUILayout.LabelField("Rooms", layout.Rooms.Count.ToString());
            EditorGUILayout.LabelField("Corridors", layout.Corridors.Count.ToString());

            if (layout.WallPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("No wall paths exist yet. This is expected in Phase 1 until procedural generation or manual tracing adds them.", MessageType.Warning);
            }
        }

        private void DrawProfilesTab(UnifiedLevelDesignEditorSession session)
        {
            EditorGUILayout.LabelField("Default Profile References", EditorStyles.boldLabel);
            session.defaultProceduralProfile = (ProceduralLayoutProfile)EditorGUILayout.ObjectField("Procedural", session.defaultProceduralProfile, typeof(ProceduralLayoutProfile), false);
            session.defaultImageTraceProfile = (ImageTraceProfile)EditorGUILayout.ObjectField("Image Trace", session.defaultImageTraceProfile, typeof(ImageTraceProfile), false);
            session.defaultWallBuildProfile = (WallBuildProfile)EditorGUILayout.ObjectField("Wall Build", session.defaultWallBuildProfile, typeof(WallBuildProfile), false);
            session.defaultModularPieceLibrary = (ModularPieceLibrary)EditorGUILayout.ObjectField("Modular Library", session.defaultModularPieceLibrary, typeof(ModularPieceLibrary), false);
            session.defaultDecorationRuleSet = (DecorationRuleSet)EditorGUILayout.ObjectField("Decoration Rules", session.defaultDecorationRuleSet, typeof(DecorationRuleSet), false);
        }

        private void DrawDebugTab(UnifiedLevelDesignEditorSession session)
        {
            EditorGUILayout.LabelField("Scene Debug Drawing", EditorStyles.boldLabel);
            session.drawBounds = EditorGUILayout.Toggle("Draw Bounds", session.drawBounds);
            session.drawWallPaths = EditorGUILayout.Toggle("Draw Wall Paths", session.drawWallPaths);
            session.drawDoorMarkers = EditorGUILayout.Toggle("Draw Door Markers", session.drawDoorMarkers);
            session.drawPathLabels = EditorGUILayout.Toggle("Draw Labels", session.drawPathLabels);
            session.boundsColor = EditorGUILayout.ColorField("Bounds Color", session.boundsColor);
            session.wallPathColor = EditorGUILayout.ColorField("Fallback Wall Color", session.wallPathColor);
            session.doorColor = EditorGUILayout.ColorField("Door Color", session.doorColor);
            EditorGUILayout.HelpBox("Wall-path debug drawing appears in the Scene view for the active layout asset or selected generated root.", MessageType.None);
        }

        private LevelLayoutData CreateLayoutAsset()
        {
            string folderPath = ResolveOrCreateDataFolder();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{newLayoutName}.asset");

            LevelLayoutData layout = CreateInstance<LevelLayoutData>();
            layout.LayoutName = newLayoutName;
            layout.SourceMetadata.sourceMode = pendingSourceMode;
            layout.SourceMetadata.layoutId = System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateAsset(layout, assetPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = layout;
            EditorGUIUtility.PingObject(layout);
            return layout;
        }

        private string ResolveOrCreateDataFolder()
        {
            string folderPath = layoutDataFolderAsset != null
                ? AssetDatabase.GetAssetPath(layoutDataFolderAsset)
                : DefaultDataFolder;

            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return folderPath;
            }

            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }

            layoutDataFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            return folderPath;
        }

        private static string EnsureFolderPath(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return folderPath;
            }

            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }

            return folderPath;
        }

        private static void PullSelectionIntoSession(UnifiedLevelDesignEditorSession session)
        {
            if (Selection.activeObject is LevelLayoutData layoutData)
            {
                session.activeLayoutData = layoutData;
                session.activeGeneratedRoot = null;
                return;
            }

            if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out GeneratedLevelMarker marker))
            {
                session.activeGeneratedRoot = marker;
                session.activeLayoutData = marker.LayoutData;
            }
        }

        private static LevelLayoutData RequireActiveLayoutData(UnifiedLevelDesignEditorSession session, LayoutSourceMode? enforceMode)
        {
            if (session.activeLayoutData == null)
            {
                EditorGUILayout.HelpBox("Create or select a layout asset first.", MessageType.Info);
                return null;
            }

            if (enforceMode.HasValue)
            {
                session.activeLayoutData.SourceMetadata.sourceMode = enforceMode.Value;
            }

            return session.activeLayoutData;
        }

        private static void AddDebugRectangle(LevelLayoutData layout)
        {
            Undo.RecordObject(layout, "Add Debug Rectangle");

            float width = Mathf.Max(4f, layout.SourceMetadata.levelSize.x * 0.5f);
            float length = Mathf.Max(4f, layout.SourceMetadata.levelSize.y * 0.5f);
            WallPathData wallPath = new WallPathData
            {
                displayName = $"WallPath_{layout.WallPaths.Count + 1:000}",
                isClosed = true,
                localPoints =
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(width, 0f, 0f),
                    new Vector3(width, 0f, length),
                    new Vector3(0f, 0f, length)
                }
            };

            layout.WallPaths.Add(wallPath);
            EditorUtility.SetDirty(layout);
        }

        private static void CreateExampleInArtTestScene(UnifiedLevelDesignEditorSession session)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ArtTestScenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            string sampleFolder = EnsureFolderPath(SampleDataFolder);
            string assetPath = $"{sampleFolder}/ArtTestScene_ProceduralToolkitExample.asset";
            LevelLayoutData layout = AssetDatabase.LoadAssetAtPath<LevelLayoutData>(assetPath);
            if (layout == null)
            {
                layout = CreateInstance<LevelLayoutData>();
                AssetDatabase.CreateAsset(layout, assetPath);
            }

            Undo.RecordObject(layout, "Create Toolkit Example Layout");
            PopulateExampleLayout(layout);
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();

            GeneratedLevelMarker existingMarker = FindGeneratedRootForLayout(layout);
            if (existingMarker != null)
            {
                Undo.DestroyObjectImmediate(existingMarker.gameObject);
            }

            GeneratedLevelMarker marker = GeneratedLevelHierarchy.CreateRoot(layout);
            marker.transform.position = layout.SourceMetadata.worldOrigin;
            marker.transform.rotation = Quaternion.Euler(layout.SourceMetadata.worldEulerRotation);
            marker.SyncFrom(layout);
            EditorUtility.SetDirty(marker);

            session.activeLayoutData = layout;
            session.activeGeneratedRoot = marker;
            session.activeTab = (int)Tab.Procedural;
            session.Save();

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = marker.gameObject;

            EditorUtility.DisplayDialog(
                WindowTitle,
                "Created the sample layout in ArtTestScene. Use the Procedural tab and Scene view debug toggles to inspect the generated wall paths and door markers.",
                "OK");
        }

        private static void GenerateQuickProceduralLayout(UnifiedLevelDesignEditorSession session, LevelLayoutData layout, bool rebuildVisuals)
        {
            if (session.quickRandomizeSeedOnGenerate)
            {
                layout.SourceMetadata.generationSeed = Random.Range(int.MinValue, int.MaxValue);
            }

            layout.LayoutName = string.IsNullOrWhiteSpace(layout.LayoutName) ? "ProceduralLayout" : layout.LayoutName;
            layout.SourceMetadata.sourceMode = LayoutSourceMode.Procedural;
            layout.SourceMetadata.levelSize = session.quickLayoutSize;
            layout.SourceMetadata.notes = "Generated with the toolkit quick procedural generator.";
            layout.State = GeneratedLayoutState.Generated;

            GenerateRoomAndCorridorLayout(layout, session);
            EditorUtility.SetDirty(layout);

            if (!rebuildVisuals)
            {
                return;
            }

            EnsureLayoutRootExists(session, layout);
            RebuildQuickSceneGeometry(session, layout);
        }

        private static void TraceImageLayout(UnifiedLevelDesignEditorSession session, LevelLayoutData layout, bool rebuildVisuals)
        {
            if (!TryLoadReadableSourceTexture(layout, out Texture2D texture))
            {
                EditorUtility.DisplayDialog(
                    WindowTitle,
                    "The source image could not be loaded. Make sure `Source Image Asset Path` points to a texture inside the Unity project.",
                    "OK");
                return;
            }

            Undo.RecordObject(layout, "Trace Image Layout");
            layout.ResetIdsIfMissing();
            layout.SourceMetadata.sourceMode = LayoutSourceMode.ImageImport;
            layout.State = GeneratedLayoutState.Generated;
            layout.SourceMetadata.sourceImageGuid = AssetDatabase.AssetPathToGUID(layout.SourceMetadata.sourceImageAssetPath);
            layout.SourceMetadata.notes = $"Traced from image: {Path.GetFileName(layout.SourceMetadata.sourceImageAssetPath)}";

            BuildWallPathsFromTexture(layout, texture, session.defaultImageTraceProfile);
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();

            if (!rebuildVisuals)
            {
                EnsureLayoutRootExists(session, layout);
                UpdateSourceImagePreview(layout, texture);
                return;
            }

            EnsureLayoutRootExists(session, layout);
            UpdateSourceImagePreview(layout, texture);
            RebuildQuickSceneGeometry(session, layout);
        }

        private static void EnsureLayoutRootExists(UnifiedLevelDesignEditorSession session, LevelLayoutData layout)
        {
            if (session.activeGeneratedRoot != null && session.activeGeneratedRoot.LayoutData == layout)
            {
                return;
            }

            GeneratedLevelMarker marker = FindGeneratedRootForLayout(layout);
            if (marker == null)
            {
                marker = GeneratedLevelHierarchy.CreateRoot(layout);
            }
            else
            {
                marker.SyncFrom(layout);
            }

            marker.transform.position = layout.SourceMetadata.worldOrigin;
            marker.transform.rotation = Quaternion.Euler(layout.SourceMetadata.worldEulerRotation);
            session.activeGeneratedRoot = marker;
            session.activeLayoutData = layout;
        }

        private static void UpdateSourceImagePreview(LevelLayoutData layout, Texture2D texture)
        {
            GeneratedLevelMarker marker = FindGeneratedRootForLayout(layout);
            if (marker == null || texture == null)
            {
                return;
            }

            Transform sourceRoot = marker.transform.Find("Source/SourceImageReference");
            if (sourceRoot == null)
            {
                return;
            }

            Transform previewTransform = sourceRoot.Find("ImagePreview");
            GameObject previewObject;
            if (previewTransform == null)
            {
                previewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Undo.RegisterCreatedObjectUndo(previewObject, "Create Image Preview");
                Undo.SetTransformParent(previewObject.transform, sourceRoot, "Parent Image Preview");
                previewObject.name = "ImagePreview";

                Collider collider = previewObject.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }
            }
            else
            {
                previewObject = previewTransform.gameObject;
            }

            previewObject.transform.localPosition = new Vector3(
                layout.SourceMetadata.levelSize.x * 0.5f,
                layout.SourceMetadata.imageElevation,
                layout.SourceMetadata.levelSize.y * 0.5f);
            previewObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            previewObject.transform.localScale = new Vector3(layout.SourceMetadata.levelSize.x, layout.SourceMetadata.levelSize.y, 1f);

            MeshRenderer renderer = previewObject.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            Material material = renderer.sharedMaterial;
            bool needsMaterial = material == null
                || material.shader == null
                || material.mainTexture != texture
                || material.name != "ImagePreviewMaterial";
            if (needsMaterial)
            {
                Shader shader = Shader.Find("Unlit/Transparent");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }

                material = new Material(shader)
                {
                    name = "ImagePreviewMaterial",
                    mainTexture = texture
                };
                renderer.sharedMaterial = material;
            }

            material.mainTexture = texture;
            Color color = Color.white;
            color.a = Mathf.Clamp01(layout.SourceMetadata.imageOpacity);
            material.color = color;

            EditorUtility.SetDirty(previewObject);
        }

        private static void RebuildQuickSceneGeometry(UnifiedLevelDesignEditorSession session, LevelLayoutData layout)
        {
            GeneratedLevelMarker marker = FindGeneratedRootForLayout(layout);
            if (marker == null)
            {
                return;
            }

            Transform wallsParent = marker.transform.Find("Walls");
            Transform doorsParent = marker.transform.Find("Doors");
            if (wallsParent == null || doorsParent == null)
            {
                return;
            }

            ClearGeneratedChildren(wallsParent);
            ClearGeneratedChildren(doorsParent);

            foreach (WallPathData wallPath in layout.WallPaths)
            {
                BuildQuickWallPath(session, wallsParent, wallPath);
            }

            foreach (DoorOpeningData opening in layout.DoorOpenings)
            {
                BuildQuickDoor(layout, session, doorsParent, opening);
            }
        }

        private static bool TryLoadReadableSourceTexture(LevelLayoutData layout, out Texture2D texture)
        {
            texture = null;
            if (layout == null || string.IsNullOrWhiteSpace(layout.SourceMetadata.sourceImageAssetPath))
            {
                return false;
            }

            string assetPath = layout.SourceMetadata.sourceImageAssetPath.Replace("\\", "/");
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                return false;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            return texture != null;
        }

        private static void BuildWallPathsFromTexture(LevelLayoutData layout, Texture2D texture, ImageTraceProfile profile)
        {
            layout.Rooms.Clear();
            layout.Corridors.Clear();
            layout.WallPaths.Clear();
            layout.DoorOpenings.Clear();
            layout.FloorZones.Clear();
            layout.CeilingZones.Clear();

            ImageTraceProfile.ThresholdSettings threshold = profile != null
                ? profile.threshold
                : new ImageTraceProfile.ThresholdSettings();
            ImageTraceProfile.CleanupSettings cleanup = profile != null
                ? profile.cleanup
                : new ImageTraceProfile.CleanupSettings();
            ImageTraceProfile.SimplificationSettings simplification = profile != null
                ? profile.simplification
                : new ImageTraceProfile.SimplificationSettings();

            bool[,] mask = BuildMask(texture, threshold, layout.SourceMetadata.horizontalFlip, layout.SourceMetadata.verticalFlip);
            RemoveSmallIslands(mask, cleanup.noiseRemovalPixels);

            float worldWidth = Mathf.Max(1f, layout.SourceMetadata.levelSize.x);
            float worldHeight = Mathf.Max(1f, layout.SourceMetadata.levelSize.y);
            float minSegment = Mathf.Max(0.05f, cleanup.minimumSegmentLength);
            float tolerance = Mathf.Max(0.01f, simplification.pathTolerance);

            int pathIndex = 0;
            foreach (List<Vector2Int> contour in ExtractMaskContours(mask).OrderByDescending(candidate => candidate.Count))
            {
                List<Vector3> worldPoints = ConvertContourToWorldPoints(contour, texture.width, texture.height, worldWidth, worldHeight);
                List<Vector3> simplified = SimplifyClosedPath(worldPoints, tolerance);
                if (CalculatePolylineLength(simplified, true) < minSegment || simplified.Count < 3)
                {
                    continue;
                }

                WallPathData wallPath = new WallPathData
                {
                    displayName = $"WallPath_{++pathIndex:000}",
                    isClosed = true,
                    usage = WallPathUsage.Wall,
                    debugColor = new Color(0.92f, 0.72f, 0.52f, 1f)
                };
                wallPath.localPoints.AddRange(simplified);
                layout.WallPaths.Add(wallPath);
            }

            AddPerimeterFromTracedWallsIfNeeded(layout);
        }

        private static bool[,] BuildMask(Texture2D texture, ImageTraceProfile.ThresholdSettings settings, bool horizontalFlip, bool verticalFlip)
        {
            int width = texture.width;
            int height = texture.height;
            bool[,] mask = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sampleX = horizontalFlip ? (width - 1 - x) : x;
                    int sampleY = verticalFlip ? (height - 1 - y) : y;
                    Color pixel = texture.GetPixel(sampleX, sampleY);
                    float grayscale = pixel.grayscale;
                    bool isWall = grayscale <= settings.grayscaleThreshold;
                    mask[x, y] = settings.invertMask ? !isWall : isWall;
                }
            }

            return mask;
        }

        private static void RemoveSmallIslands(bool[,] mask, int minimumPixels)
        {
            if (minimumPixels <= 1)
            {
                return;
            }

            int width = mask.GetLength(0);
            int height = mask.GetLength(1);
            bool[,] visited = new bool[width, height];
            int[] offsetX = { 1, -1, 0, 0 };
            int[] offsetY = { 0, 0, 1, -1 };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x, y] || visited[x, y])
                    {
                        continue;
                    }

                    Queue<Vector2Int> frontier = new Queue<Vector2Int>();
                    List<Vector2Int> component = new List<Vector2Int>();
                    frontier.Enqueue(new Vector2Int(x, y));
                    visited[x, y] = true;

                    while (frontier.Count > 0)
                    {
                        Vector2Int current = frontier.Dequeue();
                        component.Add(current);

                        for (int index = 0; index < offsetX.Length; index++)
                        {
                            int nextX = current.x + offsetX[index];
                            int nextY = current.y + offsetY[index];
                            if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                            {
                                continue;
                            }

                            if (!mask[nextX, nextY] || visited[nextX, nextY])
                            {
                                continue;
                            }

                            visited[nextX, nextY] = true;
                            frontier.Enqueue(new Vector2Int(nextX, nextY));
                        }
                    }

                    if (component.Count >= minimumPixels)
                    {
                        continue;
                    }

                    foreach (Vector2Int pixel in component)
                    {
                        mask[pixel.x, pixel.y] = false;
                    }
                }
            }
        }

        private static List<List<Vector2Int>> ExtractMaskContours(bool[,] mask)
        {
            int width = mask.GetLength(0);
            int height = mask.GetLength(1);
            Dictionary<GridEdge, bool> edges = new Dictionary<GridEdge, bool>();
            Dictionary<Vector2Int, List<Vector2Int>> adjacency = new Dictionary<Vector2Int, List<Vector2Int>>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x, y])
                    {
                        continue;
                    }

                    if (IsMaskEmpty(mask, x, y - 1))
                    {
                        AddBoundaryEdge(new Vector2Int(x, y), new Vector2Int(x + 1, y), edges, adjacency);
                    }

                    if (IsMaskEmpty(mask, x + 1, y))
                    {
                        AddBoundaryEdge(new Vector2Int(x + 1, y), new Vector2Int(x + 1, y + 1), edges, adjacency);
                    }

                    if (IsMaskEmpty(mask, x, y + 1))
                    {
                        AddBoundaryEdge(new Vector2Int(x + 1, y + 1), new Vector2Int(x, y + 1), edges, adjacency);
                    }

                    if (IsMaskEmpty(mask, x - 1, y))
                    {
                        AddBoundaryEdge(new Vector2Int(x, y + 1), new Vector2Int(x, y), edges, adjacency);
                    }
                }
            }

            HashSet<GridEdge> visited = new HashSet<GridEdge>();
            List<List<Vector2Int>> contours = new List<List<Vector2Int>>();

            foreach (GridEdge edge in edges.Keys)
            {
                if (visited.Contains(edge))
                {
                    continue;
                }

                List<Vector2Int> contour = WalkContour(edge, adjacency, visited);
                if (contour.Count >= 3)
                {
                    contours.Add(contour);
                }
            }

            return contours;
        }

        private static void AddPerimeterFromTracedWallsIfNeeded(LevelLayoutData layout)
        {
            if (layout.WallPaths.Count == 0)
            {
                return;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            foreach (WallPathData wallPath in layout.WallPaths)
            {
                foreach (Vector3 point in wallPath.localPoints)
                {
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minZ = Mathf.Min(minZ, point.z);
                    maxZ = Mathf.Max(maxZ, point.z);
                }
            }

            bool touchesBoundary = minX <= 0.1f || minZ <= 0.1f
                || maxX >= layout.SourceMetadata.levelSize.x - 0.1f
                || maxZ >= layout.SourceMetadata.levelSize.y - 0.1f;
            if (touchesBoundary)
            {
                return;
            }

            float padding = 0.25f;
            AddRectanglePath(
                layout,
                $"WallPath_{layout.WallPaths.Count + 1:000}",
                Mathf.Max(0f, minX - padding),
                Mathf.Max(0f, minZ - padding),
                Mathf.Max(0.5f, (maxX - minX) + padding * 2f),
                Mathf.Max(0.5f, (maxZ - minZ) + padding * 2f),
                WallPathUsage.Perimeter,
                new Color(0.45f, 0.95f, 0.9f, 1f));
        }

        private static bool IsMaskEmpty(bool[,] mask, int x, int y)
        {
            return x < 0 || y < 0 || x >= mask.GetLength(0) || y >= mask.GetLength(1) || !mask[x, y];
        }

        private static void AddBoundaryEdge(
            Vector2Int from,
            Vector2Int to,
            IDictionary<GridEdge, bool> edges,
            IDictionary<Vector2Int, List<Vector2Int>> adjacency)
        {
            GridEdge edge = new GridEdge(from, to);
            if (edges.ContainsKey(edge))
            {
                return;
            }

            edges[edge] = true;

            AddAdjacent(adjacency, from, to);
            AddAdjacent(adjacency, to, from);
        }

        private static void AddAdjacent(IDictionary<Vector2Int, List<Vector2Int>> adjacency, Vector2Int from, Vector2Int to)
        {
            if (!adjacency.TryGetValue(from, out List<Vector2Int> neighbors))
            {
                neighbors = new List<Vector2Int>();
                adjacency[from] = neighbors;
            }

            if (!neighbors.Contains(to))
            {
                neighbors.Add(to);
            }
        }

        private static List<Vector2Int> WalkContour(
            GridEdge startEdge,
            IReadOnlyDictionary<Vector2Int, List<Vector2Int>> adjacency,
            ISet<GridEdge> visited)
        {
            List<Vector2Int> contour = new List<Vector2Int> { startEdge.from, startEdge.to };
            visited.Add(startEdge);

            Vector2Int previous = startEdge.from;
            Vector2Int current = startEdge.to;

            while (true)
            {
                if (!adjacency.TryGetValue(current, out List<Vector2Int> neighbors))
                {
                    break;
                }

                Vector2Int next = default;
                bool found = false;
                foreach (Vector2Int candidate in neighbors)
                {
                    GridEdge candidateEdge = new GridEdge(current, candidate);
                    if (candidate == previous || visited.Contains(candidateEdge))
                    {
                        continue;
                    }

                    next = candidate;
                    found = true;
                    break;
                }

                if (!found)
                {
                    break;
                }

                GridEdge nextEdge = new GridEdge(current, next);
                visited.Add(nextEdge);
                contour.Add(next);

                previous = current;
                current = next;

                if (current == contour[0])
                {
                    contour.RemoveAt(contour.Count - 1);
                    break;
                }
            }

            return contour;
        }

        private static List<Vector3> ConvertContourToWorldPoints(
            IReadOnlyList<Vector2Int> contour,
            int textureWidth,
            int textureHeight,
            float worldWidth,
            float worldHeight)
        {
            List<Vector3> points = new List<Vector3>(contour.Count);
            foreach (Vector2Int point in contour)
            {
                float x = point.x / (float)textureWidth * worldWidth;
                float z = point.y / (float)textureHeight * worldHeight;
                points.Add(new Vector3(x, 0f, z));
            }

            return RemoveCollinearPoints(points, true);
        }

        private static List<Vector3> SimplifyClosedPath(IReadOnlyList<Vector3> points, float tolerance)
        {
            if (points.Count <= 3)
            {
                return points.ToList();
            }

            List<Vector3> openPoints = new List<Vector3>(points);
            openPoints.Add(points[0]);
            List<Vector3> simplified = SimplifyOpenPath(openPoints, tolerance);
            if (simplified.Count > 1 && Vector3.Distance(simplified[0], simplified[^1]) <= 0.001f)
            {
                simplified.RemoveAt(simplified.Count - 1);
            }

            return RemoveCollinearPoints(simplified, true);
        }

        private static List<Vector3> SimplifyOpenPath(IReadOnlyList<Vector3> points, float tolerance)
        {
            if (points.Count <= 2)
            {
                return points.ToList();
            }

            float maxDistance = 0f;
            int index = 0;
            for (int pointIndex = 1; pointIndex < points.Count - 1; pointIndex++)
            {
                float distance = DistancePointToSegment(points[pointIndex], points[0], points[^1]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    index = pointIndex;
                }
            }

            if (maxDistance <= tolerance)
            {
                return new List<Vector3> { points[0], points[^1] };
            }

            List<Vector3> left = SimplifyOpenPath(points.Take(index + 1).ToList(), tolerance);
            List<Vector3> right = SimplifyOpenPath(points.Skip(index).ToList(), tolerance);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        private static List<Vector3> RemoveCollinearPoints(IReadOnlyList<Vector3> points, bool closed)
        {
            if (points.Count <= 2)
            {
                return points.ToList();
            }

            List<Vector3> result = new List<Vector3>();
            int count = points.Count;
            for (int index = 0; index < count; index++)
            {
                Vector3 previous = points[(index - 1 + count) % count];
                Vector3 current = points[index];
                Vector3 next = points[(index + 1) % count];

                if (!closed && (index == 0 || index == count - 1))
                {
                    result.Add(current);
                    continue;
                }

                Vector3 from = (current - previous).normalized;
                Vector3 to = (next - current).normalized;
                if (from == Vector3.zero || to == Vector3.zero || Mathf.Abs(Vector3.Cross(from, to).y) > 0.001f || Vector3.Dot(from, to) < 0.999f)
                {
                    result.Add(current);
                }
            }

            return result.Count >= (closed ? 3 : 2) ? result : points.ToList();
        }

        private static float CalculatePolylineLength(IReadOnlyList<Vector3> points, bool closed)
        {
            if (points.Count < 2)
            {
                return 0f;
            }

            float length = 0f;
            for (int index = 0; index < points.Count - 1; index++)
            {
                length += Vector3.Distance(points[index], points[index + 1]);
            }

            if (closed)
            {
                length += Vector3.Distance(points[^1], points[0]);
            }

            return length;
        }

        private static float DistancePointToSegment(Vector3 point, Vector3 from, Vector3 to)
        {
            Vector3 segment = to - from;
            float magnitude = segment.sqrMagnitude;
            if (magnitude <= Mathf.Epsilon)
            {
                return Vector3.Distance(point, from);
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - from, segment) / magnitude);
            Vector3 projected = from + segment * t;
            return Vector3.Distance(point, projected);
        }

        private static void ClearGeneratedChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                Undo.DestroyObjectImmediate(parent.GetChild(index).gameObject);
            }
        }

        private static void BuildQuickWallPath(UnifiedLevelDesignEditorSession session, Transform parent, WallPathData wallPath)
        {
            if (wallPath.localPoints == null || wallPath.localPoints.Count < 2)
            {
                return;
            }

            int segmentCount = wallPath.localPoints.Count - 1 + (wallPath.isClosed ? 1 : 0);
            for (int index = 0; index < segmentCount; index++)
            {
                Vector3 from = wallPath.localPoints[index];
                Vector3 to = wallPath.localPoints[(index + 1) % wallPath.localPoints.Count];
                BuildQuickWallSegment(session, parent, wallPath.displayName, from, to);
            }
        }

        private static void BuildQuickWallSegment(UnifiedLevelDesignEditorSession session, Transform parent, string wallName, Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude;
            if (length <= 0.01f)
            {
                return;
            }

            Vector3 forward = direction.normalized;
            Quaternion pathRotation = Quaternion.LookRotation(forward, Vector3.up);

            if (session.quickWallPrefab != null)
            {
                PrefabPlacementInfo prefabInfo = AnalyzePrefab(session.quickWallPrefab);
                float pieceLength = Mathf.Max(0.1f, prefabInfo.length);
                float desiredSpacing = Mathf.Max(0.1f, session.quickWallPieceSpacing);
                int pieceCount = Mathf.Max(1, Mathf.RoundToInt(length / Mathf.Max(pieceLength, desiredSpacing)));
                float spacing = length / pieceCount;
                for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
                {
                    float distance = (pieceIndex + 0.5f) * spacing;
                    Quaternion finalRotation = pathRotation * prefabInfo.rotationOffset;
                    Vector3 position = from + forward * distance;
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(session.quickWallPrefab);
                    Undo.RegisterCreatedObjectUndo(instance, "Create Quick Wall Piece");
                    Undo.SetTransformParent(instance.transform, parent, "Parent Quick Wall Piece");
                    instance.name = $"{wallName}_Piece_{pieceIndex + 1:000}";
                    instance.transform.localPosition = position + (finalRotation * prefabInfo.positionOffset);
                    instance.transform.localRotation = finalRotation;
                }
                return;
            }

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(cube, "Create Quick Wall Placeholder");
            Undo.SetTransformParent(cube.transform, parent, "Parent Quick Wall Placeholder");
            cube.name = $"{wallName}_Placeholder";
            cube.transform.SetPositionAndRotation((from + to) * 0.5f + Vector3.up * (session.quickWallHeight * 0.5f), pathRotation);
            cube.transform.localScale = new Vector3(session.quickWallThickness, session.quickWallHeight, length);
        }

        private static void BuildQuickDoor(LevelLayoutData layout, UnifiedLevelDesignEditorSession session, Transform parent, DoorOpeningData opening)
        {
            WallPathData wallPath = layout.WallPaths.Find(candidate => candidate.pathId == opening.wallPathId);
            if (wallPath == null || wallPath.localPoints == null || wallPath.localPoints.Count < 2)
            {
                return;
            }

            if (!TryEvaluatePositionAndRotationAlongPath(wallPath, opening.distanceAlongPath, out Vector3 position, out Quaternion rotation))
            {
                return;
            }

            if (session.quickDoorPrefab != null)
            {
                PrefabPlacementInfo prefabInfo = AnalyzePrefab(session.quickDoorPrefab);
                Quaternion finalRotation = rotation * prefabInfo.rotationOffset;
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(session.quickDoorPrefab);
                Undo.RegisterCreatedObjectUndo(instance, "Create Quick Door");
                Undo.SetTransformParent(instance.transform, parent, "Parent Quick Door");
                instance.name = opening.displayName;
                instance.transform.localPosition = position + (finalRotation * prefabInfo.positionOffset);
                instance.transform.localRotation = finalRotation;
                return;
            }

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(cube, "Create Quick Door Placeholder");
            Undo.SetTransformParent(cube.transform, parent, "Parent Quick Door Placeholder");
            cube.name = $"{opening.displayName}_Placeholder";
            cube.transform.SetPositionAndRotation(position, rotation);
            cube.transform.position += Vector3.up * (opening.height * 0.5f);
            cube.transform.localScale = new Vector3(session.quickWallThickness * 2f, opening.height, opening.width);
        }

        private static bool TryEvaluatePositionAndRotationAlongPath(WallPathData wallPath, float distanceAlongPath, out Vector3 position, out Quaternion rotation)
        {
            float traversed = 0f;
            int segmentCount = wallPath.localPoints.Count - 1 + (wallPath.isClosed ? 1 : 0);
            for (int index = 0; index < segmentCount; index++)
            {
                Vector3 from = wallPath.localPoints[index];
                Vector3 to = wallPath.localPoints[(index + 1) % wallPath.localPoints.Count];
                Vector3 direction = to - from;
                float segmentLength = direction.magnitude;
                if (segmentLength <= Mathf.Epsilon)
                {
                    continue;
                }

                if (traversed + segmentLength >= distanceAlongPath)
                {
                    float t = Mathf.InverseLerp(traversed, traversed + segmentLength, distanceAlongPath);
                    position = Vector3.Lerp(from, to, t);
                    rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    return true;
                }

                traversed += segmentLength;
            }

            Vector3 fallbackFrom = wallPath.localPoints[^2];
            Vector3 fallbackTo = wallPath.localPoints[^1];
            Vector3 fallbackDirection = (fallbackTo - fallbackFrom).normalized;
            position = fallbackTo;
            rotation = Quaternion.LookRotation(fallbackDirection == Vector3.zero ? Vector3.forward : fallbackDirection, Vector3.up);
            return true;
        }

        private static void GenerateRoomAndCorridorLayout(LevelLayoutData layout, UnifiedLevelDesignEditorSession session)
        {
            Random.State previousState = Random.state;
            Random.InitState(layout.SourceMetadata.generationSeed);

            try
            {
                session.quickRoomCount = Mathf.Max(1, session.quickRoomCount);
                session.quickCorridorWidth = Mathf.Max(0.25f, session.quickCorridorWidth);
                session.quickWallPieceSpacing = Mathf.Max(0.1f, session.quickWallPieceSpacing);
                session.quickWallHeight = Mathf.Max(0.1f, session.quickWallHeight);
                session.quickWallThickness = Mathf.Max(0.01f, session.quickWallThickness);
                session.quickRoomPadding = Mathf.Max(0f, session.quickRoomPadding);
                session.quickMinRoomSize = new Vector2(Mathf.Max(2f, session.quickMinRoomSize.x), Mathf.Max(2f, session.quickMinRoomSize.y));
                session.quickMaxRoomSize = new Vector2(
                    Mathf.Max(session.quickMinRoomSize.x, session.quickMaxRoomSize.x),
                    Mathf.Max(session.quickMinRoomSize.y, session.quickMaxRoomSize.y));

                layout.Rooms.Clear();
                layout.Corridors.Clear();
                layout.WallPaths.Clear();
                layout.DoorOpenings.Clear();
                layout.FloorZones.Clear();
                layout.CeilingZones.Clear();

                AddRectanglePath(layout, $"WallPath_{layout.WallPaths.Count + 1:000}", 0f, 0f, session.quickLayoutSize.x, session.quickLayoutSize.y, WallPathUsage.Perimeter, new Color(0.45f, 0.95f, 0.9f, 1f));

                List<Rect> placedRooms = new List<Rect>();
                int attempts = session.quickRoomCount * 10;
                while (placedRooms.Count < session.quickRoomCount && attempts-- > 0)
                {
                    float width = Random.Range(session.quickMinRoomSize.x, session.quickMaxRoomSize.x);
                    float length = Random.Range(session.quickMinRoomSize.y, session.quickMaxRoomSize.y);
                    float x = Random.Range(1f, Mathf.Max(2f, session.quickLayoutSize.x - width - 1f));
                    float z = Random.Range(1f, Mathf.Max(2f, session.quickLayoutSize.y - length - 1f));
                    Rect candidate = new Rect(x, z, width, length);

                    bool overlaps = false;
                    foreach (Rect existing in placedRooms)
                    {
                        Rect padded = new Rect(
                            existing.xMin - session.quickRoomPadding,
                            existing.yMin - session.quickRoomPadding,
                            existing.width + session.quickRoomPadding * 2f,
                            existing.height + session.quickRoomPadding * 2f);
                        if (padded.Overlaps(candidate))
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps)
                    {
                        placedRooms.Add(candidate);
                    }
                }

                placedRooms.Sort((left, right) => left.center.x.CompareTo(right.center.x));

                for (int roomIndex = 0; roomIndex < placedRooms.Count; roomIndex++)
                {
                    Rect room = placedRooms[roomIndex];
                    layout.Rooms.Add(new RoomData
                    {
                        displayName = $"Room_{roomIndex + 1:000}",
                        bounds = room,
                        isLandmark = roomIndex == placedRooms.Count / 2
                    });

                    AddRectanglePath(layout, $"WallPath_{layout.WallPaths.Count + 1:000}", room.xMin, room.yMin, room.width, room.height);
                }

                if (!session.quickGenerateCorridors)
                {
                    return;
                }

                for (int roomIndex = 0; roomIndex < placedRooms.Count - 1; roomIndex++)
                {
                    Rect fromRoom = placedRooms[roomIndex];
                    Rect toRoom = placedRooms[roomIndex + 1];
                    bool horizontalFirst = Mathf.Abs(toRoom.center.x - fromRoom.center.x) >= Mathf.Abs(toRoom.center.y - fromRoom.center.y);
                    Vector3 start = GetCorridorConnectionPoint(fromRoom, toRoom.center, horizontalFirst);
                    Vector3 end = GetCorridorConnectionPoint(toRoom, fromRoom.center, horizontalFirst);
                    Vector3 corner = horizontalFirst
                        ? new Vector3(end.x, 0f, start.z)
                        : new Vector3(start.x, 0f, end.z);

                    layout.Corridors.Add(new CorridorData
                    {
                        displayName = $"Corridor_{roomIndex + 1:000}",
                        width = session.quickCorridorWidth,
                        localPoints = { start, corner, end }
                    });

                    AddCorridorWalls(layout, start, corner, session.quickCorridorWidth);
                    AddCorridorWalls(layout, corner, end, session.quickCorridorWidth);
                    AddDoorAtRoomConnection(layout, fromRoom, start);
                    AddDoorAtRoomConnection(layout, toRoom, end);
                }
            }
            finally
            {
                Random.state = previousState;
            }
        }

        private static Vector3 GetCorridorConnectionPoint(Rect room, Vector2 targetCenter, bool horizontalFirst)
        {
            const float inset = 1f;

            if (horizontalFirst)
            {
                bool connectRight = targetCenter.x >= room.center.x;
                float x = connectRight ? room.xMax : room.xMin;
                float z = Mathf.Clamp(targetCenter.y, room.yMin + inset, room.yMax - inset);
                return new Vector3(x, 0f, z);
            }

            bool connectTop = targetCenter.y >= room.center.y;
            float zEdge = connectTop ? room.yMax : room.yMin;
            float clampedX = Mathf.Clamp(targetCenter.x, room.xMin + inset, room.xMax - inset);
            return new Vector3(clampedX, 0f, zEdge);
        }

        private static void AddCorridorWalls(LevelLayoutData layout, Vector3 start, Vector3 end, float width)
        {
            Vector3 direction = (end - start).normalized;
            if (direction == Vector3.zero)
            {
                return;
            }

            Vector3 perpendicular = Vector3.Cross(Vector3.up, direction) * (width * 0.5f);
            AddPolylinePath(layout, $"WallPath_{layout.WallPaths.Count + 1:000}", new Color(0.85f, 0.65f, 0.35f, 1f), start + perpendicular, end + perpendicular);
            AddPolylinePath(layout, $"WallPath_{layout.WallPaths.Count + 1:000}", new Color(0.85f, 0.65f, 0.35f, 1f), start - perpendicular, end - perpendicular);
        }

        private static void AddDoorAtRoomConnection(LevelLayoutData layout, Rect room, Vector3 connectionPoint)
        {
            Vector2 center = room.center;
            float distanceToLeft = Mathf.Abs(connectionPoint.x - room.xMin);
            float distanceToRight = Mathf.Abs(connectionPoint.x - room.xMax);
            float distanceToBottom = Mathf.Abs(connectionPoint.z - room.yMin);
            float distanceToTop = Mathf.Abs(connectionPoint.z - room.yMax);

            float minDistance = Mathf.Min(distanceToLeft, distanceToRight, distanceToBottom, distanceToTop);
            Vector3 doorPoint;

            if (Mathf.Approximately(minDistance, distanceToLeft))
            {
                doorPoint = new Vector3(room.xMin, 0f, Mathf.Clamp(connectionPoint.z, room.yMin + 1f, room.yMax - 1f));
            }
            else if (Mathf.Approximately(minDistance, distanceToRight))
            {
                doorPoint = new Vector3(room.xMax, 0f, Mathf.Clamp(connectionPoint.z, room.yMin + 1f, room.yMax - 1f));
            }
            else if (Mathf.Approximately(minDistance, distanceToBottom))
            {
                doorPoint = new Vector3(Mathf.Clamp(connectionPoint.x, room.xMin + 1f, room.xMax - 1f), 0f, room.yMin);
            }
            else
            {
                doorPoint = new Vector3(Mathf.Clamp(connectionPoint.x, room.xMin + 1f, room.xMax - 1f), 0f, room.yMax);
            }

            WallPathData wallPath = FindClosestWallPath(layout, doorPoint);
            if (wallPath == null)
            {
                return;
            }

            float distanceAlongPath = CalculateDistanceAlongPath(wallPath, doorPoint);
            layout.DoorOpenings.Add(new DoorOpeningData
            {
                displayName = $"Door_{layout.DoorOpenings.Count + 1:000}",
                wallPathId = wallPath.pathId,
                distanceAlongPath = distanceAlongPath,
                width = 1.8f,
                height = 2.2f,
                doorType = "Standard"
            });
        }

        private static WallPathData FindClosestWallPath(LevelLayoutData layout, Vector3 point)
        {
            WallPathData closest = null;
            float closestDistance = float.MaxValue;

            foreach (WallPathData wallPath in layout.WallPaths)
            {
                if (!wallPath.isClosed || wallPath.localPoints.Count < 2)
                {
                    continue;
                }

                for (int index = 0; index < wallPath.localPoints.Count; index++)
                {
                    Vector3 from = wallPath.localPoints[index];
                    Vector3 to = wallPath.localPoints[(index + 1) % wallPath.localPoints.Count];
                    float distance = DistanceToSegment(point, from, to);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closest = wallPath;
                    }
                }
            }

            return closest;
        }

        private static float CalculateDistanceAlongPath(WallPathData wallPath, Vector3 targetPoint)
        {
            float traversed = 0f;
            float bestDistance = float.MaxValue;
            float bestAlongPath = 0f;
            int segmentCount = wallPath.localPoints.Count - 1 + (wallPath.isClosed ? 1 : 0);

            for (int index = 0; index < segmentCount; index++)
            {
                Vector3 from = wallPath.localPoints[index];
                Vector3 to = wallPath.localPoints[(index + 1) % wallPath.localPoints.Count];
                Vector3 segment = to - from;
                float length = segment.magnitude;
                if (length <= Mathf.Epsilon)
                {
                    continue;
                }

                float t = Mathf.Clamp01(Vector3.Dot(targetPoint - from, segment) / Vector3.Dot(segment, segment));
                Vector3 projected = from + segment * t;
                float distance = Vector3.Distance(projected, targetPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestAlongPath = traversed + length * t;
                }

                traversed += length;
            }

            return bestAlongPath;
        }

        private static float DistanceToSegment(Vector3 point, Vector3 from, Vector3 to)
        {
            Vector3 segment = to - from;
            float magnitude = segment.sqrMagnitude;
            if (magnitude <= Mathf.Epsilon)
            {
                return Vector3.Distance(point, from);
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - from, segment) / magnitude);
            Vector3 projected = from + segment * t;
            return Vector3.Distance(point, projected);
        }

        private static GeneratedLevelMarker FindGeneratedRootForLayout(LevelLayoutData layout)
        {
            GeneratedLevelMarker[] markers = Object.FindObjectsByType<GeneratedLevelMarker>(FindObjectsSortMode.None);
            foreach (GeneratedLevelMarker marker in markers)
            {
                if (marker != null && marker.LayoutData == layout)
                {
                    return marker;
                }
            }

            return null;
        }

        private static void PopulateExampleLayout(LevelLayoutData layout)
        {
            layout.LayoutName = "ArtTestScene_ProceduralToolkitExample";
            layout.State = GeneratedLayoutState.Preview;
            layout.ResetIdsIfMissing();
            layout.SourceMetadata.sourceMode = LayoutSourceMode.Procedural;
            layout.SourceMetadata.levelSize = new Vector2(44f, 34f);
            layout.SourceMetadata.cellSize = 2f;
            layout.SourceMetadata.generationSeed = 20260715;
            layout.SourceMetadata.worldOrigin = new Vector3(-22f, 0.05f, -17f);
            layout.SourceMetadata.worldEulerRotation = Vector3.zero;
            layout.SourceMetadata.notes = "Phase 1 sample generated through the Unified Level Design Toolkit window.";

            layout.Rooms.Clear();
            layout.Corridors.Clear();
            layout.WallPaths.Clear();
            layout.DoorOpenings.Clear();
            layout.FloorZones.Clear();
            layout.CeilingZones.Clear();

            layout.Rooms.Add(new RoomData { displayName = "Room_001", bounds = new Rect(2f, 2f, 8f, 7f) });
            layout.Rooms.Add(new RoomData { displayName = "Room_002", bounds = new Rect(14f, 3f, 10f, 6f) });
            layout.Rooms.Add(new RoomData { displayName = "Room_003", bounds = new Rect(28f, 2f, 10f, 8f), isLandmark = true });
            layout.Rooms.Add(new RoomData { displayName = "Room_004", bounds = new Rect(5f, 15f, 9f, 7f) });
            layout.Rooms.Add(new RoomData { displayName = "Room_005", bounds = new Rect(19f, 15f, 8f, 8f) });
            layout.Rooms.Add(new RoomData { displayName = "Room_006", bounds = new Rect(31f, 16f, 8f, 7f) });
            layout.Rooms.Add(new RoomData { displayName = "Room_007", bounds = new Rect(12f, 26f, 10f, 5f) });

            layout.Corridors.Add(new CorridorData
            {
                displayName = "Corridor_001",
                width = 2f,
                localPoints = { new Vector3(10f, 0f, 5.5f), new Vector3(14f, 0f, 5.5f), new Vector3(14f, 0f, 19f), new Vector3(19f, 0f, 19f) }
            });
            layout.Corridors.Add(new CorridorData
            {
                displayName = "Corridor_002",
                width = 2f,
                localPoints = { new Vector3(24f, 0f, 6f), new Vector3(28f, 0f, 6f), new Vector3(28f, 0f, 19.5f), new Vector3(31f, 0f, 19.5f) }
            });

            AddRectanglePath(layout, "WallPath_001", 2f, 2f, 8f, 7f);
            AddRectanglePath(layout, "WallPath_002", 14f, 3f, 10f, 6f);
            AddRectanglePath(layout, "WallPath_003", 28f, 2f, 10f, 8f);
            AddRectanglePath(layout, "WallPath_004", 5f, 15f, 9f, 7f);
            AddRectanglePath(layout, "WallPath_005", 19f, 15f, 8f, 8f);
            AddRectanglePath(layout, "WallPath_006", 31f, 16f, 8f, 7f);
            AddRectanglePath(layout, "WallPath_007", 12f, 26f, 10f, 5f);

            AddPolylinePath(layout, "WallPath_008", new Color(0.85f, 0.65f, 0.35f, 1f),
                new Vector3(10f, 0f, 4.5f), new Vector3(14f, 0f, 4.5f), new Vector3(14f, 0f, 18f), new Vector3(19f, 0f, 18f));
            AddPolylinePath(layout, "WallPath_009", new Color(0.85f, 0.65f, 0.35f, 1f),
                new Vector3(10f, 0f, 6.5f), new Vector3(13f, 0f, 6.5f), new Vector3(13f, 0f, 20f), new Vector3(19f, 0f, 20f));
            AddPolylinePath(layout, "WallPath_010", new Color(0.85f, 0.65f, 0.35f, 1f),
                new Vector3(24f, 0f, 5f), new Vector3(29f, 0f, 5f), new Vector3(29f, 0f, 18.5f), new Vector3(31f, 0f, 18.5f));
            AddPolylinePath(layout, "WallPath_011", new Color(0.85f, 0.65f, 0.35f, 1f),
                new Vector3(24f, 0f, 7f), new Vector3(27f, 0f, 7f), new Vector3(27f, 0f, 20.5f), new Vector3(31f, 0f, 20.5f));
            AddPolylinePath(layout, "WallPath_012", new Color(0.76f, 0.52f, 0.42f, 1f),
                new Vector3(18f, 0f, 23f), new Vector3(18f, 0f, 26f), new Vector3(24f, 0f, 26f), new Vector3(24f, 0f, 29f));

            AddDoor(layout, "Door_001", "WallPath_001", 4f);
            AddDoor(layout, "Door_002", "WallPath_002", 5f);
            AddDoor(layout, "Door_003", "WallPath_003", 5f);
            AddDoor(layout, "Door_004", "WallPath_004", 4f);
            AddDoor(layout, "Door_005", "WallPath_005", 4f);
            AddDoor(layout, "Door_006", "WallPath_006", 4f);
            AddDoor(layout, "Door_007", "WallPath_007", 5f);
        }

        private static void AddRectanglePath(LevelLayoutData layout, string name, float x, float z, float width, float length, WallPathUsage usage = WallPathUsage.Wall, Color? debugColor = null)
        {
            WallPathData wallPath = new WallPathData
            {
                displayName = name,
                isClosed = true,
                usage = usage,
                debugColor = debugColor ?? new Color(0.92f, 0.72f, 0.52f, 1f),
                localPoints =
                {
                    new Vector3(x, 0f, z),
                    new Vector3(x + width, 0f, z),
                    new Vector3(x + width, 0f, z + length),
                    new Vector3(x, 0f, z + length)
                }
            };

            layout.WallPaths.Add(wallPath);
        }

        private static void AddPolylinePath(LevelLayoutData layout, string name, Color color, params Vector3[] points)
        {
            WallPathData wallPath = new WallPathData
            {
                displayName = name,
                isClosed = false,
                debugColor = color
            };

            wallPath.localPoints.AddRange(points);
            layout.WallPaths.Add(wallPath);
        }

        private static void AddDoor(LevelLayoutData layout, string name, string wallPathName, float distanceAlongPath)
        {
            WallPathData wallPath = layout.WallPaths.Find(candidate => candidate.displayName == wallPathName);
            if (wallPath == null)
            {
                return;
            }

            layout.DoorOpenings.Add(new DoorOpeningData
            {
                displayName = name,
                wallPathId = wallPath.pathId,
                distanceAlongPath = distanceAlongPath,
                width = 1.8f,
                height = 2.2f,
                doorType = "Sliding"
            });
        }

        private static PrefabPlacementInfo AnalyzePrefab(GameObject prefab)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                return PrefabPlacementInfo.Default;
            }

            try
            {
                Bounds localBounds = CalculateLocalBounds(instance.transform);
                Vector3 size = localBounds.size;
                bool lengthIsX = size.x >= size.z;

                return new PrefabPlacementInfo
                {
                    length = lengthIsX ? size.x : size.z,
                    rotationOffset = lengthIsX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity,
                    positionOffset = new Vector3(0f, -localBounds.min.y, 0f)
                };
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            bool initialized = false;
            Bounds combinedBounds = default;
            Matrix4x4 worldToLocal = root.worldToLocalMatrix;

            foreach (Renderer renderer in renderers)
            {
                Bounds worldBounds = renderer.bounds;
                Vector3 extents = worldBounds.extents;
                Vector3 center = worldBounds.center;

                Vector3[] corners =
                {
                    center + new Vector3(-extents.x, -extents.y, -extents.z),
                    center + new Vector3(-extents.x, -extents.y, extents.z),
                    center + new Vector3(-extents.x, extents.y, -extents.z),
                    center + new Vector3(-extents.x, extents.y, extents.z),
                    center + new Vector3(extents.x, -extents.y, -extents.z),
                    center + new Vector3(extents.x, -extents.y, extents.z),
                    center + new Vector3(extents.x, extents.y, -extents.z),
                    center + new Vector3(extents.x, extents.y, extents.z)
                };

                foreach (Vector3 corner in corners)
                {
                    Vector3 localCorner = worldToLocal.MultiplyPoint3x4(corner);
                    if (!initialized)
                    {
                        combinedBounds = new Bounds(localCorner, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(localCorner);
                    }
                }
            }

            return combinedBounds;
        }

        private struct PrefabPlacementInfo
        {
            internal static readonly PrefabPlacementInfo Default = new PrefabPlacementInfo
            {
                length = 2f,
                rotationOffset = Quaternion.identity,
                positionOffset = Vector3.zero
            };

            internal float length;
            internal Quaternion rotationOffset;
            internal Vector3 positionOffset;
        }

        private readonly struct GridEdge
        {
            internal readonly Vector2Int from;
            internal readonly Vector2Int to;

            internal GridEdge(Vector2Int first, Vector2Int second)
            {
                if (first.x < second.x || (first.x == second.x && first.y <= second.y))
                {
                    from = first;
                    to = second;
                }
                else
                {
                    from = second;
                    to = first;
                }
            }
        }
    }
}
