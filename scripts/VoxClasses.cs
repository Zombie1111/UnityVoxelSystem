using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using System.Linq;

namespace zombVoxels
{
    public struct VoxObject
    {
        public NativeHashSet<int> voxs;
        public Vector3 xDirL;
        public Vector3 yDirL;
        public Vector3 zDirL;
        public Vector3 minL;
        public int vCountYZ;
        public int vCountZ;
        public byte voxType;

        public VoxObjectSaveable ToVoxObjectSaveable()
        {
            return new()
            {
                minL = minL,
                vCountYZ = vCountYZ,
                vCountZ = vCountZ,
                xDirL = xDirL,
                yDirL = yDirL,
                zDirL = zDirL,
                voxs = voxs.ToArray(),
                voxType = voxType
            };
        }

        [System.Serializable]
        public class VoxObjectSaveable
        {
            public int[] voxs;
            public Vector3 xDirL;
            public Vector3 yDirL;
            public Vector3 zDirL;
            public Vector3 minL;
            public int vCountYZ;
            public int vCountZ;
            public byte voxType;

            public VoxObject ToVoxObject()
            {
                return new()
                {
                    minL = minL,
                    vCountYZ = vCountYZ,
                    vCountZ = vCountZ,
                    xDirL = xDirL,
                    yDirL = yDirL,
                    zDirL = zDirL,
                    voxs = voxs.ToNativeHashSet(Allocator.Persistent),
                    voxType = voxType
                };
            }
        }
    }

    public unsafe struct VoxWorld
    {
        /// <summary>
        /// The status of each voxel (== 0 = air, == 1 = filled with defualt, > 1 = filled and value is typeIndex)
        /// </summary>
        public NativeArray<byte> voxs;

        /// <summary>
        /// The number of objectVoxels at each worldVoxel of type X (Use "typeIndex * voxWorld.vCount + voxelIndex" to get index)
        /// </summary>
        public NativeArray<byte> voxsTypes;

        public int vCountXYZ;
#if UNITY_EDITOR
        public int vCountY;
        public int vCountX;
#endif
        public int vCountZ;
        public int vCountZY;
    }

    [System.Serializable]
    public class VoxCollider
    {
        public Collider col;
        public int colId;
    }

    public unsafe struct VoxTransform
    {
        public Matrix4x4 prevLToW;
        public void* colIds_ptr;
        public int colIds_lenght;

        [System.Serializable]
        public class VoxTransformSavable
        {
            public List<int> colIds;
            public int transIndex;
        }
    }
}
