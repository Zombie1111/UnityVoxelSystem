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
using System;
using Unity.VisualScripting.Antlr3.Runtime.Tree;

namespace zombVoxels
{
    [BurstCompile]
    public static class VoxHelpBurst
    {
        /// <summary>
        /// Applies the given voxObject to the world voxelGrid,
        /// only voxsCount, voxsType, voxsTypeOld and voxTrans is written to inside the function
        /// </summary>
        [BurstCompile]
        public unsafe static void ApplyVoxObjectToWorldVox(
            ref VoxWorld voxWorld, ref NativeArray<byte> voxsCount, ref NativeArray<byte> voxsType, ref NativeArray<byte> voxsTypeOld,
            ref VoxObject voxObject, ref Matrix4x4 objLToWPrev, ref Matrix4x4 objLToWNow, ref VoxTransform voxTrans, bool unapplyOnly = false)
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
            int vwCountZY = voxWorld.vCountYZ;

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

            //Unapply voxels from worldVoxels
            if (voxTrans.isAppliedToWorld == true)
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

                    if (thisType == vType && (vTypeO > 0 || voxsCount[wvIndex] == 0))
                    {
                        vType = vTypeO;
                        voxsType[wvIndex] = vTypeO;
                    }
                }
            }
            else voxTrans.isAppliedToWorld = true;

            if (voxTrans.isActive == false || unapplyOnly == true)
            {
                voxTrans.isAppliedToWorld = false;
                return;
            }

            //Note: objLToWNow is not garanteed to be valid if unapplyOnly is true or trans is inactive
            //Convert local voxel grid to now worldSpace
            minW = objLToWNow.MultiplyPoint3x4(voxObject.minL);
            xDirW = objLToWNow.MultiplyVector(voxObject.xDirL) * VoxGlobalSettings.voxelSizeWorld;
            yDirW = objLToWNow.MultiplyVector(voxObject.yDirL) * VoxGlobalSettings.voxelSizeWorld;
            zDirW = objLToWNow.MultiplyVector(voxObject.zDirL) * VoxGlobalSettings.voxelSizeWorld;

            //Apply voxels to worldVoxels
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

        [BurstCompile]
        public static void WVoxIndexToPos(ref int wvoxIndex, ref Vector3 resultPos, ref VoxWorld voxWorld)
        {
            int remainderAfterZ = wvoxIndex % voxWorld.vCountYZ;
            resultPos = new Vector3(wvoxIndex / voxWorld.vCountYZ, remainderAfterZ / voxWorld.vCountZ, remainderAfterZ % voxWorld.vCountZ) * VoxGlobalSettings.voxelSizeWorld;
        }

        [BurstCompile]
        public static void PosToWVoxIndex(ref Vector3 pos, ref int resultWVoxIndex, ref VoxWorld voxWorld)
        {
            //Its half a voxel off, only solution is to round to int or offset result but not worth performance cost?
            resultWVoxIndex = (int)(pos.z / VoxGlobalSettings.voxelSizeWorld)
                + ((int)(pos.y / VoxGlobalSettings.voxelSizeWorld) * voxWorld.vCountZ)
                + ((int)(pos.x / VoxGlobalSettings.voxelSizeWorld) * voxWorld.vCountYZ);
        }

        [BurstCompile]
        public static void GetVoxelCountBetweenWVoxIndexs(int voxA, int voxB, ref ushort resultCount, ref VoxWorld voxWorld)
        {
            //resultCount = (ushort)math.abs((voxA % voxWorld.vCountZ) - (voxB % voxWorld.vCountZ));
            //voxA /= voxWorld.vCountZ;
            //voxB /= voxWorld.vCountZ;
            //resultCount += (ushort)(math.abs((voxA % voxWorld.vCountY) - (voxB % voxWorld.vCountY))
            //    + math.abs((voxA / voxWorld.vCountY) - (voxB / voxWorld.vCountY)));

            //Get Manhattan distance
            int tempReminderA = voxA % (voxWorld.vCountY * voxWorld.vCountZ);
            int tempReminderB = voxB % (voxWorld.vCountY * voxWorld.vCountZ);

            resultCount = (ushort)(
                math.abs((voxA / (voxWorld.vCountY * voxWorld.vCountZ)) - (voxB / (voxWorld.vCountY * voxWorld.vCountZ)))
                + math.abs((tempReminderA / voxWorld.vCountZ) - (tempReminderB / voxWorld.vCountZ))
                + math.abs((tempReminderA % voxWorld.vCountZ) - (tempReminderB % voxWorld.vCountZ))
                );
        }

        [BurstCompile]
        public static void GetAllSolidVoxels(ref NativeArray<byte> voxTypes, ref VoxWorld vWorld, ref NativeList<int> result)
        {
            int vCount = vWorld.vCountXYZ;
            for (int i = 0; i < vCount; i++)
            {
                if (voxTypes[i] <= VoxGlobalSettings.solidStart) continue;

                result.Add(i);
            }
        }

        public struct CustomVoxelData
        {
            public Vector3 pos;
            public byte colorI;
        }

        [BurstCompile]
        public static void GetAllVoxelDataWithinRadius(ref Vector3 center, float radius, ref NativeArray<byte> voxTypes, ref VoxWorld vWorld, ref NativeList<CustomVoxelData> result)
        {
            int vCount = vWorld.vCountXYZ;
            int vType;
            float radiusSqr = radius * radius;
            Vector3 vPos;

            for (int i = 0; i < vCount; i++)
            {
                vType = voxTypes[i];
                if (vType == 0) continue;

                int remainderAfterZ = i % vWorld.vCountYZ;
                vPos = new Vector3(i / vWorld.vCountYZ, remainderAfterZ / vWorld.vCountZ, remainderAfterZ % vWorld.vCountZ) * VoxGlobalSettings.voxelSizeWorld;
                
                if ((vPos - center).sqrMagnitude > radiusSqr) continue;
                result.Add(new()
                {
                    pos = vPos,
                    colorI = (byte)Math.Clamp(vType / 4, 0, 63)
                });
            }
        }
    }

    public static class VoxHelpFunc
    {
        /// <summary>
        /// Returns a NativeHashMap with the given allocator containing the same KeyValue pairs as the SerializableDictionary
        /// </summary>
        public static NativeHashMap<TKey, TValue> ToNativeHashMap<TKey, TValue>(this SerializableDictionary<TKey, TValue> dic, Allocator allocator)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            var newNat = new NativeHashMap<TKey, TValue>(dic.Count, allocator);

            foreach (var kvp in dic)
            {
                newNat.Add(kvp.Key, kvp.Value);
            }

            return newNat;
        }

        /// <summary>
        /// Returns the value of xyz added togehter
        /// </summary>
        public static float TotalValue(this Vector3 vec)
        {
            return vec.x + vec.y + vec.z;
        }

        /// <summary>
        /// Voxelizes the given collider, returns voxel positions in collider transform localSpace. The voxels has the size defined in voxGlobalSettings.cs
        /// </summary>
        public static VoxObject.VoxObjectSaveable VoxelizeCollider(Collider col, byte colVoxType = VoxGlobalSettings.solidStart + 1)
        {
            Vector3 voxelSize = VoxGlobalSettings.voxelSizeWorld * Vector3.one;
            Bounds colBounds = col.bounds;
            Matrix4x4 colWToL = col.transform.worldToLocalMatrix;

            Vector3 bSize = colBounds.size + (voxelSize * 2.0f);
            int vCountX = (int)Math.Ceiling(bSize.x / voxelSize.x);
            int vCountY = (int)Math.Ceiling(bSize.y / voxelSize.y);
            int vCountZ = (int)Math.Ceiling(bSize.z / voxelSize.z);

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
                //objIndex = -1//We assign this later when we actually add the real voxelObject
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
                colId = colId * 31 + (int)Math.Round(bExtents.x * 1000);
                colId = colId * 31 + (int)Math.Round(bExtents.y * 1000);
                colId = colId * 31 + (int)Math.Round(bExtents.z * 1000);
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


