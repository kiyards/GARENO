using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public sealed class LightingPostProcessingImplementor : EditorWindow
{
    private const string DefaultSourceScenePath = "Assets/Scenes/ArtTestScene.unity";
    private const string WindowTitle = "Lighting Implementor";

    [SerializeField] private SceneAsset sourceSceneAsset;
    [SerializeField] private bool applyToAllCameras;
    [SerializeField] private bool saveScenesAfterApply = true;

    private Vector2 scrollPosition;

    [MenuItem("Elenroth Tools/Lighting/Post Processing Implementer")]
    public static void OpenWindow()
    {
        GetWindow<LightingPostProcessingImplementor>(WindowTitle);
    }

    private void OnEnable()
    {
        if (sourceSceneAsset == null)
        {
            sourceSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(DefaultSourceScenePath);
        }
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        sourceSceneAsset = (SceneAsset)EditorGUILayout.ObjectField(
            "Art Reference Scene",
            sourceSceneAsset,
            typeof(SceneAsset),
            false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Apply Options", EditorStyles.boldLabel);
        applyToAllCameras = EditorGUILayout.ToggleLeft("Apply camera settings to every camera in the target scene", applyToAllCameras);
        saveScenesAfterApply = EditorGUILayout.ToggleLeft("Save active scene after applying", saveScenesAfterApply);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(sourceSceneAsset == null))
        {
            if (GUILayout.Button("Apply To Active Scene", GUILayout.Height(32)))
            {
                ApplyToActiveScene();
            }

            if (GUILayout.Button("Apply To Selected Scene Assets", GUILayout.Height(32)))
            {
                ApplyToSelectedSceneAssets();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Copies RenderSettings, replaces the target scene's Directional Light and Global Volume from the source scene, and copies Camera plus URP post-processing settings onto the target camera. Selected scene assets are saved automatically after applying.",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void ApplyToActiveScene()
    {
        string sourceScenePath = GetSourceScenePath();
        if (string.IsNullOrEmpty(sourceScenePath))
        {
            return;
        }

        Scene targetScene = SceneManager.GetActiveScene();
        if (!ValidateTargetScene(targetScene, sourceScenePath))
        {
            return;
        }

        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene sourceScene = default;
        bool openedSourceScene = false;

        try
        {
            sourceScene = GetOrOpenSourceScene(sourceScenePath, out openedSourceScene);
            SourceLightingData sourceData = CaptureSourceLightingData(sourceScene);
            ApplySourceToScene(sourceData, targetScene, saveScenesAfterApply);
            Debug.Log($"Applied ArtTestScene lighting and post-processing to {targetScene.path}.");
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }

            if (openedSourceScene && sourceScene.IsValid() && sourceScene.isLoaded)
            {
                EditorSceneManager.CloseScene(sourceScene, true);
            }
        }
    }

    private void ApplyToSelectedSceneAssets()
    {
        string sourceScenePath = GetSourceScenePath();
        if (string.IsNullOrEmpty(sourceScenePath))
        {
            return;
        }

        string[] targetScenePaths = Selection.GetFiltered<SceneAsset>(SelectionMode.Assets)
            .Select(AssetDatabase.GetAssetPath)
            .Where(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, sourceScenePath, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToArray();

        if (targetScenePaths.Length == 0)
        {
            EditorUtility.DisplayDialog(
                WindowTitle,
                "Select one or more scene assets in the Project window, then run this again.",
                "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
        int appliedCount = 0;

        try
        {
            foreach (string targetScenePath in targetScenePaths)
            {
                Scene targetScene = EditorSceneManager.OpenScene(targetScenePath, OpenSceneMode.Single);
                Scene sourceScene = EditorSceneManager.OpenScene(sourceScenePath, OpenSceneMode.Additive);

                try
                {
                    SourceLightingData sourceData = CaptureSourceLightingData(sourceScene);
                    ApplySourceToScene(sourceData, targetScene, true);
                    appliedCount++;
                }
                finally
                {
                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(sourceScene, true);
                    }
                }
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
        }

        Debug.Log($"Applied ArtTestScene lighting and post-processing to {appliedCount} selected scene(s).");
    }

    private string GetSourceScenePath()
    {
        string sourceScenePath = AssetDatabase.GetAssetPath(sourceSceneAsset);
        if (!string.IsNullOrEmpty(sourceScenePath) && sourceScenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
        {
            return sourceScenePath;
        }

        EditorUtility.DisplayDialog(WindowTitle, "Assign a valid source scene asset first.", "OK");
        return string.Empty;
    }

    private static bool ValidateTargetScene(Scene targetScene, string sourceScenePath)
    {
        if (!targetScene.IsValid() || !targetScene.isLoaded)
        {
            EditorUtility.DisplayDialog(WindowTitle, "Open a target scene before applying lighting.", "OK");
            return false;
        }

        if (string.IsNullOrEmpty(targetScene.path))
        {
            EditorUtility.DisplayDialog(WindowTitle, "Save the target scene before applying lighting.", "OK");
            return false;
        }

        if (string.Equals(targetScene.path, sourceScenePath, StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(WindowTitle, "Choose a target scene other than the source ArtTestScene.", "OK");
            return false;
        }

        return true;
    }

    private static Scene GetOrOpenSourceScene(string sourceScenePath, out bool openedSourceScene)
    {
        Scene sourceScene = SceneManager.GetSceneByPath(sourceScenePath);
        if (sourceScene.IsValid() && sourceScene.isLoaded)
        {
            openedSourceScene = false;
            return sourceScene;
        }

        openedSourceScene = true;
        return EditorSceneManager.OpenScene(sourceScenePath, OpenSceneMode.Additive);
    }

    private SourceLightingData CaptureSourceLightingData(Scene sourceScene)
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(sourceScene);

        try
        {
            GameObject directionalLightObject = FindNamedRoot(sourceScene, "Directional Light")
                ?? FindComponentsInScene<Light>(sourceScene).FirstOrDefault(light => light.type == LightType.Directional)?.gameObject;

            GameObject globalVolumeObject = FindNamedRoot(sourceScene, "Global Volume")
                ?? FindComponentsInScene<Volume>(sourceScene).FirstOrDefault(volume => volume.isGlobal)?.gameObject;

            Camera sourceCamera = FindPreferredCamera(sourceScene);
            UniversalAdditionalCameraData sourceCameraData = sourceCamera != null
                ? sourceCamera.GetComponent<UniversalAdditionalCameraData>()
                : null;

            if (directionalLightObject == null)
            {
                throw new InvalidOperationException(BuildMissingSourceObjectMessage(sourceScene, "Directional Light"));
            }

            if (globalVolumeObject == null)
            {
                throw new InvalidOperationException(BuildMissingSourceObjectMessage(sourceScene, "Global Volume"));
            }

            if (sourceCamera == null)
            {
                throw new InvalidOperationException(BuildMissingSourceObjectMessage(sourceScene, "Camera"));
            }

            return new SourceLightingData(
                RenderSettingsSnapshot.Capture(),
                directionalLightObject,
                globalVolumeObject,
                sourceCamera,
                sourceCameraData);
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
        }
    }

    private void ApplySourceToScene(SourceLightingData sourceData, Scene targetScene, bool saveScene)
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(targetScene);

        try
        {
            GameObject targetDirectionalLight = ReplaceSceneObject(
                sourceData.DirectionalLightObject,
                targetScene,
                FindTargetDirectionalLight(targetScene, sourceData.DirectionalLightObject.name));

            ReplaceSceneObject(
                sourceData.GlobalVolumeObject,
                targetScene,
                FindTargetGlobalVolume(targetScene, sourceData.GlobalVolumeObject.name));

            Light mappedSun = MapSourceSunToTarget(sourceData.RenderSettings.Sun, sourceData.DirectionalLightObject, targetDirectionalLight, targetScene);
            sourceData.RenderSettings.Apply(mappedSun);

            ApplyCameraSettings(sourceData, targetScene);

            EditorSceneManager.MarkSceneDirty(targetScene);
            if (saveScene)
            {
                EditorSceneManager.SaveScene(targetScene);
            }
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(WindowTitle, exception.Message, "OK");
            throw;
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
        }
    }

    private void ApplyCameraSettings(SourceLightingData sourceData, Scene targetScene)
    {
        List<Camera> targetCameras = FindTargetCameras(targetScene);
        if (targetCameras.Count == 0)
        {
            GameObject cameraClone = UnityEngine.Object.Instantiate(sourceData.Camera.gameObject);
            cameraClone.name = sourceData.Camera.gameObject.name;
            SceneManager.MoveGameObjectToScene(cameraClone, targetScene);
            return;
        }

        foreach (Camera targetCamera in targetCameras)
        {
            Undo.RecordObject(targetCamera, "Apply ArtTestScene Camera Settings");
            EditorUtility.CopySerialized(sourceData.Camera, targetCamera);

            if (sourceData.CameraData == null)
            {
                continue;
            }

            UniversalAdditionalCameraData targetCameraData = targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (targetCameraData == null)
            {
                targetCameraData = Undo.AddComponent<UniversalAdditionalCameraData>(targetCamera.gameObject);
            }

            Undo.RecordObject(targetCameraData, "Apply ArtTestScene URP Camera Settings");
            EditorUtility.CopySerialized(sourceData.CameraData, targetCameraData);
        }
    }

    private List<Camera> FindTargetCameras(Scene scene)
    {
        List<Camera> cameras = FindComponentsInScene<Camera>(scene);
        if (applyToAllCameras)
        {
            return cameras;
        }

        Camera mainCamera = cameras.FirstOrDefault(camera => camera.CompareTag("MainCamera"));
        if (mainCamera != null)
        {
            return new List<Camera> { mainCamera };
        }

        Camera enabledCamera = cameras.FirstOrDefault(camera => camera.enabled);
        return enabledCamera != null ? new List<Camera> { enabledCamera } : new List<Camera>();
    }

    private static GameObject ReplaceSceneObject(GameObject sourceObject, Scene targetScene, GameObject existingObject)
    {
        if (existingObject != null)
        {
            Undo.DestroyObjectImmediate(existingObject);
        }

        GameObject clone = UnityEngine.Object.Instantiate(sourceObject);
        clone.name = sourceObject.name;
        SceneManager.MoveGameObjectToScene(clone, targetScene);
        Undo.RegisterCreatedObjectUndo(clone, $"Create {sourceObject.name}");
        return clone;
    }

    private static GameObject FindTargetDirectionalLight(Scene targetScene, string sourceName)
    {
        return FindNamedRoot(targetScene, sourceName)
            ?? FindComponentsInScene<Light>(targetScene).FirstOrDefault(light => light.type == LightType.Directional)?.gameObject;
    }

    private static GameObject FindTargetGlobalVolume(Scene targetScene, string sourceName)
    {
        return FindNamedRoot(targetScene, sourceName)
            ?? FindComponentsInScene<Volume>(targetScene).FirstOrDefault(volume => volume.isGlobal)?.gameObject;
    }

    private static Camera FindPreferredCamera(Scene scene)
    {
        List<Camera> cameras = FindComponentsInScene<Camera>(scene);
        return cameras.FirstOrDefault(camera => camera.CompareTag("MainCamera"))
            ?? cameras.FirstOrDefault(camera => camera.enabled)
            ?? cameras.FirstOrDefault();
    }

    private static GameObject FindNamedRoot(Scene scene, string rootName)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == rootName);
    }

    private static List<T> FindComponentsInScene<T>(Scene scene) where T : Component
    {
        List<T> components = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            root.GetComponentsInChildren(true, components);
        }

        foreach (T component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component == null || component.gameObject.scene != scene || EditorUtility.IsPersistent(component))
            {
                continue;
            }

            if (!components.Contains(component))
            {
                components.Add(component);
            }
        }

        return components;
    }

    private static string BuildMissingSourceObjectMessage(Scene sourceScene, string objectTypeName)
    {
        string rootNames = string.Join(", ", sourceScene.GetRootGameObjects().Select(root => root.name));
        if (string.IsNullOrEmpty(rootNames))
        {
            rootNames = "none";
        }

        return $"The source scene does not contain a {objectTypeName}. Source: {sourceScene.path}. Root objects found: {rootNames}.";
    }

    private static Light MapSourceSunToTarget(Light sourceSun, GameObject sourceDirectionalLight, GameObject targetDirectionalLight, Scene targetScene)
    {
        if (sourceSun == null)
        {
            return null;
        }

        if (sourceSun.gameObject == sourceDirectionalLight)
        {
            return targetDirectionalLight.GetComponent<Light>();
        }

        return FindComponentsInScene<Light>(targetScene).FirstOrDefault(light => light.name == sourceSun.name);
    }

    private sealed class SourceLightingData
    {
        public SourceLightingData(
            RenderSettingsSnapshot renderSettings,
            GameObject directionalLightObject,
            GameObject globalVolumeObject,
            Camera camera,
            UniversalAdditionalCameraData cameraData)
        {
            RenderSettings = renderSettings;
            DirectionalLightObject = directionalLightObject;
            GlobalVolumeObject = globalVolumeObject;
            Camera = camera;
            CameraData = cameraData;
        }

        public RenderSettingsSnapshot RenderSettings { get; }
        public GameObject DirectionalLightObject { get; }
        public GameObject GlobalVolumeObject { get; }
        public Camera Camera { get; }
        public UniversalAdditionalCameraData CameraData { get; }
    }

    private sealed class RenderSettingsSnapshot
    {
        private readonly bool fog;
        private readonly Color fogColor;
        private readonly FogMode fogMode;
        private readonly float fogDensity;
        private readonly float fogStartDistance;
        private readonly float fogEndDistance;
        private readonly Color ambientSkyColor;
        private readonly Color ambientEquatorColor;
        private readonly Color ambientGroundColor;
        private readonly float ambientIntensity;
        private readonly AmbientMode ambientMode;
        private readonly Color subtractiveShadowColor;
        private readonly Material skybox;
        private readonly float haloStrength;
        private readonly float flareStrength;
        private readonly float flareFadeSpeed;
        private readonly DefaultReflectionMode defaultReflectionMode;
        private readonly int defaultReflectionResolution;
        private readonly int reflectionBounces;
        private readonly float reflectionIntensity;
        private readonly Texture customReflectionTexture;
        private readonly SphericalHarmonicsL2 ambientProbe;

        private RenderSettingsSnapshot()
        {
            fog = RenderSettings.fog;
            fogColor = RenderSettings.fogColor;
            fogMode = RenderSettings.fogMode;
            fogDensity = RenderSettings.fogDensity;
            fogStartDistance = RenderSettings.fogStartDistance;
            fogEndDistance = RenderSettings.fogEndDistance;
            ambientSkyColor = RenderSettings.ambientSkyColor;
            ambientEquatorColor = RenderSettings.ambientEquatorColor;
            ambientGroundColor = RenderSettings.ambientGroundColor;
            ambientIntensity = RenderSettings.ambientIntensity;
            ambientMode = RenderSettings.ambientMode;
            subtractiveShadowColor = RenderSettings.subtractiveShadowColor;
            skybox = RenderSettings.skybox;
            haloStrength = RenderSettings.haloStrength;
            flareStrength = RenderSettings.flareStrength;
            flareFadeSpeed = RenderSettings.flareFadeSpeed;
            defaultReflectionMode = RenderSettings.defaultReflectionMode;
            defaultReflectionResolution = RenderSettings.defaultReflectionResolution;
            reflectionBounces = RenderSettings.reflectionBounces;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            customReflectionTexture = RenderSettings.customReflectionTexture;
            ambientProbe = RenderSettings.ambientProbe;
            Sun = RenderSettings.sun;
        }

        public Light Sun { get; }

        public static RenderSettingsSnapshot Capture()
        {
            return new RenderSettingsSnapshot();
        }

        public void Apply(Light mappedSun)
        {
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStartDistance;
            RenderSettings.fogEndDistance = fogEndDistance;
            RenderSettings.ambientSkyColor = ambientSkyColor;
            RenderSettings.ambientEquatorColor = ambientEquatorColor;
            RenderSettings.ambientGroundColor = ambientGroundColor;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.subtractiveShadowColor = subtractiveShadowColor;
            RenderSettings.skybox = skybox;
            RenderSettings.haloStrength = haloStrength;
            RenderSettings.flareStrength = flareStrength;
            RenderSettings.flareFadeSpeed = flareFadeSpeed;
            RenderSettings.defaultReflectionMode = defaultReflectionMode;
            RenderSettings.defaultReflectionResolution = defaultReflectionResolution;
            RenderSettings.reflectionBounces = reflectionBounces;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.customReflectionTexture = customReflectionTexture;
            RenderSettings.ambientProbe = ambientProbe;
            RenderSettings.sun = mappedSun;

            DynamicGI.UpdateEnvironment();
        }
    }
}
