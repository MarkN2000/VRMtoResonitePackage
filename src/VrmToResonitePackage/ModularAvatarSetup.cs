using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;
using SkyFrost.Base;

namespace VrmToResonitePackage;

/// <summary>共有の modular_avatar 名前空間と装着判定パッケージを必要時だけ用意する。</summary>
internal static class ModularAvatarSetup
{
    public const string SpaceName = "modular_avatar";

    public static bool HasDynamicVariableSpace(Slot root) =>
        root.GetComponents<DynamicVariableSpace>()
            .Any(space => space.SpaceName.Value == SpaceName);

    public static void EnsureDynamicVariableSpace(Slot root)
    {
        if (HasDynamicVariableSpace(root))
        {
            return;
        }

        DynamicVariableSpace space = root.AttachComponent<DynamicVariableSpace>();
        space.SpaceName.Value = SpaceName;
        space.OnlyDirectBinding.Value = true;
    }

    public static void EnsureAvatarRootIdentification(Slot root)
    {
        if (root.FindChild("Avatar Root Identification") != null)
        {
            return;
        }

        string tempPath = Path.Combine(Path.GetTempPath(),
            "ResoPon_AvatarRootIdentification_" + Guid.NewGuid().ToString("N") + ".resonitepackage");
        try
        {
            using (System.IO.Stream resource = typeof(ModularAvatarSetup).Assembly.GetManifestResourceStream(
                       "VrmToResonitePackage.Resources.AvatarRootIdentification.resonitepackage"))
            {
                if (resource == null)
                {
                    UniLog.Warning("Avatar Root Identification resource が見つかりませんでした。装着判定をスキップします。");
                    return;
                }

                using FileStream file = File.Create(tempPath);
                resource.CopyTo(file);
            }

            using RecordPackage package = RecordPackage.Decode(tempPath);
            SkyFrost.Base.Record record = package.MainRecord;
            if (record == null)
            {
                UniLog.Warning("Avatar Root Identification package に main record がありません。装着判定をスキップします。");
                return;
            }

            string signature = RecordPackage.GetAssetSignature(new Uri(record.AssetURI));
            using System.IO.Stream asset = package.ReadAsset(signature);
            DataTreeDictionary graph = DataTreeConverter.LoadAuto(asset);
            if (graph == null)
            {
                UniLog.Warning("Avatar Root Identification package の DataTree を読み込めませんでした。装着判定をスキップします。");
                return;
            }

            Slot packageRoot = root.AddSlot("Avatar Root Identification");
            packageRoot.LoadObject(graph, record);
            packageRoot.ForeachComponentInChildren(delegate(IPackageImportEventReceiver receiver)
            {
                receiver.OnPackageImported();
            }, includeLocal: false, cacheItems: true);
        }
        catch (Exception ex)
        {
            UniLog.Warning($"Avatar Root Identification package の読み込みに失敗しました。装着判定をスキップします: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // 一時ファイルの削除失敗は変換結果に影響しない。
            }
        }
    }
}
