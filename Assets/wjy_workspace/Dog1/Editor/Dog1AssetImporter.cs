using UnityEditor;
public sealed class Dog1AssetImporter : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith("Assets/wjy_workspace/Dog1/Resources/")) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.optimizeGameObjects = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
    }
    private void OnPreprocessTexture()
    {
        if (assetPath.Contains("wjy_workspace/Dog1/Resources/") && assetPath.EndsWith("_Normal.png"))
            ((TextureImporter)assetImporter).textureType = TextureImporterType.NormalMap;
    }
}