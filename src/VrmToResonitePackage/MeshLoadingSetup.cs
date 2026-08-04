using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.Store;
using Renderite.Shared;
using SkyFrost.Base;

namespace VrmToResonitePackage;

/// <summary>
/// Keeps imported renderers hidden until all of their meshes are available on the client.
/// A small procedural stand-in remains visible while the assets are loading.
/// </summary>
internal static class MeshLoadingSetup
{
    private const string DynamicVariableSpaceName = "modular_avatar";
    private const string AvatarSettingsRootVariable = "modular_avatar/AvatarSettingsRoot";
    private const string MeshNotLoadedVariable = "modular_avatar/MeshNotLoaded";
    private const string HeadBoneVariable = "modular_avatar/HumanBone.head";
    private const string HeadPoseVariable = "modular_avatar/HumanBonePose.head";
    private const string AvatarPoseNodeHeadVariable = "modular_avatar/AvatarPoseNode.Head";

    public static async Task Apply(Slot root)
    {
        List<MeshRenderer> renderers = root.GetComponentsInChildren<MeshRenderer>()
            .Where(renderer => renderer.Mesh.Target != null)
            .ToList();
        if (renderers.Count == 0)
        {
            return;
        }

        EnsureDynamicVariableSpace(root);
        PublishHeadPose(root);

        Slot loadCheck = root.AddSlot("<color=cyan>ResoPon</color> - Mesh loaded check");
        MeshRendererLoadStatus loadStatus = loadCheck.AttachComponent<MeshRendererLoadStatus>();
        DynamicValueVariable<bool> loadVariable = loadCheck.AttachComponent<DynamicValueVariable<bool>>();
        loadVariable.Value.Value = false;

        BooleanValueDriver<string> variableNameDriver = loadCheck.AttachComponent<BooleanValueDriver<string>>();
        variableNameDriver.FalseValue.Value = MeshNotLoadedVariable;
        variableNameDriver.TrueValue.Value = null;
        variableNameDriver.TargetField.Target = loadVariable.VariableName;
        variableNameDriver.State.DriveFrom(loadStatus.IsLoaded);

        Slot avatarSettings = root.FindChild("<color=#00ffff>Avatar Settings</color>")
                              ?? root.FindChild("Avatar Settings")
                              ?? root.AddSlot("<color=#00ffff>Avatar Settings</color>");
        PublishAvatarSettingsRoot(avatarSettings);
        await ImportAvatarGrabEquip(root, avatarSettings);
        Slot loadingDisplay = avatarSettings.AddSlot("Avatar Loading Display");
        Slot standIn = await ImportLoadingStandIn(loadingDisplay);

        foreach (MeshRenderer renderer in renderers)
        {
            DynamicValueVariableDriver<bool> driver = renderer.Slot.AttachComponent<DynamicValueVariableDriver<bool>>();
            driver.VariableName.Value = MeshNotLoadedVariable;
            driver.DefaultValue.Value = true;
            driver.Target.Target = renderer.EnabledField;
            loadStatus.Renderers.Add().Target = renderer;
        }

        DynamicValueVariableDriver<bool> standInDriver = loadingDisplay.AttachComponent<DynamicValueVariableDriver<bool>>();
        BooleanValueDriver<bool> invertDriver = loadingDisplay.AttachComponent<BooleanValueDriver<bool>>();
        invertDriver.TargetField.Target = standIn.ActiveSelf_Field;
        invertDriver.FalseValue.Value = true;
        invertDriver.TrueValue.Value = false;
        standInDriver.VariableName.Value = MeshNotLoadedVariable;
        standInDriver.Target.Target = invertDriver.State;
        standInDriver.DefaultValue.Value = true;
    }

    private static void EnsureDynamicVariableSpace(Slot root)
    {
        DynamicVariableSpace space = root.GetComponents<DynamicVariableSpace>()
            .FirstOrDefault(candidate => candidate.SpaceName.Value == DynamicVariableSpaceName);
        if (space == null)
        {
            space = root.AttachComponent<DynamicVariableSpace>();
            space.SpaceName.Value = DynamicVariableSpaceName;
            space.OnlyDirectBinding.Value = true;
        }
    }

    private static void PublishHeadPose(Slot root)
    {
        BipedRig rig = root.GetComponentsInChildren<BipedRig>()
            .FirstOrDefault(candidate => candidate.IsBiped && candidate[BodyNode.Head] != null);
        Slot head = rig?[BodyNode.Head];
        if (head == null)
        {
            UniLog.Warning($"{HeadBoneVariable} and {HeadPoseVariable} could not be configured because the head bone was not found.");
            return;
        }

        DynamicReferenceVariable<Slot> bone = head.AttachComponent<DynamicReferenceVariable<Slot>>();
        bone.VariableName.Value = HeadBoneVariable;
        bone.OverrideOnLink.Value = true;
        bone.Reference.Target = head;

        DynamicValueVariable<float4x4> pose = head.AttachComponent<DynamicValueVariable<float4x4>>();
        pose.VariableName.Value = HeadPoseVariable;
        pose.OverrideOnLink.Value = true;
        pose.Value.Value = head.LocalToGlobal;

        AvatarPoseNode poseNode = root.GetComponentsInChildren<AvatarPoseNode>()
            .FirstOrDefault(candidate => candidate.Node.Value == BodyNode.Head);
        if (poseNode != null)
        {
            ReferenceField<Slot> poseNodeReference = poseNode.Slot.AttachComponent<ReferenceField<Slot>>();
            poseNodeReference.Reference.Target = poseNode.Slot;
            DynamicReferenceVariable<Slot> poseNodeVariable =
                poseNode.Slot.AttachComponent<DynamicReferenceVariable<Slot>>();
            poseNodeVariable.VariableName.Value = AvatarPoseNodeHeadVariable;
            poseNodeVariable.Reference.DriveFrom(poseNodeReference.Reference);
        }
    }

    private static async Task<Slot> ImportLoadingStandIn(Slot parent)
    {
        Slot standIn = parent.AddSlot("LoadingSpinner");
        string tempPath = Path.Combine(Path.GetTempPath(),
            "ResoPon_LoadingStandIn_" + Guid.NewGuid().ToString("N") + ".resonitepackage");
        try
        {
            using System.IO.Stream resource = typeof(MeshLoadingSetup).Assembly.GetManifestResourceStream(
                "VrmToResonitePackage.Resources.loading_standin.resonitepackage");
            if (resource == null)
            {
                throw new InvalidOperationException("Loading stand-in resource was not found.");
            }

            using (FileStream file = File.Create(tempPath))
            {
                resource.CopyTo(file);
            }

            using RecordPackage package = RecordPackage.Decode(tempPath);
            SkyFrost.Base.Record record = package.MainRecord
                ?? throw new InvalidDataException("Loading stand-in package has no main record.");
            string signature = RecordPackage.GetAssetSignature(new Uri(record.AssetURI));
            using System.IO.Stream asset = package.ReadAsset(signature);
            DataTreeDictionary graph = DataTreeConverter.LoadAuto(asset)
                ?? throw new InvalidDataException("Loading stand-in package has no DataTree.");

            await ConvertPackdbReferencesToResdb(graph, package, parent.Engine);
            standIn.LoadObject(graph, record);
            standIn.ForeachComponentInChildren(delegate(IPackageImportEventReceiver receiver)
            {
                receiver.OnPackageImported();
            }, includeLocal: false, cacheItems: true);
            standIn.LocalRotation = floatQ.Identity;
            standIn.LocalScale = float3.One;

            DynamicReferenceVariableDriver<Slot> overrideParentDriver =
                standIn.GetComponentInChildren<DynamicReferenceVariableDriver<Slot>>();
            if (overrideParentDriver == null)
            {
                UniLog.Warning("LoadingStandin VirtualParent OverrideParent DV driver was not found.");
            }
            else
            {
                overrideParentDriver.VariableName.Value = AvatarPoseNodeHeadVariable;
            }
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
                // Temp cleanup is best-effort.
            }
        }
        return standIn;
    }

    /// <summary>
    /// Adds the supplied avatar grab/equip fix beneath the shared Avatar Settings root so its
    /// modular_avatar bindings resolve alongside the generated dynamic-bone controls.
    /// </summary>
    private static async Task ImportAvatarGrabEquip(Slot avatarRoot, Slot avatarSettings)
    {
        string tempPath = Path.Combine(Path.GetTempPath(),
            "ResoPon_AvatarGrabEquip_" + Guid.NewGuid().ToString("N") + ".resonitepackage");
        try
        {
            using (System.IO.Stream resource = typeof(MeshLoadingSetup).Assembly.GetManifestResourceStream(
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

            await ConvertPackdbReferencesToResdb(graph, package, avatarSettings.Engine);
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
                // Temp cleanup is best-effort.
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

    private static void PublishAvatarSettingsRoot(Slot avatarSettings)
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

    private static async Task ConvertPackdbReferencesToResdb(
        DataTreeDictionary graph, RecordPackage package, Engine engine)
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
                        $"Loading stand-in asset {sourceUri} could not be imported into LocalDB.");
                }
                importedAssets[signature] = importedUri;
                UniLog.Log($"Loading stand-in asset converted: {sourceUri} -> {importedUri}");
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
        throw new InvalidDataException("Loading stand-in contains an unsupported texture format.");
    }
}
