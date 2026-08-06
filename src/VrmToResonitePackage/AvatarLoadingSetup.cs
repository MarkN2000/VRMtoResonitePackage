using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using FrooxEngine.Store;
using Renderite.Shared;
using SkyFrost.Base;

namespace VrmToResonitePackage;

/// <summary>メッシュの読み込み完了まで Renderer を隠し、代替表示を出す。</summary>
internal static class AvatarLoadingSetup
{
    private const string MeshNotLoadedVariable = ModularAvatarSetup.SpaceName + "/MeshNotLoaded";
    private const string HeadBoneVariable = ModularAvatarSetup.SpaceName + "/HumanBone.head";
    private const string HeadPoseVariable = ModularAvatarSetup.SpaceName + "/HumanBonePose.head";
    private const string AvatarPoseNodeHeadVariable = ModularAvatarSetup.SpaceName + "/AvatarPoseNode.Head";

    public static async Task Apply(Slot root, Slot avatarSettings)
    {
        List<MeshRenderer> renderers = root.GetComponentsInChildren<MeshRenderer>()
            .Where(renderer => renderer.Mesh.Target != null)
            .ToList();
        if (renderers.Count == 0)
        {
            return;
        }

        ModularAvatarSetup.EnsureDynamicVariableSpace(root);
        ModularAvatarSetup.EnsureAvatarRootIdentification(root);
        AvatarSettingsSetup.PublishAvatarSettingsRoot(avatarSettings);
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
            using System.IO.Stream resource = typeof(AvatarLoadingSetup).Assembly.GetManifestResourceStream(
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

            await AvatarPackageImport.ConvertPackdbReferencesToResdb(
                graph, package, parent.Engine, "Loading stand-in");
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
                // 一時ファイルの削除失敗は変換結果に影響しない。
            }
        }
        return standIn;
    }
}
