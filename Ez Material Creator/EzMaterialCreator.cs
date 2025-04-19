using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using File = UnityEngine.Windows.File;

public class EzMaterialCreator : EditorWindow
{
    enum MaterialCreatorMode
    {
        Metallic,
        Specular
    }
    
    private Vector2 scrollPos = Vector2.zero;
    private string folderPath = "";
    private string prefix = "m_";
    private string nameOverride = "";
    private string suffix = "";
    private MaterialCreatorMode mode = MaterialCreatorMode.Specular;
    private MaterialGlobalIlluminationFlags illuminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
    private Material createdMaterial;
    private Material conversionMaterial;
    private Texture2D albedo, normal, roughness, smoothness, metalness, specular, ao, emission;
    [ColorUsage(false, true)]
    private Color emissionColor;
    private bool packSmoothnessInAlpha = false;
    private bool enableEmission = false;

    private const string PrefixKey = "EzMC_Prefix";
    private const string SuffixKey = "EzMC_Suffix";

    private void Awake()
    {
        prefix = EditorPrefs.GetString(PrefixKey, "m_");
        suffix = EditorPrefs.GetString(SuffixKey, "");
    }

    private void OnDestroy()
    {
        EditorPrefs.SetString("EzMC_Prefix", prefix);
        EditorPrefs.SetString("EzMC_Suffix", suffix);
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, false, false);
        var previousPath = folderPath;
        using(new GUILayout.HorizontalScope()){
            folderPath = EditorGUILayout.TextField("Folder Path", folderPath);
            if(GUILayout.Button("Browse")){
                //set default values for directory, then try to override them with values of existing path
                string directory = "Assets";
                var newfolder = EditorUtility.OpenFolderPanel("Choose a folder", directory, "");
                if (newfolder != folderPath && !string.IsNullOrEmpty(newfolder))
                {
                    folderPath = "Assets/" + newfolder.Split("Assets/").Last();
                    GetTextures();
                    Repaint();
                    return;
                }
                    
            }
        }
        

        if (folderPath != previousPath)
        {
            albedo = null;
        }
        
        if (GUILayout.Button("Get Textures"))
        {
            GetTextures();
        }

        
        albedo = EditorGUILayout.ObjectField("Albedo", albedo, typeof(Texture2D), false) as Texture2D;
        normal = EditorGUILayout.ObjectField("Normal", normal, typeof(Texture2D), false) as Texture2D;
        roughness = EditorGUILayout.ObjectField("Roughness", roughness, typeof(Texture2D), false) as Texture2D;
        smoothness = EditorGUILayout.ObjectField("Smoothness", smoothness, typeof(Texture2D), false) as Texture2D;
        metalness = EditorGUILayout.ObjectField("Metalness", metalness, typeof(Texture2D), false) as Texture2D;
        specular = EditorGUILayout.ObjectField("Specular", specular, typeof(Texture2D), false) as Texture2D;
        ao = EditorGUILayout.ObjectField("AO", ao, typeof(Texture2D), false) as Texture2D;
        
        enableEmission = EditorGUILayout.Toggle("Enable Emission", enableEmission);
        if (enableEmission)
        {
            emission = EditorGUILayout.ObjectField("Emission", emission, typeof(Texture2D), false) as Texture2D;
            emissionColor = EditorGUILayout.ColorField(new GUIContent("Emission color"), emissionColor, true, false, true);
            illuminationFlags = (MaterialGlobalIlluminationFlags) EditorGUILayout.EnumPopup("Global Illumination", illuminationFlags);
            if (illuminationFlags == MaterialGlobalIlluminationFlags.AnyEmissive)
            {
                illuminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else if (illuminationFlags == MaterialGlobalIlluminationFlags.EmissiveIsBlack)
            {
                EditorGUILayout.HelpBox($"If the illumination is set to {illuminationFlags}, emission will be disabled.", MessageType.Warning);
            }
        }

        var style = new GUIStyle() { fontSize = 16, richText = true, normal = new GUIStyleState() { textColor = Color.white } };
        
        EditorGUILayout.LabelField("Material type:");
        if (specular != null && metalness != null)
        {
            style.normal.textColor = Color.green;
            EditorGUILayout.LabelField("Specular OR Metallic", style);
            mode = (MaterialCreatorMode) EditorGUILayout.EnumPopup("Mode", mode);
        }
        else if (metalness != null)
        {
            style.normal.textColor = Color.yellow;
            EditorGUILayout.LabelField("Metallic", style);
            mode = MaterialCreatorMode.Metallic;
        }
        else if (specular != null)
        {
            style.normal.textColor = Color.cyan;
            EditorGUILayout.LabelField("Specular", style);
            mode = MaterialCreatorMode.Specular;
        }
        else
        {
            style.normal.textColor = Color.red;
            EditorGUILayout.LabelField("Metallic/Specular not found!", style);
            EditorGUILayout.HelpBox("A metallic material will be created, with metalness set to 1.", MessageType.Info);
            mode = MaterialCreatorMode.Metallic;
        }

        if (smoothness != null && roughness != null)
        {
            style.normal.textColor = Color.cyan;
            EditorGUILayout.LabelField("Found smoothness and roughness maps. Prioritizing smoothness.", style);
        }
        else if (smoothness != null)
        {
            style.normal.textColor = Color.green;
            EditorGUILayout.LabelField("Found a smoothness map.", style);
        }
        else if (roughness != null)
        {
            style.normal.textColor = Color.yellow;
            EditorGUILayout.LabelField("Found a roughness map. Will invert it when packing textures.", style);
        }
        else
        {
            style.normal.textColor = Color.red;
            EditorGUILayout.LabelField("A smoothness map was not found!", style);
            EditorGUILayout.HelpBox("The smoothness will be set to 1.", MessageType.Info);
        }

        if(roughness || smoothness)
        {
            packSmoothnessInAlpha = EditorGUILayout.Toggle("Pack smoothness/roughness in Alpha channel", packSmoothnessInAlpha);
        }
        else
        {
            packSmoothnessInAlpha = false;
            EditorGUILayout.HelpBox("As neither a metallic/specular nor a smoothness/roughness maps were not found, their textures will not be assigned.", MessageType.Warning);
        }
        
        prefix = EditorGUILayout.TextField("Material prefix", prefix);
        nameOverride = EditorGUILayout.TextField("Material name override", nameOverride);
        suffix = EditorGUILayout.TextField("Material suffix", suffix); 
        
        GUI.backgroundColor = Color.green;
        
        if (GUILayout.Button("OneClickGenerate!", GUILayout.Height(48)))
        {
            packSmoothnessInAlpha = roughness || smoothness;
            FixTextureImport(smoothness? smoothness : roughness, false, false);
            FixTextureImport(specular? specular : metalness, true, false);
            CreateMaterial();
            FixTextureImports();
        }
        
        GUI.backgroundColor = Color.white;
        
        if (GUILayout.Button("Create Material"))
        {
            CreateMaterial();
        }

        if (GUILayout.Button("Fix Texture Imports"))
        {
            FixTextureImports();
        }
        
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void FixTextureImports()
    {
        FixTextureImport(albedo, true, false);
        FixTextureImport(normal, false, true);
        FixTextureImport(roughness, false, false);
        FixTextureImport(smoothness, false, false);
        FixTextureImport(specular, false, false);
        FixTextureImport(metalness, false, false);
        FixTextureImport(ao, false, false);
        FixTextureImport(emission, false, false);
    }

    private void FixTextureImport(Texture2D tex, bool isSRGB, bool isNormalMap)
    {
        if (!tex)
        {
            return;
        }
        
        TextureImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
        importer.textureType = isNormalMap? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = isSRGB;
        importer.SaveAndReimport();
    }

    private void CreateMaterial()
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            EditorUtility.DisplayDialog("Empty folder path", "The folder path is empty.", "OK");
            return;
        }
        if (!folderPath.Contains("Assets") || !folderPath.Split("/").First().Contains("Assets"))
        {
            EditorUtility.DisplayDialog("Invalid folder path", "The folder path does not start at the Assets folder.", "OK");
            return;
        }
        string matName = "Material";
        string foundName = folderPath.Split("/").Last();
        if (foundName != "")
        {
            matName = foundName;
        }
        
        var existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(folderPath + $"/{prefix}{matName}.mat");
        if (existingMaterial)
        {
            createdMaterial = existingMaterial;
        }
        else
        {
            createdMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            
        }
        createdMaterial.SetTexture("_BaseMap", albedo);
        createdMaterial.SetTexture("_BumpMap", normal);
        createdMaterial.SetTexture("_MetallicGlossMap", metalness);
        createdMaterial.SetTexture("_SpecGlossMap", specular);
        createdMaterial.SetTexture("_OcclusionMap", ao);
        
        //0 equals specular, 1 equals metallic
        if (mode == MaterialCreatorMode.Metallic)
        {
            createdMaterial.SetFloat("_WorkflowMode", 1);
            Debug.Log("Created metallic");
        }
        else
        {
            createdMaterial.SetFloat("_WorkflowMode", 0);
            Debug.Log("Created specular");
        }

        if (packSmoothnessInAlpha)
        {
            //todo: redo this

            PrepareConversionMaterial();
            int texWidth = 32;
            int texHeight = 32;
            if (roughness)
            {
                texHeight = roughness.height;
                texWidth = roughness.width;
            }
            else if (smoothness)
            {
                texHeight = smoothness.height;
                texWidth = smoothness.width;
            }
            RenderTexture packedRender = RenderTexture.GetTemporary(texWidth, texHeight);
            Graphics.Blit(null, packedRender, conversionMaterial);
            
            Texture2D packed = new Texture2D(texWidth, texHeight);
            RenderTexture.active = packedRender;
            packed.ReadPixels(new Rect(Vector2.zero, new Vector2(texWidth, texHeight)), 0, 0);

            var type = mode == MaterialCreatorMode.Metallic ? "Metalness" : "Specular";
            var path = folderPath + $"/Packed{type}{folderPath.Split("/").Last()}SmoothnessTexture.png";
            byte[] packedPng = packed.EncodeToPNG();
            File.WriteAllBytes(path, packedPng);
            
            AssetDatabase.Refresh();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(packedRender);
            DestroyImmediate(packed);
            
            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            FixTextureImport(saved, false, false);
            if(mode == MaterialCreatorMode.Metallic)
                createdMaterial.SetTexture("_MetallicGlossMap", saved);
            else if (mode == MaterialCreatorMode.Specular)
                createdMaterial.SetTexture("_SpecGlossMap", saved);
                
            createdMaterial.SetFloat("_Smoothness", 1f);
        }
        else
        {
            if (mode == MaterialCreatorMode.Metallic)
            {
                createdMaterial.SetTexture("_MetallicGlossMap", metalness);
                createdMaterial.SetFloat("_Metallic", 1.0f);
                
            }
            else if (mode == MaterialCreatorMode.Specular)
            {
                createdMaterial.SetTexture("_SpecGlossMap", specular);
                createdMaterial.SetColor("_SpecColor", Color.white);
            }

            createdMaterial.SetFloat("_Smoothness", 1f);
        }

        if (enableEmission && illuminationFlags != MaterialGlobalIlluminationFlags.EmissiveIsBlack)
        {
            createdMaterial.EnableKeyword("_EMISSION");
            createdMaterial.globalIlluminationFlags = illuminationFlags;
            createdMaterial.SetColor("_EmissionColor", emissionColor);
            createdMaterial.SetTexture("_EmissionMap", emission);
        }
        else
        {
            createdMaterial.DisableKeyword("_EMISSION");
            createdMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            createdMaterial.SetColor("_EmissionColor", Color.black);
            createdMaterial.SetTexture("_EmissionMap", null);
        }

        if (!existingMaterial)
        {
            AssetDatabase.CreateAsset(createdMaterial, folderPath + $"/{prefix}{matName}.mat");
        }
    }

    private void PrepareConversionMaterial()
    {
        if (!conversionMaterial)
        {
            conversionMaterial = new Material(Shader.Find("Hidden/PackSmoothness"));
        }

        conversionMaterial.SetFloat("_InvertSmoothness", smoothness? 0 : 1f);
        
        if (smoothness == null && roughness == null)
        {
            conversionMaterial.SetTexture("_SmoothnessMap", null); //Defaulting to black
        }
        conversionMaterial.SetTexture("_SmoothnessMap", smoothness? smoothness: roughness);

        if (metalness == null && specular == null)
        {
            conversionMaterial.SetTexture("_SurfaceMap", null);
        }
        conversionMaterial.SetTexture("_SurfaceMap", metalness? metalness : specular);
        
    }

    private void GetTextures()
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            EditorUtility.DisplayDialog("Empty folder path", "Cannot load textures because the folder path is empty.", "OK");
            return;
        }
        
        albedo = FindTextureByNames(new string[] { "Albedo", "Color", "Base" });
        normal = FindTextureByNames(new string[] { "Normal", "Bump" });
        roughness = FindTextureByNames(new string[] { "Rough" });
        smoothness = FindTextureByNames(new string[] { "Smooth" });
        metalness = FindTextureByNames(new string[] { "Metal" });
        specular = FindTextureByNames(new string[] { "Specular" });
        ao = FindTextureByNames(new string[] { "AO", "Occlusion" });
        emission = FindTextureByNames(new string[] { "Emiss" });
        if (emission)
        {
            enableEmission = true;
        }
    }

    private Texture2D FindTextureByNames(string[] names)
    { 
        List<string> ids = new();
        foreach (string s in names)
        {
            string[] tempIds = AssetDatabase.FindAssets(s, new[] { folderPath });
            if (tempIds.Length <= 0)
            {
                continue;
            }
            
            ids = ids.Union(tempIds).ToList();
        }
        
        foreach (string id in ids)
        {
            if (id != null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(id));
                if (!texture)
                {
                    continue;
                }
                Debug.Log(texture.name);
                if (texture.name.ToLower().Contains("packed"))
                {
                    continue;
                }

                return texture;
            }
        }

        return null;
    }

    [MenuItem("EzMaterialCreator/Open Creator")]
    static void CreateMaterialInFolder()
    {
        EditorWindow.GetWindow<EzMaterialCreator>("EzMaterialCreator");
    }
}
