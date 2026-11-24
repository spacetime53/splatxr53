// Copyright (c) 2026 PSI Lab. MIT License. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatXR
{
    // Uploads a sorted splat mesh on the main thread and logs why it would fail to draw.
    // Unity's Mesh API is main-thread only. Calling it from Task.Run on Quest threw or no-op'd,
    // left the mesh empty, and the headset showed only the skybox.
    internal static class SplatMeshUploader
    {
        // 4 verts * 5 float3s + 4 float4 opacities + 6 int indices.
        const long BytesPerSplat = 328L;

        // Identity for "log once". Unity 6.5 makes Object.GetInstanceID a compile error
        // (use GetEntityId). HashSet equality uses the object itself, which is valid on
        // 2022.3, Unity 6.2, and 6.6. Do not store Object.GetHashCode: EntityId hashes are not unique.
        static readonly HashSet<MonoBehaviour> LoggedOwners = new HashSet<MonoBehaviour>();
        static float nextErrorLogTime;

        public static void Apply(
            MonoBehaviour owner,
            Mesh mesh,
            Vector3[] vertices,
            Vector3[] centerPositions,
            Vector3[] vertexColors,
            Vector4[] vertexOpacities,
            Vector3[] vertexCov0_3,
            Vector3[] vertexCov3_6,
            int[] indices,
            int loadedCount)
        {
            if (owner == null || mesh == null)
                return;

            int vertexCount = vertices != null ? vertices.Length : 0;
            try
            {
                // Clear() can drop back to 16-bit indices. 16-bit tops out at 65,535 vertices
                // (16,383 splats). Set UInt32 before and after the clear.
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.Clear();
                mesh.indexFormat = IndexFormat.UInt32;

                if (vertexCount > 0)
                {
                    mesh.vertices = vertices;
                    mesh.SetUVs(1, centerPositions);
                    mesh.SetUVs(2, vertexColors);
                    mesh.SetUVs(3, vertexOpacities);
                    mesh.SetUVs(5, vertexCov0_3);
                    mesh.SetUVs(6, vertexCov3_6);
                    mesh.SetIndices(indices, MeshTopology.Triangles, 0);
                    mesh.RecalculateBounds();
                }
            }
            catch (Exception exception)
            {
                LogErrorThrottled(owner, "Mesh upload threw, so the splat mesh is empty and will not draw. " +
                                         exception.GetType().Name + ": " + exception.Message);
                return;
            }

            var meshRenderer = owner.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                // Quad vertices sit on the origin; the shader places them from UV data.
                // Default mesh bounds are a couple of meters at the pivot, so the renderer
                // is frustum-culled and the headset shows only the skybox.
                meshRenderer.forceRenderingOff = false;
                meshRenderer.allowOcclusionWhenDynamic = false;
                meshRenderer.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            }

            LogOnce(owner, meshRenderer, vertexCount, loadedCount);

            if (loadedCount > 0 && vertexCount == 0)
            {
                LogErrorThrottled(owner, "Loaded " + loadedCount.ToString("N0") +
                                         " splats but the mesh has 0 vertices. Nothing will draw.");
            }
        }

        static void LogOnce(MonoBehaviour owner, MeshRenderer meshRenderer, int vertexCount, int loadedCount)
        {
            if (!LoggedOwners.Add(owner))
                return;

            var material = meshRenderer != null ? meshRenderer.sharedMaterial : null;
            var shader = material != null ? material.shader : null;
            bool supported = shader != null && shader.isSupported;
            long meshMb = vertexCount > 0 ? (vertexCount / 4L) * BytesPerSplat / (1024L * 1024L) : 0L;
            string cameraName = Camera.main != null ? Camera.main.name : "null";

            Debug.Log("[SplatXR] Mesh applied on '" + owner.name + "': loaded=" + loadedCount.ToString("N0") +
                      ", submittedSplats=" + (vertexCount / 4).ToString("N0") +
                      ", vertices=" + vertexCount.ToString("N0") +
                      ", meshMB~" + meshMb +
                      ", material=" + (material != null ? material.name : "null") +
                      ", shader=" + (shader != null ? shader.name : "null") +
                      ", shaderSupported=" + supported +
                      ", gfx=" + SystemInfo.graphicsDeviceType +
                      " (" + SystemInfo.graphicsDeviceName + ")" +
                      ", camera=" + cameraName + ".", owner);

            if (material == null || shader == null)
            {
                Debug.LogError("[SplatXR] '" + owner.name + "' has no material. The MeshRenderer needs a material using SplatXR/SplatShader.", owner);
            }
            else if (!supported)
            {
                Debug.LogError("[SplatXR] Shader '" + shader.name + "' is not supported on " +
                               SystemInfo.graphicsDeviceType + " (" + SystemInfo.graphicsDeviceName +
                               "). The splat will not draw. Search logcat for 'Shader' compiler errors.", owner);
            }

            if (shader != null && shader.name != "SplatXR/SplatShader")
            {
                Debug.LogError("[SplatXR] '" + owner.name + "' is using shader '" + shader.name +
                               "' instead of SplatXR/SplatShader, so the splats will not rasterize.", owner);
            }

            if (Camera.main == null)
            {
                Debug.LogWarning("[SplatXR] No camera is tagged MainCamera. Splats still draw, but they are depth-sorted from the world origin. Tag the XR camera MainCamera.", owner);
            }
        }

        static void LogErrorThrottled(MonoBehaviour owner, string message)
        {
            if (Time.realtimeSinceStartup < nextErrorLogTime)
                return;
            nextErrorLogTime = Time.realtimeSinceStartup + 2f;
            Debug.LogError("[SplatXR] " + message, owner);
        }
    }
}
