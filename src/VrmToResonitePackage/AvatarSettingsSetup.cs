using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.Store;
using SkyFrost.Base;

namespace VrmToResonitePackage;

/// <summary>Loading 表示とは独立した Avatar Settings の共通機能を構築する。</summary>
internal static class AvatarSettingsSetup
{
    private const string AvatarSettingsRootVariable = ModularAvatarSetup.SpaceName + "/AvatarSettingsRoot";

    public static async Task<Slot> Apply(Slot root, bool addDefaultUserScale)
    {
        Slot avatarSettings = root.FindChild("<color=#00ffff>Avatar Settings</color>")
                              ?? root.FindChild("Avatar Settings")
                              ?? root.AddSlot("<color=#00ffff>Avatar Settings</color>");
        if (ModularAvatarSetup.HasDynamicVariableSpace(root))
        {
            PublishAvatarSettingsRoot(avatarSettings);
        }
        AddDefaultUserScale(avatarSettings, addDefaultUserScale);
        await ImportAvatarGrabEquip(root, avatarSettings);
        return avatarSettings;
    }

    private static void AddDefaultUserScale(Slot avatarSettings, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        Slot settingsSlot = avatarSettings.FindChild("Default User Scale")
                         ?? avatarSettings.AddSlot("Default User Scale");
        DefaultUserScale userScale = settingsSlot.GetComponent<DefaultUserScale>()
                                  ?? settingsSlot.AttachComponent<DefaultUserScale>();
        userScale.DefaultScale.Value = 1f;
        UniLog.Log("DefaultUserScale を Avatar Settings/Default User Scale に付与: DefaultScale = 1");
    }

    private static async Task ImportAvatarGrabEquip(Slot avatarRoot, Slot avatarSettings)
    {
        string tempPath = Path.Combine(Path.GetTempPath(),
            "ResoPon_AvatarGrabEquip_" + Guid.NewGuid().ToString("N") + ".resonitepackage");
        try
        {
            using (System.IO.Stream resource = typeof(AvatarSettingsSetup).Assembly.GetManifestResourceStream(
                       "VrmToResonitePackage.Resources.FixAvatarGrabEquip.resonitepackage"))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException("Avatar grab/equip resource was not found.");
                }

                using FileStream file = File.Create(tempPath);
                resource.CopyTo(file);
            }

            using RecordPackage package = RecordPackage.Decode(tempPath);
            SkyFrost.Base.Record record = package.MainRecord
                ?? throw new InvalidDataException("Avatar grab/equip package has no main record.");
            string signature = RecordPackage.GetAssetSignature(new Uri(record.AssetURI));
            using System.IO.Stream asset = package.ReadAsset(signature);
            DataTreeDictionary graph = DataTreeConverter.LoadAuto(asset)
                ?? throw new InvalidDataException("Avatar grab/equip package has no DataTree.");

            await AvatarPackageImport.ConvertPackdbReferencesToResdb(
                graph, package, avatarSettings.Engine, "Avatar Grab & Equip");
            Slot packageRoot = avatarSettings.AddSlot("Avatar Grab & Equip");
            packageRoot.LoadObject(graph, record);
            packageRoot.PersistentSelf = true;
            ConnectAvatarGrabbable(packageRoot, avatarRoot);
            packageRoot.ForeachComponentInChildren(delegate(IPackageImportEventReceiver receiver)
            {
                receiver.OnPackageImported();
            }, includeLocal: false, cacheItems: true);
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

    private static void ConnectAvatarGrabbable(Slot packageRoot, Slot avatarRoot)
    {
        Grabbable avatarGrabbable = avatarRoot.GetComponent<Grabbable>();
        if (avatarGrabbable == null)
        {
            UniLog.Warning("Avatar Grab & Equip was imported, but the avatar root has no Grabbable to assign.");
            return;
        }

        List<ReferenceField<IGrabbable>> emptyReferences = packageRoot
            .GetComponentsInChildren<ReferenceField<IGrabbable>>()
            .Where(reference => reference.Reference.Target == null)
            .ToList();
        if (emptyReferences.Count != 1)
        {
            UniLog.Warning($"Avatar Grab & Equip expected one empty IGrabbable ReferenceField, found {emptyReferences.Count}.");
            return;
        }

        emptyReferences[0].Reference.Target = avatarGrabbable;
        UniLog.Log($"Avatar Grab & Equip connected to the avatar Grabbable: {packageRoot.Name}");
    }

    internal static void PublishAvatarSettingsRoot(Slot avatarSettings)
    {
        if (avatarSettings.GetComponents<DynamicReferenceVariable<Slot>>().Any(variable =>
                variable.VariableName.Value == AvatarSettingsRootVariable))
        {
            return;
        }

        ReferenceField<Slot> selfReference = avatarSettings.AttachComponent<ReferenceField<Slot>>();
        selfReference.Reference.Target = avatarSettings;
        DynamicReferenceVariable<Slot> settingsRoot = avatarSettings.AttachComponent<DynamicReferenceVariable<Slot>>();
        settingsRoot.VariableName.Value = AvatarSettingsRootVariable;
        settingsRoot.Reference.DriveFrom(selfReference.Reference);
    }
}
