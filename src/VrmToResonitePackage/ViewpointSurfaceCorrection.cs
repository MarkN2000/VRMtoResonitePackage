using Elements.Assets;
using Elements.Core;
using FrooxEngine;

namespace VrmToResonitePackage;

/// <summary>現在の視点から正面にある最初のメッシュ面まで、視点を前方へ補正する。</summary>
internal static class ViewpointSurfaceCorrection
{
    private const float SurfaceClearance = 0.002f;
    private const float ParallelEpsilon = 0.00000001f;
    private const float ForwardDistanceEpsilon = 0.00001f;
    private const float BarycentricEpsilon = 0.00001f;

    public static async Task<float3> Apply(Slot root, float3 origin, float3 forward)
    {
        float3 direction = forward.Normalized;
        List<RendererSnapshot> renderers = CaptureRenderers(root);
        if (renderers.Count == 0)
        {
            return origin;
        }

        float nearestDistance = float.PositiveInfinity;
        var failures = new List<string>();

        await default(ToBackground);
        foreach (RendererSnapshot renderer in renderers)
        {
            try
            {
                MeshX mesh = await CopyMesh(renderer.Asset).ConfigureAwait(false);
                if (mesh == null)
                {
                    continue;
                }

                float distance = FindNearestIntersection(mesh, renderer, origin, direction);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{renderer.Name}: {ex.Message}");
            }
        }
        await default(ToWorld);

        foreach (string failure in failures)
        {
            UniLog.Warning($"視点のメッシュ表面補正で Renderer を処理できませんでした: {failure}");
        }

        if (!float.IsFinite(nearestDistance))
        {
            UniLog.Log("視点の前方にメッシュ面がないため、表面補正は行いませんでした。");
            return origin;
        }

        float correctedDistance = nearestDistance + SurfaceClearance;
        UniLog.Log($"視点を前方のメッシュ表面まで {correctedDistance:F4} m 補正しました。");
        return origin + direction * correctedDistance;
    }

    private static List<RendererSnapshot> CaptureRenderers(Slot root)
    {
        var result = new List<RendererSnapshot>();
        Slot worldSpace = root.World.RootSlot;

        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            try
            {
                FrooxEngine.Mesh asset = renderer.Mesh.Asset;
                if (!renderer.Slot.IsActive || !renderer.ShouldBeEnabled || asset?.Data == null)
                {
                    continue;
                }

                float4x4[] boneTransforms = Array.Empty<float4x4>();
                float[] blendShapeWeights = Array.Empty<float>();
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    boneTransforms = skinned.GetBoneTransforms(worldSpace);
                    blendShapeWeights = new float[Math.Max(0, skinned.MeshBlendshapeCount)];
                    int count = Math.Min(blendShapeWeights.Length, skinned.BlendShapeWeights.Count);
                    for (int i = 0; i < count; i++)
                    {
                        blendShapeWeights[i] = skinned.BlendShapeWeights[i];
                    }
                }

                result.Add(new RendererSnapshot(
                    renderer.Slot.Name ?? renderer.GetType().Name,
                    asset,
                    renderer.Slot.LocalToGlobal,
                    boneTransforms,
                    blendShapeWeights));
            }
            catch (Exception ex)
            {
                UniLog.Warning($"視点のメッシュ表面補正で Renderer を取得できませんでした: " +
                               $"{renderer.Slot.Name ?? renderer.GetType().Name}: {ex.Message}");
            }
        }

        return result;
    }

    private static async Task<MeshX> CopyMesh(FrooxEngine.Mesh asset)
    {
        object readLock = new();
        await asset.RequestReadLock(readLock).ConfigureAwait(false);
        try
        {
            return asset.Data == null ? null : new MeshX(asset.Data);
        }
        finally
        {
            asset.ReleaseReadLock(readLock);
        }
    }

    private static float FindNearestIntersection(MeshX mesh, RendererSnapshot renderer,
        float3 origin, float3 direction)
    {
        float3[] positions = TransformVertices(mesh, renderer);
        float nearest = float.PositiveInfinity;

        foreach (Triangle triangle in mesh.Triangles)
        {
            int i0 = triangle.Vertex0IndexUnsafe;
            int i1 = triangle.Vertex1IndexUnsafe;
            int i2 = triangle.Vertex2IndexUnsafe;
            if ((uint)i0 >= positions.Length || (uint)i1 >= positions.Length || (uint)i2 >= positions.Length)
            {
                continue;
            }

            if (TryIntersectTriangle(origin, direction, positions[i0], positions[i1], positions[i2],
                    out float distance) && distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }

    private static float3[] TransformVertices(MeshX mesh, RendererSnapshot renderer)
    {
        float3[] source = mesh.RawPositions;
        var positions = new float3[source.Length];
        bool applyBlendShapes = renderer.BlendShapeWeights.Length > 0 && mesh.BlendShapeCount > 0;
        bool applySkinning = mesh.HasBoneBindings && renderer.BoneTransforms.Length > 0;

        for (int i = 0; i < source.Length; i++)
        {
            float3 position = source[i];
            float3 normal = float3.Zero;
            float4 tangent = default;

            if (applyBlendShapes)
            {
                mesh.TransformVertexByBlendshapes(i, ref position, ref normal, ref tangent,
                    renderer.BlendShapeWeights);
            }

            if (applySkinning)
            {
                mesh.TransformVertexByBones(i, ref position, ref normal, ref tangent,
                    renderer.BoneTransforms);
            }
            else
            {
                position = renderer.LocalToGlobal.TransformPoint(position);
            }

            positions[i] = position;
        }

        return positions;
    }

    private static bool TryIntersectTriangle(float3 origin, float3 direction,
        float3 vertex0, float3 vertex1, float3 vertex2, out float distance)
    {
        float3 edge1 = vertex1 - vertex0;
        float3 edge2 = vertex2 - vertex0;
        float3 p = MathX.Cross(direction, edge2);
        float determinant = Dot(edge1, p);
        if (MathF.Abs(determinant) < ParallelEpsilon)
        {
            distance = 0f;
            return false;
        }

        float inverse = 1f / determinant;
        float3 offset = origin - vertex0;
        float u = Dot(offset, p) * inverse;
        if (u < -BarycentricEpsilon || u > 1f + BarycentricEpsilon)
        {
            distance = 0f;
            return false;
        }

        float3 q = MathX.Cross(offset, edge1);
        float v = Dot(direction, q) * inverse;
        if (v < -BarycentricEpsilon || u + v > 1f + BarycentricEpsilon)
        {
            distance = 0f;
            return false;
        }

        distance = Dot(edge2, q) * inverse;
        return distance > ForwardDistanceEpsilon;
    }

    private static float Dot(float3 left, float3 right)
        => left.x * right.x + left.y * right.y + left.z * right.z;

    private sealed record RendererSnapshot(
        string Name,
        FrooxEngine.Mesh Asset,
        float4x4 LocalToGlobal,
        float4x4[] BoneTransforms,
        float[] BlendShapeWeights);
}
