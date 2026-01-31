using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// StationeersServerPatcher - Stationeers Dedicated Server Patches
// Copyright (c) 2025 JacksonTheMaster
// All rights reserved.
// https://github.com/SteamServerUI/StationeersServerUI

namespace StationeersServerPatcher.Patches
{
    /// <summary>
    /// Patches to fix critical memory leaks in the terrain/LOD system.
    /// 
    /// The game creates new Unity Mesh objects when terrain is modified (e.g., mining)
    /// but never destroys the old meshes, causing severe memory leaks over time.
    /// 
    /// Affected methods:
    /// - LodObject.ApplyMesh() - creates new mesh without destroying old one
    /// - LodObject.OnReturnedToPool() - nulls mesh reference without destroying
    /// - LodMeshRenderer.SetMesh() - assigns new mesh without destroying old one
    /// - LodMeshRenderer.Clear() - nulls mesh without destroying
    /// </summary>
    public static class TerrainMemoryLeakStats
    {
        public static long MeshesDestroyed = 0;
        public static long VerticesFreed = 0;
        public static long ApplyMeshCalls = 0;
        public static long SetMeshCalls = 0;
        
        public static void LogStats()
        {
            StationeersServerPatcher.LogInfo($"[TerrainMemoryLeak] Stats - Meshes Destroyed: {MeshesDestroyed}, Vertices Freed: {VerticesFreed}, ApplyMesh: {ApplyMeshCalls}, SetMesh: {SetMeshCalls}");
        }
        
        public static void Reset()
        {
            MeshesDestroyed = 0;
            VerticesFreed = 0;
            ApplyMeshCalls = 0;
            SetMeshCalls = 0;
        }
        
        public static long EstimatedMemoryFreedBytes => VerticesFreed * 90;
        public static string EstimatedMemoryFreed => $"{EstimatedMemoryFreedBytes / 1024.0 / 1024.0:F2} MB";
    }

    [HarmonyPatch]
    public static class LodObjectApplyMeshPatch
    {
        private static FieldInfo _meshField;

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            if (!PluginConfig.IsTerrainMemoryLeakPatchEnabled)
            {
                StationeersServerPatcher.LogInfo("[TerrainMemoryLeak] Patch is disabled, skipping LodObject.ApplyMesh.");
                return null;
            }

            var lodObjectType = AccessTools.TypeByName("TerrainSystem.Lods.LodObject");
            if (lodObjectType == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find LodObject type.");
                return null;
            }

            _meshField = AccessTools.Field(lodObjectType, "_mesh");
            if (_meshField == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find _mesh field on LodObject.");
            }

            var method = AccessTools.Method(lodObjectType, "ApplyMesh");
            if (method == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find ApplyMesh method.");
                return null;
            }

            StationeersServerPatcher.LogInfo("[TerrainMemoryLeak] Found LodObject.ApplyMesh for patching.");
            return method;
        }

        /// <summary>
        /// Before ApplyMesh creates a new mesh, destroy the old one to prevent memory leak.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            try
            {
                TerrainMemoryLeakStats.ApplyMeshCalls++;
                if (_meshField == null) return;

                var oldMesh = _meshField.GetValue(__instance) as Mesh;
                if (oldMesh != null)
                {
                    TerrainMemoryLeakStats.MeshesDestroyed++;
                    TerrainMemoryLeakStats.VerticesFreed += oldMesh.vertexCount;
                    UnityEngine.Object.Destroy(oldMesh);
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"[TerrainMemoryLeak] Error in LodObject.ApplyMesh prefix: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    public static class LodObjectOnReturnedToPoolPatch
    {
        private static FieldInfo _meshField;

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            if (!PluginConfig.IsTerrainMemoryLeakPatchEnabled)
                return null;

            var lodObjectType = AccessTools.TypeByName("TerrainSystem.Lods.LodObject");
            if (lodObjectType == null)
            {
                return null;
            }

            _meshField = AccessTools.Field(lodObjectType, "_mesh");

            var method = AccessTools.Method(lodObjectType, "OnReturnedToPool");
            if (method == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find OnReturnedToPool method.");
                return null;
            }

            StationeersServerPatcher.LogInfo("[TerrainMemoryLeak] Found LodObject.OnReturnedToPool for patching.");
            return method;
        }

        /// <summary>
        /// Before OnReturnedToPool nulls the mesh, destroy it to prevent memory leak.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            try
            {
                if (_meshField == null) return;

                var oldMesh = _meshField.GetValue(__instance) as Mesh;
                if (oldMesh != null)
                {
                    UnityEngine.Object.Destroy(oldMesh);
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"[TerrainMemoryLeak] Error in LodObject.OnReturnedToPool prefix: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    public static class LodMeshRendererSetMeshPatch
    {
        private static FieldInfo _meshFilterField;

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            if (!PluginConfig.IsTerrainMemoryLeakPatchEnabled)
                return null;

            var lodMeshRendererType = AccessTools.TypeByName("TerrainSystem.Lods.LodMeshRenderer");
            if (lodMeshRendererType == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find LodMeshRenderer type.");
                return null;
            }

            _meshFilterField = AccessTools.Field(lodMeshRendererType, "_meshFilter");
            if (_meshFilterField == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find _meshFilter field on LodMeshRenderer.");
            }

            var method = AccessTools.Method(lodMeshRendererType, "SetMesh", new[] { typeof(Mesh), typeof(bool), typeof(Material) });
            if (method == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find SetMesh method.");
                return null;
            }

            StationeersServerPatcher.LogInfo("[TerrainMemoryLeak] Found LodMeshRenderer.SetMesh for patching.");
            return method;
        }

        /// <summary>
        /// Before SetMesh assigns a new mesh, destroy the old one to prevent memory leak.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            try
            {
                TerrainMemoryLeakStats.SetMeshCalls++;
                if (_meshFilterField == null) return;

                var meshFilter = _meshFilterField.GetValue(__instance) as MeshFilter;
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    // Note: This mesh may already be destroyed by LodObjectApplyMeshPatch
                    // Unity's "fake null" will make this check return false in that case
                    TerrainMemoryLeakStats.MeshesDestroyed++;
                    TerrainMemoryLeakStats.VerticesFreed += meshFilter.mesh.vertexCount;
                    UnityEngine.Object.Destroy(meshFilter.mesh);
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"[TerrainMemoryLeak] Error in LodMeshRenderer.SetMesh prefix: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    public static class LodMeshRendererClearPatch
    {
        private static FieldInfo _meshFilterField;

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            if (!PluginConfig.IsTerrainMemoryLeakPatchEnabled)
                return null;

            var lodMeshRendererType = AccessTools.TypeByName("TerrainSystem.Lods.LodMeshRenderer");
            if (lodMeshRendererType == null)
            {
                return null;
            }

            _meshFilterField = AccessTools.Field(lodMeshRendererType, "_meshFilter");

            var method = AccessTools.Method(lodMeshRendererType, "Clear");
            if (method == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find LodMeshRenderer.Clear method.");
                return null;
            }

            StationeersServerPatcher.LogInfo("[TerrainMemoryLeak] Found LodMeshRenderer.Clear for patching.");
            return method;
        }
        
        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            try
            {
                if (_meshFilterField == null) return;

                var meshFilter = _meshFilterField.GetValue(__instance) as MeshFilter;
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    UnityEngine.Object.Destroy(meshFilter.mesh);
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"[TerrainMemoryLeak] Error in LodMeshRenderer.Clear prefix: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    public static class LavaMeshSetMeshPatch
    {
        private static FieldInfo _meshFilterField;

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            if (!PluginConfig.IsTerrainMemoryLeakPatchEnabled)
                return null;

            var lavaMeshType = AccessTools.TypeByName("TerrainSystem.Lods.LavaMesh");
            if (lavaMeshType == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find LavaMesh type.");
                return null;
            }

            _meshFilterField = AccessTools.Field(lavaMeshType, "_meshFilter");
            if (_meshFilterField == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find _meshFilter field on LavaMesh.");
            }

            var method = AccessTools.Method(lavaMeshType, "SetMesh", new[] { typeof(Mesh) });
            if (method == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find LavaMesh.SetMesh method.");
                return null;
            }

            StationeersServerPatcher.LogInfo("[TerrainMemoryLeak] Found LavaMesh.SetMesh for patching.");
            return method;
        }

        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            try
            {
                if (_meshFilterField == null) return;

                var meshFilter = _meshFilterField.GetValue(__instance) as MeshFilter;
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    UnityEngine.Object.Destroy(meshFilter.mesh);
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"[TerrainMemoryLeak] Error in LavaMesh.SetMesh prefix: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    public static class LavaMeshClearPatch
    {
        private static FieldInfo _meshFilterField;

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            if (!PluginConfig.IsTerrainMemoryLeakPatchEnabled)
                return null;

            var lavaMeshType = AccessTools.TypeByName("TerrainSystem.Lods.LavaMesh");
            if (lavaMeshType == null)
            {
                return null;
            }

            _meshFilterField = AccessTools.Field(lavaMeshType, "_meshFilter");

            var method = AccessTools.Method(lavaMeshType, "Clear");
            if (method == null)
            {
                StationeersServerPatcher.LogWarning("[TerrainMemoryLeak] Could not find LavaMesh.Clear method.");
                return null;
            }

            StationeersServerPatcher.LogInfo("[TerrainMemoryLeak] Found LavaMesh.Clear for patching.");
            return method;
        }

        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            try
            {
                if (_meshFilterField == null) return;

                var meshFilter = _meshFilterField.GetValue(__instance) as MeshFilter;
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    UnityEngine.Object.Destroy(meshFilter.mesh);
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"[TerrainMemoryLeak] Error in LavaMesh.Clear prefix: {ex.Message}");
            }
        }
    }
}
