using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;
using Renderite.Shared;
using SkyFrost.Base;

namespace VrmToResonitePackage;

/// <summary>埋め込みパッケージ内の packdb 画像を出力可能な resdb 参照へ変換する。</summary>
internal static class AvatarPackageImport
{
    public static async Task ConvertPackdbReferencesToResdb(
        DataTreeDictionary graph,
        RecordPackage package,
        Engine engine,
        string assetDescription)
    {
        var importedAssets = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        var references = graph.EnumerateTree()
            .OfType<DataTreeValue>()
            .Select(value => (Value: value, Uri: value.TryExtractURL()))
            .Where(reference => reference.Uri != null &&
                string.Equals(reference.Uri.Scheme, "packdb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach ((DataTreeValue value, Uri sourceUri) in references)
        {
            string signature = RecordPackage.GetAssetSignature(sourceUri);
            if (!importedAssets.TryGetValue(signature, out Uri importedUri))
            {
                byte[] data;
                using (System.IO.Stream source = package.ReadAsset(signature))
                using (var memory = new MemoryStream())
                {
                    source.CopyTo(memory);
                    data = memory.ToArray();
                }

                string extension = DetectImageExtension(data);
                string tempFile = engine.LocalDB.GetTempFilePath(extension);
                try
                {
                    await default(ToBackground);
                    await File.WriteAllBytesAsync(tempFile, data);
                    importedUri = await engine.LocalDB.ImportLocalAssetAsync(
                        tempFile, LocalDB.ImportLocation.Move);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }
                    }
                    finally
                    {
                        await default(ToWorld);
                    }
                }

                if (importedUri == null)
                {
                    throw new InvalidDataException(
                        $"{assetDescription} asset {sourceUri} could not be imported into LocalDB.");
                }
                importedAssets[signature] = importedUri;
                UniLog.Log($"{assetDescription} asset converted: {sourceUri} -> {importedUri}");
            }

            value.UpdateValue(importedUri);
        }
    }

    private static string DetectImageExtension(byte[] data)
    {
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return ".png";
        }
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ".jpg";
        }
        if (data.Length >= 12 &&
            data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
        {
            return ".webp";
        }
        throw new InvalidDataException($"{nameof(AvatarPackageImport)} found an unsupported texture format.");
    }
}
