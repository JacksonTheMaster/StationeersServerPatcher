# Stationeers Terrain/Mining Memory Leak - Root Cause Analysis
 
**Author:** JacksonTheMaster  
**Date:** January 31, 2026  
**Affected Versions:** Presumably all versions since the new LOD system was implemented  
**Severity:** Critical - will eventually crash servers/clients given enough time
 
---
 
## TL;DR for the busy dev
 
Unity `Mesh` objects are native resources. You're creating new ones when terrain changes but never calling `Destroy()` on the old ones. They pile up in memory until the game dies. Every single mining action leaks dozens of meshes across all LOD levels.
 
---


100mb of leaked memory without the patch:
![20260131175406_1](https://github.com/user-attachments/assets/52d46a4f-ca27-46c5-9195-32bd2dcf74ac)


400 kilobytes after I made this hole, which is likely not a leak but just the general cost to keep the changed terrain loaded. 
![20260131165752_1](https://github.com/user-attachments/assets/f463c447-33ca-41ca-9572-743b6040db55)

---
 
## How I found this
 
As you probably know, maintaining StationeersServerUi brings me the joy to take care of s bunch of users reporting various issues with basegame code I didn't write, and users kept complaining about memory usage climbing and climbing until the server eventually just... dies since the terrain update. Restarting fixes it temporarily, but give it a few hours of active play with mining and boom, same issue.
 
I finally sat down and decompiled the current game code to figure out what's going on. Started grepping for anything terrain/mining related and honestly I expected some complicated threading issue or something. Nope. Its way simpler than that, and also way more painful because of how simple the fix is.
 
---
 
## Who's affected?
 
| Mode | Affected? | Notes |
|------|-----------|-------|
| Dedicated Server | **YES - SEVERELY** | Servers run for days/weeks, memory accumulates until crash |
| Singleplayer | **YES** | Less noticable because sessions are shorter, but still leaks |
| Client (multiplayer) | **YES** | Same code runs on client for local terrain rendering |
 
The server gets hit hardest because:
1. Servers run continuously for extended periods
2. Multiple players mining = more terrain changes = faster leak
3. No natural "reset" from closing the game
 
---
 
## The Root Cause
 
Okay so here's the thing about Unity I had to wrap my head around (gotta admit Claude made this process of understanding a lot easier)
When you create a `Mesh` object with `new Mesh()`, you're allocating native (unmanaged) memory. This is NOT garbage collected automatically. You MUST call `Object.Destroy()` on it when you're done, or else that memory just... stays there. Forever. Until the process dies.
 
The terrain system creates meshes constantly. Every time you mine a voxel, the game needs to regenerate the mesh for that chunk of terrain. Actually not just one chunk - it dirties chunks across ALL 6 LOD levels, and because it also dirties neighbors, a single mine action can trigger regeneration of like... 27 chunks per level? So potentially 162+ meshes getting regenerated from ONE voxel change. Lol.
 
And here's the problem: **every single one of those old meshes gets orphaned without being destroyed.**
 
---
 
## The Leaky Code
 
### Leak #1: LodObject.ApplyMesh()
 
**File:** `TerrainSystem/Lods/LodObject.cs`  
**Method:** `ApplyMesh()` (around line 197)
 
```csharp
public void ApplyMesh()
{
    this._mesh = MeshCreatorHelper.CreateMesh(this._verts, this._normals, ...);
    if (this._mesh)
    {
        // ... setup code ...
        this.LodMeshRenderer.SetMesh(this._mesh, this.ShouldRenderMesh, VoxelTerrain.Instance.TerrainMaterial);
        // ...
    }
    // ...
}
```
 
See the problem already? `this._mesh` might already contain a mesh from the last time this LOD chunk was generated. When you do `this._mesh = MeshCreatorHelper.CreateMesh(...)`, the old mesh reference is just... gone. Overwritten. Never destroyed.
 
**Should be:**
```csharp
public void ApplyMesh()
{
    // DESTROY OLD MESH FIRST
    if (this._mesh != null)
    {
        Object.Destroy(this._mesh);
    }
 
    this._mesh = MeshCreatorHelper.CreateMesh(this._verts, this._normals, ...);
    // ... rest of method
}
```
 
### Leak #2: LodObject.OnReturnedToPool()
 
**File:** `TerrainSystem/Lods/LodObject.cs`  
**Method:** `OnReturnedToPool()` (around line 53-60)
 
```csharp
public void OnReturnedToPool()
{
    this.DeactivateLodMeshRenderer();
    this.ClearCollections();
    this._mesh = null;  // <-- THIS LINE RIGHT HERE
    this.Index = Vector3Int.zero;
    this.RequesterLookup.Clear();
    this.ShouldRenderMesh = false;
}
```
 
This one's almost comical. The code literally sets `_mesh = null` to "clean up" but never actually destroys the mesh. The mesh object is still sitting there in memory waiting to be freed, its just that nothing references it anymore. And since a Mesh is a native resource, the garbage collector doesn't care - it can't free that memory.
 
**Should be:**
```csharp
public void OnReturnedToPool()
{
    this.DeactivateLodMeshRenderer();
    this.ClearCollections();
 
    if (this._mesh != null)
    {
        Object.Destroy(this._mesh);
        this._mesh = null;
    }
 
    // ... rest
}
```
 
### Leak #3 & #4: LodMeshRenderer.SetMesh() and Clear()
 
**File:** `TerrainSystem/Lods/LodMeshRenderer.cs`
 
```csharp
public virtual void SetMesh(Mesh mesh, bool shouldRenderMesh, Material sharedMaterial)
{
    this._meshFilter.mesh = mesh;  // OLD MESH LEAKED
    this._meshRenderer.sharedMaterial = sharedMaterial;
    this._meshRenderer.enabled = shouldRenderMesh;
}
 
public void Clear()
{
    this._meshFilter.mesh = null;  // OLD MESH LEAKED
    this.IsDirty = false;
}
```
 
Same pattern. Assigning to `_meshFilter.mesh` just replaces the reference. The old mesh that was there? Gone. Leaked. Floating in memory purgatory.
 
Now here's where it gets a bit confusing and I had to actually think about this for a while - when you access `MeshFilter.mesh`, Unity will actually *instantiate* a copy of the mesh if it wasnt already an instance mesh. So theres some edge cases here. But regardless, the mesh being replaced (which was created by `MeshCreatorHelper.CreateMesh()`) is definitely a unique instance and definitely needs to be destroyed.
 
**Should be:**
```csharp
public virtual void SetMesh(Mesh mesh, bool shouldRenderMesh, Material sharedMaterial)
{
    if (this._meshFilter.mesh != null)
    {
        Object.Destroy(this._meshFilter.mesh);
    }
 
    this._meshFilter.mesh = mesh;
    this._meshRenderer.sharedMaterial = sharedMaterial;
    this._meshRenderer.enabled = shouldRenderMesh;
}
```
 
### Leak #5 & #6: LavaMesh.SetMesh() and Clear()
 
**File:** `TerrainSystem/Lods/LavaMesh.cs`
 
Exact same pattern as LodMeshRenderer. Less critical because lava meshes don't change afaik, but still a potential leak.
 
---
 
## Why This Is Bad
 
Let me do some napkin math here. I actually went through the code to get the (hopefully) correct numbers.

**Vertex size (from MeshCreatorHelper.CreateMesh):**

Each vertex stores 19 floats in the vertex buffer:
- Position: 3 floats (12 bytes)
- Normal: 3 floats (12 bytes)  
- Tangent: 4 floats (16 bytes)
- Color: 4 floats (16 bytes)
- TexCoord0: 2 floats (8 bytes)
- TexCoord1: 3 floats (12 bytes)

That's **76 bytes per vertex**. Plus theres another UV array allocated separately (8 bytes per vertex), and indices are UInt32 (4 bytes each, roughly 1 index per vertex for triangle meshes). So realistically we're looking at around **88-90 bytes per vertex** in total.

A marching cubes chunk after greedy meshing probably has somewhere between 200-2000 vertices depending on terrain complexity. Let's say 500 vertices average for a mixed terrain chunk:

500 vertices × 90 bytes = **~45KB per mesh** (give or take)

**Dirtying behavior (from LodManager.DirtyLods):**

When you mine a voxel, `DirtyLods(position, true)` is called. Looking at the code:

```csharp
public void DirtyLods(Vector3 position, bool dirtyNeighbours)
{
    for (int i = 0; i < 6; i++)  // ALL 6 LOD LEVELS
    {
        // ...
        if (dirtyNeighbours)
        {
            foreach (Vector3Int vector3Int2 in LodManager.LodNeighbourOffsets[i])
            {
                this.DirtyLod(vector3Int + vector3Int2, i);  // 27 neighbors per level
            }
        }
        // ...
    }
}
```

So..it marks 27 chunks dirty at EACH of the 6 LOD levels. That's 162 chunks marked dirty per mining action. However, only *active* chunks (ones that have requesters, actually get regenerated. The `DirtyLod` method only enqueues chunks where `LodObjectCache.TryGetActive` returns true.

In practice, a player mining nearby probably has maybe:
- LOD0: 5-15 active chunks in immediate vicinity
- LOD1: 3-8 active chunks
- LOD2-5: 1-4 active chunks each (larger chunks, fewer needed)

So realistically we're looking at maybe **15-40 mesh regenerations per mining action** depending on player position and terrain render distance settings.

**Conservative estimate:**

25 meshes × 45KB = **~1.1MB leaked per mined voxel**

On a server with multiple players? Each player adds to this independently.

---

## Real-World Validation

I implemented a Harmony patch to fix these leaks and added statistics tracking to measure the actual impact. Here's what 10 minutes of active mining produced:

| Metric | Value |
|--------|-------|
| Meshes Destroyed | **15,661** |
| Vertices Freed | **5,077,890** |
| Estimated Memory Freed | **~435 MB** |
| Time Period | 10 minutes |

That's an estimated **435 megabytes** of memory that would have leaked in just 10 minutes of one player mining - the top level soil is actually was worse than deeper levels - I guess it is differnet form the lower terrain - did not investigate this, just noticed a difference.

Let's work backwards from the real numbers:
- 5,077,890 vertices ÷ 15,661 meshes = **~324 vertices per mesh** (on average)
- 324 vertices × 90 bytes = **~29KB per mesh**
- 435MB ÷ 10 minutes = **~43.5 MB/minute** of leaked memory during active mining

This is the statistics for MkII mining drill LMB hold for about a sec:

- [StationeersServerPatcher] [TerrainMemoryLeak] === Patch Statistics ===
- [StationeersServerPatcher] [TerrainMemoryLeak] Meshes Destroyed: 90
- [StationeersServerPatcher] [TerrainMemoryLeak] Vertices Freed: 47502
- [StationeersServerPatcher] [TerrainMemoryLeak] Estimated Memory Freed: 4.08 MB
- [StationeersServerPatcher] [TerrainMemoryLeak] ApplyMesh Calls: 135
- [StationeersServerPatcher] [TerrainMemoryLeak] SetMesh Calls: 45

**Extrapolating to server runtime:**

| Scenario | Leak Rate | Time to 8GB |
|----------|-----------|-------------|
| 1 player mining actively | ~43 MB/min | ~3 hours |
| 2 players mining | ~86 MB/min | ~1.5 hours |
| 4 players mining | ~172 MB/min | ~45 minutes |

This definitely explains why servers with active players crash after a a few days due to a lack of more system ram to "waste". While idle servers can run indefinitel, the leak rate scales linearly with mining activity.

---

## The Fix
 
The fix is honestly embarassingly simple. Before assigning a new mesh, destroy the old one:
 
```csharp
if (oldMesh != null)
{
    Object.Destroy(oldMesh);
}
```
 
That's it. That's the whole fix. Add this check before every mesh assignment in the terrain system.
 
Specifically, these methods need the fix:
1. `LodObject.ApplyMesh()` - destroy `this._mesh` before creating new one
2. `LodObject.OnReturnedToPool()` - destroy `this._mesh` before nulling it
3. `LodMeshRenderer.SetMesh()` - destroy `this._meshFilter.mesh` before assigning new one
4. `LodMeshRenderer.Clear()` - destroy `this._meshFilter.mesh` before nulling it
5. `LavaMesh.SetMesh()` - same pattern
6. `LavaMesh.Clear()` - same pattern
 
I've already implemented a Harmony patch that does this as a prefix to each of these methods. Its in my StationeersServerPatcher mod if you want to see exactly how it works.
 
---
 
## Other Potential Issues I Noticed (but didn't fully investigate)
 
While I was poking around the terrain code I noticed a few other things that might be worth looking at:
 
1. **Vein.VeinsLookup** - This is a ConcurrentDictionary pre-allocated for 1,048,576 entries. Veins get added when terrain loads but individual veins are never removed when mined out - they just get marked as inactive. Over a very long session with lots of exploration, this could grow pretty large. Not as critical as the mesh leak but worth mentioning since I'm writing this anyway.
 
2. **LodObject.FinishedQueue** - Static queue that holds completed LOD jobs. If the main thread stalls while worker threads keep producing, this could theoretically grow unbounded. Probably not an issue in practice but worth a look.
 
3. **InstancedIndirectDrawCall buffers** - ComputeBuffers for instanced rendering. I saw a `Clear()` method that resets counts but doesnt release the buffers. The `ReleaseBuffers()` method exists but im not sure its ever called. Might be fine, might not be - didnt dig deep enough.
 
---
 
## (My) Conclusion
 
This seems to be a classic Unity gotcha that catches a lot of developers. Native resources need manual cleanup, the 
garbage collector won't save you.
 
The fix is straightforward - just add `Object.Destroy()` calls before overwriting mesh references. Should be like 20 lines of code total across all affected files.
 
Cheers,
Jackson
