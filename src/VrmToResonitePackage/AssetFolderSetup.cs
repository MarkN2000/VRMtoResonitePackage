using Elements.Core;
using FrooxEngine;

namespace VrmToResonitePackage;

/// <summary>Assets 直下のアセットスロットを、種類ごとのフォルダへ整理する。</summary>
internal static class AssetFolderSetup
{
    public static void Apply(Slot assetsSlot)
    {
        Slot meshes = GetOrAddFolder(assetsSlot, "Meshes");
        Slot materials = GetOrAddFolder(assetsSlot, "Materials");
        Slot textures = GetOrAddFolder(assetsSlot, "Textures");
        var categoryFolders = new HashSet<Slot> { meshes, materials, textures };

        int meshCount = 0;
        int materialCount = 0;
        int textureCount = 0;
        int unclassifiedCount = 0;

        foreach (Slot child in assetsSlot.Children.ToList())
        {
            if (categoryFolders.Contains(child))
            {
                continue;
            }

            switch (Classify(child))
            {
                case AssetCategory.Meshes:
                    child.Parent = meshes;
                    meshCount++;
                    break;

                case AssetCategory.Materials:
                    child.Parent = materials;
                    materialCount++;
                    break;

                case AssetCategory.Textures:
                    child.Parent = textures;
                    textureCount++;
                    break;

                case AssetCategory.Unclassified:
                    unclassifiedCount++;
                    break;
            }
        }

        UniLog.Log($"Assetsを整理しました: Meshes={meshCount}, Materials={materialCount}, " +
                   $"Textures={textureCount}, 直下に保持={unclassifiedCount}");
    }

    private static Slot GetOrAddFolder(Slot assetsSlot, string name)
    {
        Slot existing = assetsSlot.Children.FirstOrDefault(child =>
            string.Equals(child.Name, name, StringComparison.Ordinal));
        return existing ?? assetsSlot.AddSlot(name);
    }

    private static AssetCategory Classify(Slot slot)
    {
        AssetCategory category = AssetCategory.None;

        foreach (IAssetProvider provider in slot.GetComponentsInChildren<IAssetProvider>())
        {
            AssetCategory current = provider switch
            {
                StaticMesh => AssetCategory.Meshes,
                StaticTexture2D => AssetCategory.Textures,
                IAssetProvider<FrooxEngine.Material> => AssetCategory.Materials,
                _ => AssetCategory.Unclassified,
            };

            if (current == AssetCategory.Unclassified)
            {
                return AssetCategory.Unclassified;
            }
            if (category != AssetCategory.None && category != current)
            {
                return AssetCategory.Unclassified;
            }
            category = current;
        }

        return category == AssetCategory.None ? AssetCategory.Unclassified : category;
    }

    private enum AssetCategory
    {
        None,
        Meshes,
        Materials,
        Textures,
        Unclassified,
    }
}
