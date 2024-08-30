
<h1 align="center">UnityVoxelSystem by David Westberg</h1>

## Overview
A multithreaded voxel system for Unity designed to be an efficient way to access a rough reprisentation of the worlds geometry. The primary purpose of this project is to provide a easy way to run heavy tasks that require knowledge of the worlds geometry on a background thread. Its particularly useful for tasks like pathfinding and finding clear positions where some NPCs can spawn.

![Gif showing adding, creating and moving voxel objects](https://media.giphy.com/media/yaVuPs1yBckdq5tBh3/giphy.gif)

![Gif showing pathfinding](https://media.giphy.com/media/PvyUHqM5OGjFCEHIWR/giphy.gif)

## Key Features
<ul>
<li>Creates a voxelized version of all colliders in your scene</li>
<li>Multithreading and burst compiled code, thousands of colliders has minor cost</li>
<li>Safe read access to voxel data from any thread using unity JobSystem</li>
<li>Prebake voxelObjects in editor for faster load time</li>
<li>Baking, Adding and Removing voxelObjects at runtime</li>
<li>Identical colliders are stored as one voxelObject, saving both memory and baking time</li>
<li>All voxels can be in 255 different states, allowing you to categorize colliders and more</li>
</ul>

## Installaion
**Requirements** (Should work in other versions/render piplines)
<ul>
<li>Unity 2023.2.20f1 (Built-in)</li>
<li>Burst 1.8.17</li>
<li>Collections 2.1.4</li>
<li>Allow unsafe code (Because using pointers)</li>
</ul>

**General Setup**

<ol>
  <li>Download and copy the Resources, plugins, scripts, and _demo (optional) folders into an empty folder inside your Assets folder</li>
  <li>Create a new empty game object and add the VoxGlobalHandler script to it</li>
  <li>Make sure the worldspace bounding box of all colliders that should be voxelized are inside the yellow wireframe box. You can change the WorldScaleAxis parameter on the VoxGlobalHandler script to make the process easier or open the VoxGlobalSettings script and change the voxelSizeWorld and voxelAxisCount values</li>
  <li>Add the VoxParent script to game objects that will be cloned at runtime or if you want more configuration options</li>
  <li>Build the scene, Tools->Voxel System->Build Active Scene</li>
  <li>Enter playmode and a voxelized version of all colliders should exist, Tools->Voxel System->Toggle Draw Editor Voxels</li>
</ol>

## Documentation
Most parameters have tooltips in the unity inspector and a lot of the functions are commented.

See the `_demo` folder for pratical exampels

## Technical details
**VoxelObjects**

A voxelObject is a voxelized version of a collider, they are stored in a public dictorary and uses a hash generated from the collider type, bounds and vertex count as key. When baking a new collider a local voxel grid is created inside the collider worldspace bounding box. The index of all local voxels that are overlapping with the collider are added to the voxelObject (See `VoxelizeCollider()` in `scripts/VoxHelpFunc.cs`). The voxelObject stores the index of all overlapping local voxels and the local voxel grid dimensions. By only storing the index we can save 64 bits per overlapping voxel and still be able to get the world position of each voxel using the local grid dimensions and transform localToWorld matrix.

**Global Voxel Grid**

All voxelObjects are added to the global voxel grid, it consumes 24 bits per voxel. You can start reading the global voxel grid from any thread when GlobalReadAccessStart is invoked as long as you stop reading it immediately when GlobalReadAccessStop is invoked. The voxelObjects are added to the global voxel grid by converting the voxelObject to worldspace (See `ApplyVoxObjectToWorldVox()` in `scripts/VoxHelpFunc.cs`), this is done on a background thread.

**Execution Order**

![Image of execution order diagram](https://i.postimg.cc/Bvr7p1S2/image.png)

## License
The original code and assets created for this project are licensed under CC BY-NC 4.0 - See the `LICENSE` file for more details.

### Third-Party Libraries
This project uses third-party libraries located in the `plugins/` folder. These libraries are **not** covered by the CC BY-NC 4.0 license and are subject to their own respective licenses. Please refer to each library's license file in the `plugins/` folder for more information.
