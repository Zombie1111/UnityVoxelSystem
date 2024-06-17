using UnityEngine;
using Unity.Burst;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine.Jobs;
using Unity.Collections;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;

namespace zombVoxels
{
    [BurstCompile]
    public static class VoxHelpBurst
    {
        [BurstCompile]
        public static bool DoBoxOverlapTriangel(ref Vector3 tPosA, ref Vector3 tPosB, ref Vector3 tPosC, ref Bounds aabb)
        {
            float p0, p1, p2, r;

            Vector3 center = aabb.center, extents = aabb.max - center;

            Vector3 v0 = tPosA - center,
                v1 = tPosB - center,
                v2 = tPosC - center;

            Vector3 f0 = v1 - v0,
                f1 = v2 - v1,
                f2 = v0 - v2;

            Vector3 a00 = new Vector3(0, -f0.z, f0.y),
                a01 = new Vector3(0, -f1.z, f1.y),
                a02 = new Vector3(0, -f2.z, f2.y),
                a10 = new Vector3(f0.z, 0, -f0.x),
                a11 = new Vector3(f1.z, 0, -f1.x),
                a12 = new Vector3(f2.z, 0, -f2.x),
                a20 = new Vector3(-f0.y, f0.x, 0),
                a21 = new Vector3(-f1.y, f1.x, 0),
                a22 = new Vector3(-f2.y, f2.x, 0);

            // Test axis a00
            p0 = Vector3.Dot(v0, a00);
            p1 = Vector3.Dot(v1, a00);
            p2 = Vector3.Dot(v2, a00);
            r = extents.y * math.abs(f0.z) + extents.z * math.abs(f0.y);

            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a01
            p0 = Vector3.Dot(v0, a01);
            p1 = Vector3.Dot(v1, a01);
            p2 = Vector3.Dot(v2, a01);
            r = extents.y * math.abs(f1.z) + extents.z * math.abs(f1.y);

            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a02
            p0 = Vector3.Dot(v0, a02);
            p1 = Vector3.Dot(v1, a02);
            p2 = Vector3.Dot(v2, a02);
            r = extents.y * math.abs(f2.z) + extents.z * math.abs(f2.y);

            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a10
            p0 = Vector3.Dot(v0, a10);
            p1 = Vector3.Dot(v1, a10);
            p2 = Vector3.Dot(v2, a10);
            r = extents.x * math.abs(f0.z) + extents.z * math.abs(f0.x);
            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a11
            p0 = Vector3.Dot(v0, a11);
            p1 = Vector3.Dot(v1, a11);
            p2 = Vector3.Dot(v2, a11);
            r = extents.x * math.abs(f1.z) + extents.z * math.abs(f1.x);

            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a12
            p0 = Vector3.Dot(v0, a12);
            p1 = Vector3.Dot(v1, a12);
            p2 = Vector3.Dot(v2, a12);
            r = extents.x * math.abs(f2.z) + extents.z * math.abs(f2.x);

            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a20
            p0 = Vector3.Dot(v0, a20);
            p1 = Vector3.Dot(v1, a20);
            p2 = Vector3.Dot(v2, a20);
            r = extents.x * math.abs(f0.y) + extents.y * math.abs(f0.x);

            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a21
            p0 = Vector3.Dot(v0, a21);
            p1 = Vector3.Dot(v1, a21);
            p2 = Vector3.Dot(v2, a21);
            r = extents.x * math.abs(f1.y) + extents.y * math.abs(f1.x);

            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            // Test axis a22
            p0 = Vector3.Dot(v0, a22);
            p1 = Vector3.Dot(v1, a22);
            p2 = Vector3.Dot(v2, a22);
            r = extents.x * math.abs(f2.y) + extents.y * math.abs(f2.x);

            if (math.max(-FloatMaxThree(p0, p1, p2), FloatMinThree(p0, p1, p2)) > r)
            {
                return false;
            }

            if (FloatMaxThree(v0.x, v1.x, v2.x) < -extents.x || FloatMinThree(v0.x, v1.x, v2.x) > extents.x)
            {
                return false;
            }

            if (FloatMaxThree(v0.y, v1.y, v2.y) < -extents.y || FloatMinThree(v0.y, v1.y, v2.y) > extents.y)
            {
                return false;
            }

            if (FloatMaxThree(v0.z, v1.z, v2.z) < -extents.z || FloatMinThree(v0.z, v1.z, v2.z) > extents.z)
            {
                return false;
            }

            var normal = Vector3.Cross(f1, f0).normalized;
            var pl = new Plane(normal, Vector3.Dot(normal, tPosA));
            return DoBoxOverlapPlane(ref pl, ref aabb);
        }

        [BurstCompile]
        public static bool DoBoxOverlapPlane(ref Plane pl, ref Bounds aabb)
        {
            Vector3 center = aabb.center;
            var extents = aabb.max - center;

            var r = extents.x * math.abs(pl.normal.x) + extents.y * math.abs(pl.normal.y) + extents.z * math.abs(pl.normal.z);
            var s = Vector3.Dot(pl.normal, center) - pl.distance;

            return math.abs(s) <= r;
        }

        [BurstCompile]
        public static float FloatMaxThree(float a, float b, float c)
        {
            float max = a;

            if (b > max)
            {
                max = b;
            }

            if (c > max)
            {
                max = c;
            }

            return max;
        }

        [BurstCompile]
        public static float FloatMinThree(float a, float b, float c)
        {
            float min = a;

            if (b < min)
            {
                min = b;
            }

            if (c < min)
            {
                min = c;
            }

            return min;
        }

        [BurstCompile]
        public static void GetVoxelObjectPos(ref VoxWorld voxWorld,
            ref NativeHashSet<int> voxs, int vCountYZ, int vCountZ, ref Vector3 minW, ref Vector3 xDirW, ref Vector3 yDirW, ref Vector3 zDirW)
        {
            foreach (int vox in voxs)
            {
                int remainderAfterZ = vox % vCountYZ;
                Vector3 voxPos = minW + (xDirW * (vox / vCountYZ)) + (yDirW * (remainderAfterZ / vCountZ)) + (zDirW * (remainderAfterZ % vCountZ));
            }
        }

        [BurstCompile]
        public unsafe static void ApplyVoxObjectToWorldVox(
            ref VoxWorld voxWorld, ref NativeArray<byte> voxsCount, ref NativeArray<byte> voxsType, ref NativeArray<byte> voxsTypeOld,
            ref VoxObject voxObject, ref Matrix4x4 objLToWPrev, ref Matrix4x4 objLToWNow)
        {
            //Get voxs nativeArray from pointer
            NativeArray<int> voxs = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(
                                       voxObject.voxs_ptr, voxObject.voxs_lenght, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref voxs, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

            //Define veriabels used in compute
            int vCountYZ = voxObject.vCountYZ;
            int vCountZ = voxObject.vCountZ;
            int vwCountZ = voxWorld.vCountZ;
            int vwCountZY = voxWorld.vCountZY;

            Vector3 minW;
            Vector3 xDirW;
            Vector3 yDirW;
            Vector3 zDirW;

            byte thisType = voxObject.voxType;
            byte vType;
            byte vTypeO;
            Vector3 voxPos;
            int wvIndex;
            int remainderAfterZ;

            //Waint until no other thread is accessing voxObject
            //Write+Read to voxObject
            //Unlock voxObject to allow other thread to Write+Read if needed

            //Remove voxels from worldVoxels
            if (voxObject.isAppliedToWorld == true)
            {
                //Convert local voxel grid to prev worldSpace
                minW = objLToWPrev.MultiplyPoint3x4(voxObject.minL);
                xDirW = objLToWPrev.MultiplyVector(voxObject.xDirL) * VoxGlobalSettings.voxelSizeWorld;
                yDirW = objLToWPrev.MultiplyVector(voxObject.yDirL) * VoxGlobalSettings.voxelSizeWorld;
                zDirW = objLToWPrev.MultiplyVector(voxObject.zDirL) * VoxGlobalSettings.voxelSizeWorld;
                
                foreach (int vox in voxs)
                {
                    remainderAfterZ = vox % vCountYZ;
                    voxPos = minW + (xDirW * (vox / vCountYZ)) + (yDirW * (remainderAfterZ / vCountZ)) + (zDirW * (remainderAfterZ % vCountZ));

                    wvIndex = (int)(voxPos.z / VoxGlobalSettings.voxelSizeWorld)
                        + ((int)(voxPos.y / VoxGlobalSettings.voxelSizeWorld) * vwCountZ)
                        + ((int)(voxPos.x / VoxGlobalSettings.voxelSizeWorld) * vwCountZY);

                    voxsCount[wvIndex]--;

                    vType = voxsType[wvIndex];
                    vTypeO = voxsTypeOld[wvIndex];
                    if (thisType == vTypeO)
                    {
                        vTypeO = 0;
                        voxsTypeOld[wvIndex] = 0;
                    }

                    if (thisType == vType)
                    {
                        vType = vTypeO;
                        voxsType[wvIndex] = vTypeO;
                    }
                }
            }
            else voxObject.isAppliedToWorld = true;

            //Convert local voxel grid to now worldSpace
            minW = objLToWNow.MultiplyPoint3x4(voxObject.minL);
            xDirW = objLToWNow.MultiplyVector(voxObject.xDirL) * VoxGlobalSettings.voxelSizeWorld;
            yDirW = objLToWNow.MultiplyVector(voxObject.yDirL) * VoxGlobalSettings.voxelSizeWorld;
            zDirW = objLToWNow.MultiplyVector(voxObject.zDirL) * VoxGlobalSettings.voxelSizeWorld;

            //Add voxels to worldVoxels
            foreach (int vox in voxs)
            {
                remainderAfterZ = vox % vCountYZ;
                voxPos = minW + (xDirW * (vox / vCountYZ)) + (yDirW * (remainderAfterZ / vCountZ)) + (zDirW * (remainderAfterZ % vCountZ));

                wvIndex = (int)(voxPos.z / VoxGlobalSettings.voxelSizeWorld)
                    + ((int)(voxPos.y / VoxGlobalSettings.voxelSizeWorld) * vwCountZ)
                    + ((int)(voxPos.x / VoxGlobalSettings.voxelSizeWorld) * vwCountZY);

                voxsCount[wvIndex]++;
                if (voxsType[wvIndex] < thisType) voxsType[wvIndex] = thisType;

                vType = voxsTypeOld[wvIndex];
                if (vType == 0 || vType > thisType) voxsTypeOld[wvIndex] = thisType;
            }
        }
    }

    public static class VoxHelpFunc
    {
        /// <summary>
        /// Returns the value of xyz added togehter
        /// </summary>
        public static float TotalValue(this Vector3 vec)
        {
            return vec.x + vec.y + vec.z;
        }

        /// <summary>
        /// Voxelizes the given mesh, returns voxel positions in mesh localSpace. The voxels has the size defined in voxGlobalSettings.cs
        /// </summary>
        public static HashSet<Vector3> VoxelizeMesh(Vector3[] vers, int[] tris, Bounds meshBounds, Vector3 meshWorldScale)
        {
            //We could potentially improve performance by only allowing uniform scale, is it worth it?
            //Get voxel size and count
            HashSet<Vector3> voxelPoss = new();
            //Vector3 voxelSize = meshWorldScale * VoxGlobalSettings.voxelSizeWorld;
            Vector3 voxelSize = new(VoxGlobalSettings.voxelSizeWorld / meshWorldScale.x, VoxGlobalSettings.voxelSizeWorld / meshWorldScale.y, VoxGlobalSettings.voxelSizeWorld / meshWorldScale.z);
            Vector3 bSize = meshBounds.size + (voxelSize * 2.0f);
            int vCountX = Mathf.CeilToInt(bSize.x / voxelSize.x);
            int vCountY = Mathf.CeilToInt(bSize.y / voxelSize.y);
            int vCountZ = Mathf.CeilToInt(bSize.z / voxelSize.z);

            Vector3 bStart = meshBounds.min - voxelSize;
            int trisCount = tris.Length;
            Bounds voxBounds = new(Vector3.zero, voxelSize);

            //Get what voxels are overlapping and add them to hashset
            for (int x = 0; x < vCountX; x++)
            {
                for (int y = 0; y < vCountY; y++)
                {
                    for (int z = 0; z < vCountZ; z++)
                    {
                        bool doOverlap = false;
                        var voxPos = Vector3.Scale(new Vector3(x, y, z), voxelSize) + bStart;
                        voxBounds.center = voxPos;

                        for (int tI = 0; tI < trisCount; tI += 3)
                        {
                            if (VoxHelpBurst.DoBoxOverlapTriangel(ref vers[tris[tI]], ref vers[tris[tI + 1]], ref vers[tris[tI + 2]], ref voxBounds) == false) continue;

                            doOverlap = true;
                            break;
                        }

                        if (doOverlap == false) continue;

                        voxelPoss.Add(voxPos);
                    }
                }
            }

            return voxelPoss;
        }

        /// <summary>
        /// Voxelizes the given collider, returns voxel positions in collider transform localSpace. The voxels has the size defined in voxGlobalSettings.cs
        /// </summary>
        public static VoxObject.VoxObjectSaveable VoxelizeCollider(Collider col, byte colVoxType = 0)
        {
            Vector3 voxelSize = VoxGlobalSettings.voxelSizeWorld * Vector3.one;
            Bounds colBounds = col.bounds;
            Matrix4x4 colWToL = col.transform.worldToLocalMatrix;

            Vector3 bSize = colBounds.size + (voxelSize * 2.0f);
            int vCountX = Mathf.CeilToInt(bSize.x / voxelSize.x);
            int vCountY = Mathf.CeilToInt(bSize.y / voxelSize.y);
            int vCountZ = Mathf.CeilToInt(bSize.z / voxelSize.z);

            Vector3 bStart = colBounds.min - voxelSize;
            Collider[] hitCols = new Collider[8];
            Quaternion colRot = Quaternion.identity;
            LayerMask layerMask = 1 << col.gameObject.layer;
            PhysicsScene colPhyScene = col.gameObject.scene.GetPhysicsScene();

            voxelSize *= 0.5f;

            HashSet<int> voxs = new(128);
            int nextVoxId = 0;

            for (int x = 0; x < vCountX; x++)
            {
                for (int y = 0; y < vCountY; y++)
                {
                    for (int z = 0; z < vCountZ; z++)
                    {
                        var voxPos = (new Vector3(x, y, z) * VoxGlobalSettings.voxelSizeWorld) + bStart;

                        int hitCount = colPhyScene.OverlapBox(voxPos, voxelSize, hitCols, colRot, layerMask, QueryTriggerInteraction.Ignore);

                        for (int i = 0; i < hitCount; i++)
                        {
                            if (hitCols[i] != col) continue;

                            //voxelPoss.Add(colWToL.MultiplyPoint3x4(voxPos));
                            //voxs.Add(nextVoxId);
                            voxs.Add(nextVoxId);
                            break;
                        }

                        nextVoxId++;
                    }
                }
            }

            bSize.x *= colWToL.lossyScale.x;
            bSize.y *= colWToL.lossyScale.y;
            bSize.z *= colWToL.lossyScale.z;

            return new()
            {
                //voxs = voxs.ToNativeHashSet(Allocator.Persistent),
                voxs = voxs.ToArray(),
                vCountYZ = vCountY * vCountZ,
                vCountZ = vCountZ,
                xDirL = colWToL.MultiplyVector(Vector3.right),
                yDirL = colWToL.MultiplyVector(Vector3.up),
                zDirL = colWToL.MultiplyVector(Vector3.forward),
                minL = colWToL.MultiplyPoint3x4(bStart),
                voxType = colVoxType,
                objIndex = -1//We assign this later when we actually add the real voxelObject
            };
        }

        /// <summary>
        /// Converts a HashSet<int> to a NativeHashSet<int>.
        /// </summary>
        public static NativeHashSet<int> ToNativeHashSet(this HashSet<int> hashSet, Allocator allocator)
        {
            NativeHashSet<int> nativeHashSet = new(hashSet.Count, allocator);
            foreach (var item in hashSet)
            {
                nativeHashSet.Add(item);
            }

            return nativeHashSet;
        }

        /// <summary>
        /// Converts a int[] to a NativeHashSet<int>.
        /// </summary>
        public static NativeHashSet<int> ToNativeHashSet(this int[] array, Allocator allocator)
        {
            NativeHashSet<int> nativeHashSet = new(array.Length, allocator);
            foreach (var item in array)
            {
                nativeHashSet.Add(item);
            }

            return nativeHashSet;
        }

        /// <summary>
        /// Converts a int[] to a NativeArray<int>.
        /// </summary>
        public static NativeArray<int> ToNativeArray(this int[] array, Allocator allocator)
        {
            NativeArray<int> nativeArray = new(array.Length, allocator);
            for (int i = 0; i < array.Length; i++)
            {
                nativeArray[i] = array[i];
            }

            return nativeArray;
        }

        /// <summary>
        /// Converts a List<int> to a NativeArray<int>.
        /// </summary>
        public static NativeArray<int> ToNativeArray(this List<int> array, Allocator allocator)
        {
            NativeArray<int> nativeArray = new(array.Count, allocator);
            for (int i = 0; i < array.Count; i++)
            {
                nativeArray[i] = array[i];
            }

            return nativeArray;
        }

        /// <summary>
        /// Converts a NativeHashSet<int> to a HashSet<int>.
        /// </summary>
        public static HashSet<int> ToHashSet(this NativeHashSet<int> nativeHashSet)
        {
            HashSet<int> hashSet = new(nativeHashSet.Count);
            foreach (var item in nativeHashSet)
            {
                hashSet.Add(item);
            }

            return hashSet;
        }

        /// <summary>
        /// Returns a int that is unique for all colliders that are different (Mesh colliders useses bounds and vertexCount to get id)
        /// </summary>
        public static int GetColId(this Collider col, byte objType)
        {
            int colId = 17;
            Vector3 bExtents = col.bounds.extents;

            unchecked
            {
                colId = colId * 31 + bExtents.x.GetHashCode();
                colId = colId * 31 + bExtents.y.GetHashCode();
                colId = colId * 31 + bExtents.z.GetHashCode();
                colId = colId * 31 + objType.GetHashCode();
            }

            if (col is MeshCollider mCol)
            {
                unchecked
                {
                    colId = colId * 31 + mCol.sharedMesh.vertexCount.GetHashCode();
                }
            }
            else if (col is CapsuleCollider cCol)
            {
                unchecked
                {
                    colId = colId * 34 + cCol.center.x.GetHashCode();
                    colId = colId * 31 + cCol.center.y.GetHashCode();
                    colId = colId * 31 + cCol.center.z.GetHashCode();
                }
            }
            else if (col is BoxCollider bCol)
            {
                unchecked
                {
                    colId = colId * 31 + bCol.center.x.GetHashCode();
                    colId = colId * 31 + bCol.center.y.GetHashCode();
                    colId = colId * 31 + bCol.center.z.GetHashCode();
                }
            }
            else if (col is SphereCollider sCol)
            {
                unchecked
                {
                    colId = colId * 37 + sCol.center.x.GetHashCode();
                    colId = colId * 31 + sCol.center.y.GetHashCode();
                    colId = colId * 31 + sCol.center.z.GetHashCode();
                }
            }
            else if (col is WheelCollider wCol)
            {
                unchecked
                {
                    colId = colId * 39 + wCol.center.x.GetHashCode();
                    colId = colId * 31 + wCol.center.y.GetHashCode();
                    colId = colId * 31 + wCol.center.z.GetHashCode();
                }
            }

            return colId;
        }

        /// <summary>
        /// Returns the VoxSavedData asset, returns null if it has been deleted
        /// </summary>
        public static VoxSavedData TryGetVoxelSaveAsset()
        {
            VoxSavedData savedVoxData = Resources.Load<VoxSavedData>("VoxSavedData");
            if (savedVoxData == null)
            {
                Debug.LogError("Expected VoxSavedData.asset to exist at path _voxelSystem/Resources/VoxSavedData.asset, have you deleted it?");
                return null;
            }

            return savedVoxData;
        }
    }
}


