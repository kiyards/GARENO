using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class SubstanceLikeMaterialImporter : EditorWindow
{
    private const string WindowTitle = "One-Click Model Importer";
    private const string DefaultDestinationRoot = "Assets/Models/Imported";
    private const string UrpLitShaderName = "Universal Render Pipeline/Lit";

    [SerializeField] private string modelSourcePath = string.Empty;
    [SerializeField] private string texturesSourceFolder = string.Empty;
    [SerializeField] private DefaultAsset destinationFolderAsset;
    [SerializeField] private string assetNameOverride = string.Empty;
    [SerializeField] private bool createPrefab = true;
    [SerializeField] private bool assignMaterialToAllRenderers = true;
    [SerializeField] private float normalScale = 1f;
    [SerializeField] private float heightScale = 0.05f;
    [SerializeField] private float occlusionStrength = 1f;
    [SerializeField] private float emissionIntensity = 1f;

    private Vector2 scrollPosition;
    private TextureSourceSelection resolvedTextures = new TextureSourceSelection();

    [MenuItem("Elenroth Tools/Art/One-Click Model Importer")]
    public static void OpenWindow()
    {
        GetWindow<SubstanceLikeMaterialImporter>(WindowTitle);
    }

    private void OnEnable()
    {
        if (destinationFolderAsset == null)
        {
            destinationFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(DefaultDestinationRoot);
        }
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        DrawPathField(
            "Model File",
            ref modelSourcePath,
            "Model (.fbx, .obj)",
            "fbx,obj");
        DrawFolderField("Texture Folder", ref texturesSourceFolder, "Select exported texture folder");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Destination", EditorStyles.boldLabel);
        destinationFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField(
            "Assets Folder",
            destinationFolderAsset,
            typeof(DefaultAsset),
            false);
        assetNameOverride = EditorGUILayout.TextField("Asset Name Override", assetNameOverride);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Material Settings", EditorStyles.boldLabel);
        createPrefab = EditorGUILayout.ToggleLeft("Create prefab after import", createPrefab);
        assignMaterialToAllRenderers = EditorGUILayout.ToggleLeft("Assign the generated material to every renderer slot on the model", assignMaterialToAllRenderers);
        normalScale = EditorGUILayout.Slider("Normal Strength", normalScale, 0f, 4f);
        heightScale = EditorGUILayout.Slider("Height Strength", heightScale, 0f, 0.2f);
        occlusionStrength = EditorGUILayout.Slider("AO Strength", occlusionStrength, 0f, 1f);
        emissionIntensity = EditorGUILayout.Slider("Emission Intensity", emissionIntensity, 0f, 8f);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Auto-Detect Textures", GUILayout.Height(28)))
            {
                resolvedTextures = TextureSourceSelection.FromFolder(texturesSourceFolder);
            }

            using (new EditorGUI.DisabledScope(!CanImport()))
            {
                if (GUILayout.Button("Import Model Package", GUILayout.Height(28)))
                {
                    ImportPackage();
                }
            }
        }

        EditorGUILayout.Space();
        DrawResolvedTextureSummary();
        EditorGUILayout.HelpBox(
            "This tool imports a model plus a Substance-style texture export into a clean folder structure, creates a URP Lit material, converts roughness into Unity smoothness, assigns the textures automatically, and saves a prefab.",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawResolvedTextureSummary()
    {
        EditorGUILayout.LabelField("Detected Texture Set", EditorStyles.boldLabel);
        DrawTextureSummaryLine("Base Color", resolvedTextures.BaseColorPath);
        DrawTextureSummaryLine("Normal", resolvedTextures.NormalPath);
        DrawTextureSummaryLine("Metallic", resolvedTextures.MetallicPath);
        DrawTextureSummaryLine("Roughness", resolvedTextures.RoughnessPath);
        DrawTextureSummaryLine("Height", resolvedTextures.HeightPath);
        DrawTextureSummaryLine("Ambient Occlusion", resolvedTextures.AmbientOcclusionPath);
        DrawTextureSummaryLine("Emission", resolvedTextures.EmissionPath);
    }

    private static void DrawTextureSummaryLine(string label, string path)
    {
        string value = string.IsNullOrEmpty(path) ? "Not Found" : Path.GetFileName(path);
        EditorGUILayout.LabelField(label, value);
    }

    private bool CanImport()
    {
        return !string.IsNullOrEmpty(modelSourcePath)
            && File.Exists(modelSourcePath)
            && destinationFolderAsset != null
            && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(destinationFolderAsset));
    }

    private void ImportPackage()
    {
        try
        {
            string destinationRoot = AssetDatabase.GetAssetPath(destinationFolderAsset);
            if (!AssetDatabase.IsValidFolder(destinationRoot))
            {
                throw new InvalidOperationException("Choose a valid destination folder inside Assets.");
            }

            string assetName = GetAssetName();
            string packageRoot = EnsureFolder(destinationRoot, assetName);
            string modelFolder = EnsureFolder(packageRoot, "Model");
            string texturesFolder = EnsureFolder(packageRoot, "Textures");
            string materialsFolder = EnsureFolder(packageRoot, "Materials");
            string prefabsFolder = EnsureFolder(packageRoot, "Prefabs");

            string importedModelPath = ImportExternalFile(modelSourcePath, modelFolder);
            ImportedTextureSet textureAssets = ImportTextures(texturesFolder);

            string materialPath = $"{materialsFolder}/{assetName}_PBR.mat";
            Material material = CreateOrUpdateMaterial(assetName, materialPath, materialsFolder, textureAssets);

            if (createPrefab)
            {
                string prefabPath = $"{prefabsFolder}/{assetName}.prefab";
                CreatePrefab(importedModelPath, prefabPath, material);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(WindowTitle, $"Imported {assetName} into {packageRoot}.", "OK");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(packageRoot);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(WindowTitle, exception.Message, "OK");
        }
    }

    private string GetAssetName()
    {
        if (!string.IsNullOrWhiteSpace(assetNameOverride))
        {
            return SanitizeName(assetNameOverride);
        }

        return SanitizeName(Path.GetFileNameWithoutExtension(modelSourcePath));
    }

    private static string SanitizeName(string rawName)
    {
        string[] invalidChars = Path.GetInvalidFileNameChars().Select(character => character.ToString()).ToArray();
        string sanitized = invalidChars.Aggregate(rawName.Trim(), (current, invalid) => current.Replace(invalid, string.Empty));
        return string.IsNullOrWhiteSpace(sanitized) ? "ImportedModel" : sanitized.Replace(' ', '_');
    }

    private ImportedTextureSet ImportTextures(string texturesFolder)
    {
        if (!Directory.Exists(texturesSourceFolder))
        {
            return new ImportedTextureSet();
        }

        resolvedTextures = TextureSourceSelection.FromFolder(texturesSourceFolder);

        return new ImportedTextureSet
        {
            BaseColor = ImportTextureAsset(resolvedTextures.BaseColorPath, texturesFolder, TextureImportIntent.BaseColor),
            Normal = ImportTextureAsset(resolvedTextures.NormalPath, texturesFolder, TextureImportIntent.Normal),
            Metallic = ImportTextureAsset(resolvedTextures.MetallicPath, texturesFolder, TextureImportIntent.LinearMask),
            Roughness = ImportTextureAsset(resolvedTextures.RoughnessPath, texturesFolder, TextureImportIntent.LinearMask),
            Height = ImportTextureAsset(resolvedTextures.HeightPath, texturesFolder, TextureImportIntent.LinearMask),
            AmbientOcclusion = ImportTextureAsset(resolvedTextures.AmbientOcclusionPath, texturesFolder, TextureImportIntent.LinearMask),
            Emission = ImportTextureAsset(resolvedTextures.EmissionPath, texturesFolder, TextureImportIntent.Emission)
        };
    }

    private Material CreateOrUpdateMaterial(string assetName, string materialPath, string materialsFolder, ImportedTextureSet textures)
    {
        Shader shader = Shader.Find(UrpLitShaderName);
        if (shader == null)
        {
            throw new InvalidOperationException($"Could not find shader '{UrpLitShaderName}'. Make sure URP is installed and active.");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = $"{assetName}_PBR"
            };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            material.shader = shader;
        }

        ApplyMaterialTextures(material, assetName, materialsFolder, textures);
        EditorUtility.SetDirty(material);
        return material;
    }

    private void ApplyMaterialTextures(Material material, string assetName, string materialsFolder, ImportedTextureSet textures)
    {
        material.SetFloat("_WorkflowMode", 1f);
        material.SetFloat("_Smoothness", 0.5f);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_BumpScale", normalScale);
        material.SetFloat("_Parallax", heightScale);
        material.SetFloat("_OcclusionStrength", occlusionStrength);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

        if (textures.BaseColor != null)
        {
            material.SetTexture("_BaseMap", textures.BaseColor);
        }
        else
        {
            material.SetTexture("_BaseMap", null);
        }

        if (textures.Normal != null)
        {
            material.SetTexture("_BumpMap", textures.Normal);
            material.EnableKeyword("_NORMALMAP");
        }
        else
        {
            material.SetTexture("_BumpMap", null);
            material.DisableKeyword("_NORMALMAP");
        }

        if (textures.Height != null)
        {
            material.SetTexture("_ParallaxMap", textures.Height);
            material.EnableKeyword("_PARALLAXMAP");
        }
        else
        {
            material.SetTexture("_ParallaxMap", null);
            material.DisableKeyword("_PARALLAXMAP");
        }

        if (textures.AmbientOcclusion != null)
        {
            material.SetTexture("_OcclusionMap", textures.AmbientOcclusion);
            material.EnableKeyword("_OCCLUSIONMAP");
        }
        else
        {
            material.SetTexture("_OcclusionMap", null);
            material.DisableKeyword("_OCCLUSIONMAP");
        }

        if (textures.Emission != null)
        {
            material.SetTexture("_EmissionMap", textures.Emission);
            material.SetColor("_EmissionColor", Color.white * emissionIntensity);
            material.EnableKeyword("_EMISSION");
        }
        else
        {
            material.SetTexture("_EmissionMap", null);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
        }

        Texture2D packedMask = CreatePackedMaskTexture(assetName, materialsFolder, textures.Metallic, textures.Roughness);
        if (packedMask != null)
        {
            material.SetTexture("_MetallicGlossMap", packedMask);
            material.SetFloat("_Smoothness", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else
        {
            material.SetTexture("_MetallicGlossMap", null);
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
        }
    }

    private Texture2D CreatePackedMaskTexture(string assetName, string materialsFolder, Texture2D metallic, Texture2D roughness)
    {
        if (metallic == null && roughness == null)
        {
            return null;
        }

        Texture2D sourceTexture = metallic != null ? metallic : roughness;
        string sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
        TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (sourceImporter == null)
        {
            throw new InvalidOperationException($"Could not read texture importer for {sourcePath}.");
        }

        int width = sourceTexture.width;
        int height = sourceTexture.height;
        Texture2D metallicReadable = GetReadableCopy(metallic, width, height);
        Texture2D roughnessReadable = GetReadableCopy(roughness, width, height);

        Texture2D packedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float metallicValue = metallicReadable != null ? metallicReadable.GetPixel(x, y).grayscale : 0f;
                float roughnessValue = roughnessReadable != null ? roughnessReadable.GetPixel(x, y).grayscale : 0.5f;
                Color packed = new Color(metallicValue, 0f, 0f, 1f - roughnessValue);
                packedTexture.SetPixel(x, y, packed);
            }
        }

        packedTexture.Apply(false, false);

        string packedPath = $"{materialsFolder}/{assetName}_MaskMap.png";
        File.WriteAllBytes(GetAbsoluteProjectPath(packedPath), packedTexture.EncodeToPNG());
        AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceUpdate);

        TextureImporter packedImporter = AssetImporter.GetAtPath(packedPath) as TextureImporter;
        if (packedImporter != null)
        {
            packedImporter.textureType = TextureImporterType.Default;
            packedImporter.sRGBTexture = false;
            packedImporter.alphaSource = TextureImporterAlphaSource.FromInput;
            packedImporter.alphaIsTransparency = false;
            packedImporter.mipmapEnabled = true;
            packedImporter.SaveAndReimport();
        }

        DestroyImmediate(packedTexture);
        if (metallicReadable != null)
        {
            DestroyImmediate(metallicReadable);
        }

        if (roughnessReadable != null)
        {
            DestroyImmediate(roughnessReadable);
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath);
    }

    private static Texture2D GetReadableCopy(Texture2D texture, int width, int height)
    {
        if (texture == null)
        {
            return null;
        }

        RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(texture, temporary);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = temporary;

        Texture2D readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readable.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temporary);
        return readable;
    }

    private void CreatePrefab(string modelAssetPath, string prefabPath, Material material)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
        if (modelAsset == null)
        {
            throw new InvalidOperationException($"Could not load imported model at {modelAssetPath}.");
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
        if (instance == null)
        {
            throw new InvalidOperationException($"Could not instantiate model at {modelAssetPath}.");
        }

        try
        {
            if (assignMaterialToAllRenderers)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] sharedMaterials = renderer.sharedMaterials;
                    if (sharedMaterials == null || sharedMaterials.Length == 0)
                    {
                        continue;
                    }

                    for (int index = 0; index < sharedMaterials.Length; index++)
                    {
                        sharedMaterials[index] = material;
                    }

                    renderer.sharedMaterials = sharedMaterials;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    private Texture2D ImportTextureAsset(string sourcePath, string destinationFolder, TextureImportIntent importIntent)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        string assetPath = ImportExternalFile(sourcePath, destinationFolder);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            return null;
        }

        switch (importIntent)
        {
            case TextureImportIntent.BaseColor:
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                break;
            case TextureImportIntent.Normal:
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                break;
            case TextureImportIntent.LinearMask:
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                break;
            case TextureImportIntent.Emission:
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                break;
        }

        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private string ImportExternalFile(string sourcePath, string destinationFolder)
    {
        string fileName = Path.GetFileName(sourcePath);
        string destinationPath = AssetDatabase.GenerateUniqueAssetPath($"{destinationFolder}/{fileName}");
        FileUtil.CopyFileOrDirectory(sourcePath, destinationPath);
        AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceSynchronousImport);
        return destinationPath;
    }

    private static string EnsureFolder(string parentFolder, string folderName)
    {
        string combinedPath = $"{parentFolder}/{folderName}";
        if (AssetDatabase.IsValidFolder(combinedPath))
        {
            return combinedPath;
        }

        string guid = AssetDatabase.CreateFolder(parentFolder, folderName);
        return AssetDatabase.GUIDToAssetPath(guid);
    }

    private static string GetAbsoluteProjectPath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
        {
            throw new InvalidOperationException("Could not resolve the Unity project root.");
        }

        return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void DrawPathField(string label, ref string currentPath, string panelTitle, string extensions)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            currentPath = EditorGUILayout.TextField(label, currentPath);
            if (GUILayout.Button("Browse", GUILayout.Width(72)))
            {
                string selected = EditorUtility.OpenFilePanel(panelTitle, GetBrowseRoot(currentPath), extensions);
                if (!string.IsNullOrEmpty(selected))
                {
                    currentPath = selected;
                }
            }
        }
    }

    private static void DrawFolderField(string label, ref string currentPath, string panelTitle)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            currentPath = EditorGUILayout.TextField(label, currentPath);
            if (GUILayout.Button("Browse", GUILayout.Width(72)))
            {
                string selected = EditorUtility.OpenFolderPanel(panelTitle, GetBrowseRoot(currentPath), string.Empty);
                if (!string.IsNullOrEmpty(selected))
                {
                    currentPath = selected;
                }
            }
        }
    }

    private static string GetBrowseRoot(string currentPath)
    {
        if (!string.IsNullOrEmpty(currentPath))
        {
            if (Directory.Exists(currentPath))
            {
                return currentPath;
            }

            string directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private enum TextureImportIntent
    {
        BaseColor,
        Normal,
        LinearMask,
        Emission
    }

    [Serializable]
    private sealed class TextureSourceSelection
    {
        private static readonly Dictionary<TextureSlot, string[]> Matchers = new Dictionary<TextureSlot, string[]>
        {
            { TextureSlot.BaseColor, new[] { "basecolor", "base_color", "albedo", "diffuse", "color", "col" } },
            { TextureSlot.Normal, new[] { "normal", "normalgl", "nor" } },
            { TextureSlot.Metallic, new[] { "metallic", "metalness", "metal" } },
            { TextureSlot.Roughness, new[] { "roughness", "rough" } },
            { TextureSlot.Height, new[] { "height", "displacement", "disp", "parallax" } },
            { TextureSlot.AmbientOcclusion, new[] { "ambientocclusion", "ambient_occlusion", "occlusion", "ao" } },
            { TextureSlot.Emission, new[] { "emissive", "emission" } }
        };

        public string BaseColorPath;
        public string NormalPath;
        public string MetallicPath;
        public string RoughnessPath;
        public string HeightPath;
        public string AmbientOcclusionPath;
        public string EmissionPath;

        public static TextureSourceSelection FromFolder(string folderPath)
        {
            TextureSourceSelection selection = new TextureSourceSelection();
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return selection;
            }

            string[] files = Directory.GetFiles(folderPath)
                .Where(IsSupportedTextureFile)
                .OrderBy(path => path.Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            HashSet<string> usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            selection.BaseColorPath = Resolve(files, TextureSlot.BaseColor, usedPaths);
            selection.NormalPath = Resolve(files, TextureSlot.Normal, usedPaths);
            selection.MetallicPath = Resolve(files, TextureSlot.Metallic, usedPaths);
            selection.RoughnessPath = Resolve(files, TextureSlot.Roughness, usedPaths);
            selection.HeightPath = Resolve(files, TextureSlot.Height, usedPaths);
            selection.AmbientOcclusionPath = Resolve(files, TextureSlot.AmbientOcclusion, usedPaths);
            selection.EmissionPath = Resolve(files, TextureSlot.Emission, usedPaths);

            // If naming does not match our expected patterns, fall back to any remaining textures.
            selection.BaseColorPath ??= TakeFirstUnused(files, usedPaths);
            selection.NormalPath ??= TakeFirstUnused(files, usedPaths);
            selection.MetallicPath ??= TakeFirstUnused(files, usedPaths);
            selection.RoughnessPath ??= TakeFirstUnused(files, usedPaths);
            selection.HeightPath ??= TakeFirstUnused(files, usedPaths);
            selection.AmbientOcclusionPath ??= TakeFirstUnused(files, usedPaths);
            selection.EmissionPath ??= TakeFirstUnused(files, usedPaths);

            return selection;
        }

        private static string Resolve(IEnumerable<string> files, TextureSlot slot, ISet<string> usedPaths)
        {
            string[] candidates = Matchers[slot];
            string resolvedPath = files.FirstOrDefault(path =>
            {
                if (usedPaths.Contains(path))
                {
                    return false;
                }

                string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                return candidates.Any(candidate => fileName.Contains(candidate));
            });

            if (!string.IsNullOrEmpty(resolvedPath))
            {
                usedPaths.Add(resolvedPath);
            }

            return resolvedPath;
        }

        private static string TakeFirstUnused(IEnumerable<string> files, ISet<string> usedPaths)
        {
            string resolvedPath = files.FirstOrDefault(path => !usedPaths.Contains(path));
            if (!string.IsNullOrEmpty(resolvedPath))
            {
                usedPaths.Add(resolvedPath);
            }

            return resolvedPath;
        }

        private static bool IsSupportedTextureFile(string path)
        {
            string extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension == ".png"
                || extension == ".tga"
                || extension == ".jpg"
                || extension == ".jpeg"
                || extension == ".tif"
                || extension == ".tiff";
        }
    }

    private enum TextureSlot
    {
        BaseColor,
        Normal,
        Metallic,
        Roughness,
        Height,
        AmbientOcclusion,
        Emission
    }

    private sealed class ImportedTextureSet
    {
        public Texture2D BaseColor;
        public Texture2D Normal;
        public Texture2D Metallic;
        public Texture2D Roughness;
        public Texture2D Height;
        public Texture2D AmbientOcclusion;
        public Texture2D Emission;
    }
}
