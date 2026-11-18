using UnityEditor;
public sealed class Dog1AssetImporter : AssetPostprocessor
{
    private const string BreedAssets = "Assets/wjy_workspace/Dog1/DogBreedAssets/";

    private void OnPreprocessModel()
    {
        if (assetPath.StartsWith(BreedAssets, System.StringComparison.Ordinal))
        {
            var breedImporter = (ModelImporter)assetImporter;
            breedImporter.animationType = ModelImporterAnimationType.Generic;
            breedImporter.optimizeGameObjects = false;
            breedImporter.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            breedImporter.importAnimation = false;
            return;
        }
        const string resources = "Assets/wjy_workspace/Dog1/Resources/";
        if (!assetPath.StartsWith(resources, System.StringComparison.Ordinal)) return;
        var importer = (ModelImporter)assetImporter;
        if (assetPath.StartsWith(resources + "Dog1/Apartment/Furniture/", System.StringComparison.Ordinal))
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importAnimation = false;
            return;
        }
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.optimizeGameObjects = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
    }
    private void OnPreprocessTexture()
    {
        if ((assetPath.Contains("wjy_workspace/Dog1/Resources/") && assetPath.EndsWith("_Normal.png")) ||
            (assetPath.StartsWith(BreedAssets, System.StringComparison.Ordinal) && assetPath.EndsWith("_n.png")))
            ((TextureImporter)assetImporter).textureType = TextureImporterType.NormalMap;
    }
}
