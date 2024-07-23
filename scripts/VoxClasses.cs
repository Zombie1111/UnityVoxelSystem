using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using System.Linq;
using System.Runtime.InteropServices;

namespace zombVoxels
{
    public unsafe struct VoxObject
    {
        public void* voxs_ptr;
        public int voxs_lenght;
        public Vector3 xDirL;
        public Vector3 yDirL;
        public Vector3 zDirL;
        public Vector3 minL;
        public int vCountYZ;
        public int vCountZ;
        public byte voxType;

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
        }
    }

    public unsafe struct VoxWorld
    {
        public int vCountXYZ;
#if UNITY_EDITOR
        public int vCountY;//Its only used in editor to visualize the voxel grid
        public int vCountX;
#endif
        public int vCountZ;
        public int vCountYZ;
    }

    [System.Serializable]
    public class VoxCollider
    {
        public Collider col;
        public int colId;
        public byte colType;
    }

    public unsafe struct VoxTransform
    {
        public void* colIds_ptr;
        public int colIds_lenght;
        [MarshalAs(UnmanagedType.U1)] public bool isAppliedToWorld;

        [System.Serializable]
        public class VoxTransformSavable
        {
            public List<int> colIds;
            public int transIndex;
        }

        public struct ToCompute
        {
            public Matrix4x4 prevLToW;
            public Matrix4x4 nowLToW;
            public int transIndex;
        }
    }
}
