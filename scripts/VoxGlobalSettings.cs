using UnityEngine;

namespace zombVoxels
{
    public static class VoxGlobalSettings
    {
        //The size of each voxel in worldSpace (Extents)
        public const float voxelSizeWorld = 0.5f;

        //How many voxels there are in the scene in total
        public const int voxelAxisCount = 200;

        //How many different types of voxels that can exist
        public const byte voxelTypeCount = 4;
    }
}

