//https://github.com/Zombie1111/UnityVoxelSystem
using UnityEngine;
using Unity.Burst;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using System;
using Unity.Mathematics;

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

            int maxVoxI = voxWorld.vCountXYZ - 1;
            float worldMaxX = voxWorld.vCountX * VoxGlobalSettings.voxelSizeWorld;
            float worldMaxY = voxWorld.vCountY * VoxGlobalSettings.voxelSizeWorld;
            float worldMaxZ = voxWorld.vCountZ * VoxGlobalSettings.voxelSizeWorld;
            float invVoxelSizeWorld = 1.0f / VoxGlobalSettings.voxelSizeWorld;

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

                    if (voxPos.x < 0 || voxPos.y < 0 || voxPos.z < 0//Prevent out of bounds (Accurate, no warping)
                        || voxPos.x > worldMaxX || voxPos.y > worldMaxY || voxPos.z > worldMaxZ) continue;

                    wvIndex = (int)(voxPos.z * invVoxelSizeWorld)
                        + ((int)(voxPos.y * invVoxelSizeWorld) * vwCountZ)
                        + ((int)(voxPos.x * invVoxelSizeWorld) * vwCountZY);

                    //if (wvIndex < 0 || wvIndex > maxVoxI) continue;//Prevent out of bounds (Fast, warping)

                    voxsCount[wvIndex]--;
                    if (voxsCount[wvIndex] == 0)
                    {
                        voxsType[wvIndex] = 0;
                        voxsTypeOld[wvIndex] = 0;
                        continue;
                    }

                    vType = voxsType[wvIndex];
                    vTypeO = voxsTypeOld[wvIndex];
                    if (thisType == vTypeO)
                    {
                        if (vType < vTypeO || voxsCount[wvIndex] == 1) voxsTypeOld[wvIndex] = vType;
                        continue;
                    }

                    if (thisType == vType && vTypeO > 0)
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

            ////Check if out of bounds
            //Vector3 maxW = minW + xDirW + yDirW + zDirW;
            //Vector3 worldMax = new Vector3(voxWorld.vCountX, voxWorld.vCountY, voxWorld.vCountZ) * VoxGlobalSettings.voxelSizeWorld;
            //
            //if (minW.x < 0 || minW.y < 0 || minW.z < 0
            //    || maxW.x > worldMax.x || maxW.y > worldMax.y || maxW.z > worldMax.z
            //    
            //    || maxW.x < 0 || maxW.y < 0 || maxW.z < 0//Min is not garanteed to be min in worldspace
            //    || minW.x > worldMax.x || minW.y > worldMax.y || minW.z > worldMax.z)
            //{
            //    voxTrans.isAppliedToWorld = false;
            //    return;
            //}

            //Apply voxels to worldVoxels
            foreach (int vox in voxs)
            {
                remainderAfterZ = vox % vCountYZ;
                voxPos = minW + (xDirW * (vox / vCountYZ)) + (yDirW * (remainderAfterZ / vCountZ)) + (zDirW * (remainderAfterZ % vCountZ));

                if (voxPos.x < 0 || voxPos.y < 0 || voxPos.z < 0//Prevent out of bounds (Accurate, no warping)
                    || voxPos.x > worldMaxX || voxPos.y > worldMaxY || voxPos.z > worldMaxZ) continue;

                wvIndex = (int)(voxPos.z * invVoxelSizeWorld)
                    + ((int)(voxPos.y * invVoxelSizeWorld) * vwCountZ)
                    + ((int)(voxPos.x * invVoxelSizeWorld) * vwCountZY);

                //if (wvIndex < 0 || wvIndex > maxVoxI) continue;//Prevent out of bounds (Fast, warping)

                voxsCount[wvIndex]++;
                vType = voxsType[wvIndex];
                if (vType < thisType) voxsType[wvIndex] = thisType;
                if (voxsCount[wvIndex] == 1) continue;

                vTypeO = voxsTypeOld[wvIndex];
                if (vTypeO == 0 || vTypeO > thisType) voxsTypeOld[wvIndex] = vType > thisType ? thisType : vType;
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
                if (voxTypes[i] <= VoxGlobalSettings.solidTypeStart) continue;

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
        /// Clamps the bounding box so its fits inside boundsB - safeDis
        /// </summary>
        public static void Clamp(ref this Bounds boundsA, Bounds boundsB, float safeDis = 0.0f)
        {
            Vector3 maxA = boundsA.max;
            Vector3 minA = boundsA.min;
            Vector3 maxB = boundsB.max - (Vector3.one * safeDis);
            Vector3 minB = boundsB.min + (Vector3.one * safeDis);

            if (maxA.x > maxB.x) maxA.x = maxB.x;
            if (maxA.y > maxB.y) maxA.y = maxB.y;
            if (maxA.z > maxB.z) maxA.z = maxB.z;

            if (minA.x < minB.x) minA.x = minB.x;
            if (minA.y < minB.y) minA.y = minB.y;
            if (minA.z < minB.z) minA.z = minB.z;

            boundsA.SetMinMax(minA, maxA);
        }

        /// <summary>
        /// Voxelizes the given collider, returns voxel positions in collider transform localSpace. The voxels has the size defined in voxGlobalSettings.cs
        /// </summary>
        public static VoxObject.VoxObjectSaveable VoxelizeCollider(Collider col, ref VoxWorld vWorld, byte colVoxType = VoxGlobalSettings.defualtType)
        {
            Vector3 voxelSize = VoxGlobalSettings.voxelSizeWorld * Vector3.one;
            Bounds colBounds = col.bounds;
            Vector3 voxelGridSize = new Vector3(vWorld.vCountX, vWorld.vCountY, vWorld.vCountZ) * VoxGlobalSettings.voxelSizeWorld;
            colBounds.Clamp(new Bounds(voxelGridSize * 0.5f, voxelGridSize), VoxGlobalSettings.voxelSizeWorld * 2.0f);
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
        /// Returns N number of points evenly distributed on a flat circle with the given center, normal and radius
        /// </summary>
        public static List<Vector3> GetRandomPointsInCircle(int n, Vector3 center, Vector3 normal, float radius, float alpha = 0, bool geodesic = false)
        {
            float phi = (1 + Mathf.Sqrt(5)) / 2; //golden ratio
            float angle_stride = 360f * phi;
            int b = (int)(alpha * Mathf.Sqrt(n));  //number of boundary points
            List<Vector3> points = new();
            Vector3 tangent = Vector3.Cross(normal, Vector3.up).normalized;

            if (tangent == Vector3.zero) //In case normal is directly up or down
            {
                tangent = Vector3.Cross(normal, Vector3.forward).normalized;
            }

            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

            for (int k = 0; k < n; k++)
            {
                float r = k > n - b ? 1 : Mathf.Sqrt(k - 0.5f) / Mathf.Sqrt(n - (b + 1) / 2);
                float theta = geodesic ? k * 360f * phi : k * angle_stride;
                points.Add(center + ((r * Mathf.Cos(theta * Mathf.Deg2Rad) * radius) * tangent + (r * Mathf.Sin(theta * Mathf.Deg2Rad) * radius) * bitangent));
            }

            return points;
        }

        public static List<Vector3> GetRandomConeDirections(int n, Vector3 center, Vector3 normal, float radius, float alpha = 0, bool geodesic = false)
        {
            //float phi = (1 + Mathf.Sqrt(5)) / 2; // golden ratio
            //float angle_stride = 360f * phi;
            //
            //int b = (int)(alpha * Mathf.Sqrt(n));  // number of boundary points
            //
            //List<Vector3> points = new();
            //
            //// Get two vectors perpendicular to the normal
            //Vector3 tangent = Vector3.Cross(normal, Vector3.up).normalized;
            //if (tangent == Vector3.zero) // In case normal is directly up or down
            //{
            //    tangent = Vector3.Cross(normal, Vector3.forward).normalized;
            //}
            //Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            //
            //for (int k = 0; k < n; k++)
            //{
            //    float r = k > n - b ? 1 : Mathf.Sqrt(k - 0.5f) / Mathf.Sqrt(n - (b + 1) / 2);
            //    float theta = geodesic ? k * 360f * phi : k * angle_stride;
            //    float x = r * Mathf.Cos(theta * Mathf.Deg2Rad) * radius;
            //    float y = r * Mathf.Sin(theta * Mathf.Deg2Rad) * radius;
            //
            //    // Convert the 2D point to 3D
            //    points.Add((x * tangent + y * bitangent).normalized);
            //}
            //
            //return points;

            List<Vector3> dirs = GetRandomPointsInCircle(n, center + normal, normal, radius, alpha, geodesic);
            
            for (int i = 0; i < n; i++)
            {
                dirs[i] = (dirs[i] - center).normalized;
            }

            dirs.Add(normal);

            return dirs;
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
                if (mCol.sharedMesh == null)
                {
                    Debug.LogWarning(mCol.transform.name + " has a MeshCollider with no mesh assigned to it");
                    return colId;
                }

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

        /// <summary>
        /// Returns the closest point on a line between lineStart and lineEnd 
        /// </summary>
        public static Vector3 ClosestPointOnLine(Vector3 lineStart, Vector3 lineEnd, ref Vector3 point)
        {
            Vector3 lineDirection = lineEnd - lineStart;
            float lineLength = lineDirection.magnitude;
            lineDirection.Normalize();
            return lineStart + Math.Clamp(Vector3.Dot(point - lineStart, lineDirection), 0.0f, lineLength) * lineDirection;
        }

        /// <summary>
        /// Returns the distance between A and B in the given direction
        /// </summary>
        public static float GetDistanceInDirection(Vector3 posA, Vector3 posB, Vector3 direction)
        {
            return Mathf.Abs(Vector3.Dot(posB - posA, direction));
        }

        /// <summary>
        /// Spherecast from pos in the given direction with the highest radius that fits,
        /// Returns RaycastHit with null collider and distance = rayLenght + radius if nothing is hit
        /// </summary>
        public static RaycastHit WideRaycast(Vector3 pos, Vector3 dir, float rayLenght, LayerMask mask, float maxRadius, float radiusShrink = 0.05f)
        {
            float radiusMulti = 1.0f;
            float radius = maxRadius;
            while (Physics.CheckSphere(pos, radius, mask, QueryTriggerInteraction.Ignore) == true && radiusMulti > 0.0f)
            {
                radiusMulti -= radiusShrink;
                radius = maxRadius * radiusMulti;
            }

            radiusMulti -= radiusShrink * 0.5f;//Always shrink bit more before cast
            radius = maxRadius * radiusMulti;

            if (Physics.SphereCast(pos, radius, dir, out RaycastHit nHit, rayLenght, mask, QueryTriggerInteraction.Ignore) == false)
            {
                nHit.distance = rayLenght + radius;
                nHit.point = pos + (dir * nHit.distance);
            }

            return nHit;
        }

        /// <summary>
        /// Spherecast from pos in the given direction with the highest radius that fits,
        /// Returns RaycastHit with null collider and distance = rayLenght + radius if nothing is hit
        /// </summary>
        public static bool WideRaycastOut(Vector3 pos, Vector3 dir, out RaycastHit hit, float rayLenght, LayerMask mask, float maxRadius, float radiusShrink = 0.05f)
        {
            float radiusMulti = 1.0f;
            float radius = maxRadius;
            while (Physics.CheckSphere(pos, radius, mask, QueryTriggerInteraction.Ignore) == true && radiusMulti > 0.0f)
            {
                radiusMulti -= radiusShrink;
                radius = maxRadius * radiusMulti;
            }

            radiusMulti -= radiusShrink * 0.5f;//Always shrink bit more before cast
            radius = maxRadius * radiusMulti;

            return Physics.SphereCast(pos, radius, dir, out hit, rayLenght, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Spherecast from pos to end with the highest radius that fits,
        /// Returns RaycastHit with null collider and distance = rayLenght + radius if nothing is hit
        /// </summary>
        public static RaycastHit WideLinecast(Vector3 pos, Vector3 end, LayerMask mask, float maxRadius, float radiusShrink = 0.05f)
        {
            float radiusMulti = 1.0f;
            float radius = maxRadius;
            while (Physics.CheckSphere(pos, radius, mask, QueryTriggerInteraction.Ignore) == true && radiusMulti > 0.0f)
            {
                radiusMulti -= radiusShrink;
                radius = maxRadius * radiusMulti;
            }

            radiusMulti -= radiusShrink * 0.5f;//Always shrink bit more before cast
            radius = maxRadius * radiusMulti;

            end -= pos;
            if (Physics.SphereCast(pos, radius, end.normalized, out RaycastHit nHit, end.magnitude, mask, QueryTriggerInteraction.Ignore) == false)
            {
                nHit.distance = end.magnitude + radius;
                nHit.point = pos + (end.normalized * nHit.distance);
            }

            return nHit;
        }

        /// <summary>
        /// Returns the position and normal of the closest surface found, returns input if no surface found
        /// </summary>
        public static bool GetClosestSurface(Vector3 pos, Vector3 nor, float maxRadius, float radiusShrink, float maxDis, LayerMask mask, int resolution, out Vector3 surfacePos, out Vector3 surfaceNor, bool ignoreOpposite = false)
        {
            //Get radius to use
            float radiusMulti = 1.0f;
            float radius = maxRadius;
            while (Physics.CheckSphere(pos, radius, mask, QueryTriggerInteraction.Ignore) == true && radiusMulti > 0.0f)
            {
                radiusMulti -= radiusShrink;
                radius = maxRadius * radiusMulti;
            }

            radiusMulti -= radiusShrink * 0.5f;//Always shrink bit more before cast
            radius = maxRadius * radiusMulti;

            //Cast rays
            float bestDis = float.MaxValue;
            surfacePos = pos;
            surfaceNor = nor;
            bool didFind = false;

            if (ignoreOpposite == true) resolution *= 2;

            foreach (Vector3 dir in GetSphereDirections(resolution))
            {
                if (ignoreOpposite == true && Vector3.Dot(dir, nor) < 0.0f) continue;

                if (Physics.SphereCast(pos, radius, dir, out RaycastHit nHit, maxDis, mask, QueryTriggerInteraction.Ignore) == false) continue;
                if (nHit.distance > bestDis) continue;

                bestDis = nHit.distance;
                surfacePos = nHit.point;
                surfaceNor = nHit.normal;
                didFind = true;
            }

            return didFind;

            //float bestDis = float.MaxValue;
            //surfacePos = pos;
            //surfaceNor = nor;
            //
            //foreach (Vector3 dir in GetSphereDirections(resolution))
            //{
            //    if (Physics.Raycast(pos, dir, out RaycastHit nHit, maxDis, mask, QueryTriggerInteraction.Ignore) == false) continue;
            //    if (nHit.distance > bestDis) continue;
            //
            //    bestDis = nHit.distance;
            //    surfacePos = nHit.point;
            //    surfaceNor = nHit.normal;
            //}
        }

        /// <summary>
        /// Returns the position and normal of the closest surface found, returns input if no surface found
        /// </summary>
        public static void GetClosestSurfaceTo(Vector3 pos, Vector3 nor, Vector3 to, float maxDis, float maxRadius, float radiusShrink,
            LayerMask mask, int resolution, out Vector3 surfacePos, out Vector3 surfaceNor)
        {
            //Get radius to use
            float radiusMulti = 1.0f;
            float radius = maxRadius;
            while (Physics.CheckSphere(pos, radius, mask, QueryTriggerInteraction.Ignore) == true && radiusMulti > 0.0f)
            {
                radiusMulti -= radiusShrink;
                radius = maxRadius * radiusMulti;
            }

            radiusMulti -= radiusShrink * 0.5f;//Always shrink bit more before cast
            radius = maxRadius * radiusMulti;

            //Cast rays
            float bestDis = float.MaxValue;
            surfacePos = pos;
            surfaceNor = nor;

            foreach (Vector3 dir in GetSphereDirections(resolution))
            {
                if (Physics.SphereCast(pos, radius, dir, out RaycastHit nHit, maxDis, mask, QueryTriggerInteraction.Ignore) == false) continue;
                float dis = (nHit.point - to).magnitude;
                if (dis > bestDis) continue;

                bestDis = dis;
                surfacePos = nHit.point;
                surfaceNor = nHit.normal;
            }
        }

        /// <summary>
        /// Returns a array containing evenly distributed directions with the largest possible avg difference between each direction
        /// </summary>
        public static Vector3[] GetSphereDirections(int directionCount)
        {
            Vector3[] directions = new Vector3[directionCount];

            float goldenRatio = (1 + Mathf.Sqrt(5)) / 2;
            float angleIncrement = Mathf.PI * 2 * goldenRatio;

            for (int i = 0; i < directionCount; i++)
            {
                float inclination = Mathf.Acos(1 - 2 * ((float)i / directionCount));
                float azimuth = angleIncrement * i;

                directions[i] = new Vector3(Mathf.Sin(inclination) * Mathf.Cos(azimuth), Mathf.Sin(inclination) * Mathf.Sin(azimuth), Mathf.Cos(inclination));
            }

            return directions;
        }

        /// <summary>
        /// Returns the rotation with specified right and up direction   
        /// May have to make more error catches here. Whatif not orthogonal?
        /// </summary>
        public static Quaternion LookRotationRight(Vector3 right, Vector3 up)
        {
            if (up == Vector3.zero || right == Vector3.zero) return Quaternion.identity;
            // If vectors are parallel return identity
            float angle = Vector3.Angle(right, up);
            if (angle == 0 || angle == 180) return Quaternion.identity;
            Vector3 forward = Vector3.Cross(right, up);
            return Quaternion.LookRotation(forward, up);
        }

        /// <summary>
        /// Returns the position on the bezier curve at T (Like Vector3.Lerp but curve instead of linear)
        /// </summary>
        public static Vector3 GetPointOnCurve(ref Vector3 start, Vector3 startOffset,
            ref Vector3 end, Vector3 endOffset, float t)
        {
            startOffset = start + startOffset;
            endOffset = end + endOffset;//We do this inside the function since I always expect offset to be a direction

            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            Vector3 result = uuu * start; // (1-t)^3 * p0
            result += 3 * uu * t * startOffset; // 3 * (1-t)^2 * t * p1
            result += 3 * u * tt * endOffset; // 3 * (1-t) * t^2 * p2
            result += ttt * end; // t^3 * p3
            return result;
        }

        /// <summary>
        /// Returns a random direction inside a cone (Not uniform, more likely to be closer to center)
        /// </summary>
        public static Vector3 RandomConeDirection(Vector3 dir, float radius)
        {
            var angle = UnityEngine.Random.value * Math.PI * 2;
            var rndRadius = UnityEngine.Random.value * radius;
            float x = (float)(rndRadius * Math.Cos(angle));
            float y = (float)(rndRadius * Math.Sin(angle));

            GetSideAndUpDir(dir, out Vector3 uDir, out Vector3 sDir);
            return (dir + (uDir * x) + (sDir * y)).normalized;
        }

        /// <summary>
        /// Returns two dirs that togehter with given forward dir forms an orthonormal basis
        /// </summary>
        public static void GetSideAndUpDir(Vector3 forward, out Vector3 up, out Vector3 side)
        {
            //Get valid world up dir
            Vector3 arbitraryUp = Vector3.up;

            if (Mathf.Abs(Vector3.Dot(forward, arbitraryUp)) > 0.999f)
            {
                arbitraryUp = Vector3.right;
            }

            //Get local side and up dir
            side = Vector3.Cross(forward, arbitraryUp).normalized;
            up = Vector3.Cross(side, forward).normalized;
        }
    }
}


