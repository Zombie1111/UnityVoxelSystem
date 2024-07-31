using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.VisualScripting.FullSerializer;
using UnityEditor;
using UnityEngine;

namespace zombVoxels
{
    public class VoxPathfinder : MonoBehaviour
    {
        #region Configuration

        [SerializeField] private int maxVoxelsToSearch = 1000;
        [SerializeField] private int maxVoxelsSearched = 32000;
        [SerializeField] private SerializableDictionary<byte, float> voxTypeCostMultiplier = new();

        #endregion Configuration


        #region SetupPathfinder

        private VoxGlobalHandler voxHandler;

        private void Start()
        {
            voxHandler = VoxGlobalHandler.TryGetValidGlobalHandler();
            if (voxHandler == null) return;

            SetupPathfindJob();

            //Subscribe to event
            voxHandler.OnGlobalReadAccessStart += OnGlobalReadAccessStart;
            voxHandler.OnGlobalReadAccessStop += OnGlobalReadAccessStop;
        }

        private void OnDestroy()
        {
            ClearAllocatedMemory();

            if (voxHandler == null) return;

            //Unsubscrive from event
            voxHandler.OnGlobalReadAccessStart -= OnGlobalReadAccessStart;
            voxHandler.OnGlobalReadAccessStop -= OnGlobalReadAccessStop;
        }

        private void SetupPathfindJob()
        {
            fp_job = new()
            {
                _activeRequest = new(Allocator.Persistent),
                _voxsSearched = new(maxVoxelsSearched, Allocator.Persistent),
                _voxsType = voxHandler.cvo_job.voxsType.AsReadOnly(),
                _voxWorld = voxHandler.cvo_job.voxWorld.AsReadOnly(),
                _toSearchValues = new(maxVoxelsToSearch, Allocator.Persistent),
                _toSearchIndex = new(maxVoxelsToSearch, Allocator.Persistent),
                _maxVoxelsSearched = new(maxVoxelsSearched, Allocator.Persistent),
                _voxTypeToMultiply = voxTypeCostMultiplier.ToNativeHashMap(Allocator.Persistent),
                _resultPath = new(maxVoxelsToSearch / 100, Allocator.Persistent)
            };
        }

        private void ClearAllocatedMemory()
        {
            OnGlobalReadAccessStop();

            if (fp_job._activeRequest.IsCreated == true) fp_job._activeRequest.Dispose();
            if (fp_job._toSearchIndex.IsCreated == true) fp_job._toSearchIndex.Dispose();
            if (fp_job._toSearchValues.IsCreated == true) fp_job._toSearchValues.Dispose();
            if (fp_job._voxsSearched.IsCreated == true) fp_job._voxsSearched.Dispose();
            if (fp_job._maxVoxelsSearched.IsCreated == true) fp_job._maxVoxelsSearched.Dispose();
            if (fp_job._voxTypeToMultiply.IsCreated == true) fp_job._voxTypeToMultiply.Dispose();
            if (fp_job._resultPath.IsCreated == true) fp_job._resultPath.Dispose();
        }

        #endregion SetupPathfinder




        #region HandlePathRequest

        [System.Serializable]
        public struct PathRequest
        {
            [System.NonSerialized] public int startVoxIndex;
            [System.NonSerialized] public int endVoxIndex;
            public byte radius;
            public byte snapRadius;
            public PathType pathType;
        }

        public struct PathPendingRequest
        {
            public int startVoxIndex;
            public int endVoxIndex;
            public byte radius;
            public byte snapRadius;
            public PathType pathType;
            public int requestId;
        }

        public enum PathType
        {
            walk = 0,
            climbing = 1,
            flying = 2
        }

        private Queue<PathPendingRequest> pendingRequests = new();
        private HashSet<int> discardedPendingRequests = new();
        private int nextRequestId = 0;

        /// <summary>
        /// Requests a path with the given properties and returns the id the request got. (The referenced pathRequest will not be modified)
        /// </summary>
        public int RequestPath(ref PathRequest pathRequest)
        {
            nextRequestId++;

            pendingRequests.Enqueue(new()
            {
                endVoxIndex = pathRequest.endVoxIndex,
                pathType = pathRequest.pathType,
                radius = pathRequest.radius,
                startVoxIndex = pathRequest.startVoxIndex,
                snapRadius = pathRequest.snapRadius,
                requestId = nextRequestId,
            });

            return nextRequestId;
        }

        public delegate void Event_OnPathRequestCompleted(int requestId);
        public event Event_OnPathRequestCompleted OnPathRequestCompleted;
        public delegate void Event_OnPathRequestDiscarded(int requestId);
        public event Event_OnPathRequestDiscarded OnPathRequestDiscarded;

        private void OnGlobalReadAccessStart()
        {
            //Only check 
            if (fp_jobIsActive == true) return;

            TryGetActiveRequestAgain:;
            if (pendingRequests.TryDequeue(out var activeRequest) == false) return;
            if (discardedPendingRequests.Remove(activeRequest.requestId) == true)
            {
                OnPathRequestDiscarded?.Invoke(activeRequest.requestId);
                goto TryGetActiveRequestAgain;
            }

            if (pendingRequests.Count == 0) discardedPendingRequests.Clear();

            fp_jobIsActive = true;
            fp_job._activeRequest.Value = activeRequest;
            fp_handle = fp_job.Schedule();
        }

        private void OnGlobalReadAccessStop()
        {
            if (fp_jobIsActive == false) return;

            fp_jobIsActive = false;
            fp_handle.Complete();
            OnPathRequestCompleted?.Invoke(fp_job._activeRequest.Value.requestId);
        }

        /// <summary>
        /// Discards any pending path request with the given requestId
        /// </summary>
        public void DiscardPendingRequest(int requestId)
        {
            discardedPendingRequests.Add(requestId);
        }

        /// <summary>
        /// Discards all pending path requests
        /// </summary>
        public void DiscardAllPendingRequests()
        {
            pendingRequests.Clear();
            discardedPendingRequests.Clear();
        }

        #endregion HandlePathRequest




        #region ActualPathfinding

        [System.NonSerialized] public FindPath_work fp_job;
        private JobHandle fp_handle;
        private bool fp_jobIsActive = false;

        [BurstCompile]
        public struct FindPath_work : IJob
        {
            public NativeReference<PathPendingRequest> _activeRequest;
            /// <summary>
            /// The type this global voxel is, always air if voxsCount[X] is 0
            /// </summary>
            public NativeArray<byte>.ReadOnly _voxsType;
            public NativeHashMap<int, byte> _voxsSearched;
            public NativeList<ushort> _toSearchValues;
            public NativeList<int> _toSearchIndex;
            public NativeReference<VoxWorld>.ReadOnly _voxWorld;
            public NativeReference<int> _maxVoxelsSearched;
            public NativeHashMap<byte, float> _voxTypeToMultiply;

            /// <summary>
            /// voxel indexs path from end to start, only garanteed to be valid in OnPathRequestComplete() callback
            /// (_resultPath[0] == closestToEnd, _resultPath[^1] == closestToStart)
            /// </summary>
            public NativeList<int> _resultPath;

            public unsafe void Execute()
            {
                //Get job data
                var vWorld = _voxWorld.Value;
                var vTypes = _voxsType;
                var vSearched = _voxsSearched;
                var request = _activeRequest.Value;
                var toSearchValue = _toSearchValues;
                var toSearchIndex = _toSearchIndex;
                var voxTypeToMultiply = _voxTypeToMultiply;
                var maxVoxelsSearched = _maxVoxelsSearched.Value;
                int snapRadius = request.snapRadius;

                int vCountY = vWorld.vCountY;
                int vCountZ = vWorld.vCountZ;
                int vCountYZ = vWorld.vCountYZ;
                byte aiSize = request.radius;
                int aiSizeExtented = aiSize + 2;
                float voxSizeSqr = VoxGlobalSettings.voxelSizeWorld * VoxGlobalSettings.voxelSizeWorld * (aiSize / 2.0f);

                //Reset arrays
                toSearchValue.Clear();
                toSearchIndex.Clear();
                _resultPath.Clear();
                vSearched.Clear();

                //Create temp variabels
                int tempI;
                int activeVoxI;
                int tempVoxA;
                int tempVoxB;
                byte activeDirI;
                byte tempType;
                byte activeType;
                bool tempDirXA;
                bool tempDirXB;
                bool tempDirYA;
                bool tempDirYB;
                bool tempDirZA;
                bool tempDirZB;
                ushort tempVValue;
                int tempLoop;
                Vector3 tempPosA = Vector3.zero;
                Vector3 tempPosB = Vector3.zero;
                Vector3 tempPosC = Vector3.zero;
                Vector3 tempDirA = Vector3.zero;
                Vector3 tempDirB = Vector3.zero;
                int tempDeltaX;
                int tempDeltaY;
                int tempDeltaZ;

                int toSearchCount;
                int closestVoxV;

                //Get start voxel
                int startVoxI = request.startVoxIndex;
                int endVoxI = request.endVoxIndex;
                if (snapRadius > 0)
                {
                    toSearchCount = -1;
                    activeDirI = 1;
                    closestVoxV = 0;

#pragma warning disable CS0162
                    switch (request.pathType)
                    {
                        case PathType.climbing: startVoxI = SnapToValidVoxelClimbing(startVoxI); break;
                        case PathType.walk: throw new NotImplementedException(); break;
                        case PathType.flying: throw new NotImplementedException(); break;
                    }
#pragma warning restore CS0162

                    vSearched.Clear();//Reset stuff used in snapping
                    toSearchValue.Clear();
                    toSearchIndex.Clear();
                }

                //Prepare pathfinding
                ushort tempS = 0;
                VoxHelpBurst.GetVoxelCountBetweenWVoxIndexs(startVoxI, endVoxI, ref tempS, ref vWorld);

                toSearchCount = 0;
                int totalVoxelsSearched = 0;
                int closestVoxI = startVoxI;
                closestVoxV = tempS;
                toSearchValue.Add(tempS);
                toSearchIndex.Add(startVoxI);
                vSearched[startVoxI] = 1;//We must set it so its not searched 

                //Do pathfinding
#pragma warning disable CS0162
                switch (request.pathType)
                {
                    case PathType.climbing: DoPathfindClimbing(); break;
                    case PathType.walk: throw new NotImplementedException(); break;
                    case PathType.flying: throw new NotImplementedException(); break;
                }
#pragma warning restore CS0162

                //Recreate the path
                int voxOnPath = closestVoxI;
                int prevDir = 0;
                vSearched.Remove(startVoxI);

                while (true)
                {
                    if (vSearched.TryGetValue(voxOnPath, out activeDirI) == false)
                    {
                        _resultPath.Add(voxOnPath);
                        break;
                    }

                    //_resultPath.Add(voxOnPath);
                    if (activeDirI != prevDir) _resultPath.Add(voxOnPath);
                    prevDir = activeDirI;

                    switch (activeDirI)
                    {
                        case 1:
                            voxOnPath--; break;
                        case 2:
                            voxOnPath++; break;
                        case 3:
                            voxOnPath -= vCountZ; break;
                        case 4:
                            voxOnPath += vCountZ; break;
                        case 5:
                            voxOnPath -= vCountYZ; break;
                        case 6:
                            voxOnPath += vCountYZ; break;
                    }
                }

                ////Optimize path
                //for (int i = _resultPath.Length - 1; i > 1 ; i--)
                //{
                //    if (voxelLineCast(_resultPath[i], _resultPath[i - 2]) == false) continue;
                //    _resultPath.RemoveAt(i - 1);
                //}

                void DoPathfindClimbing()
                {
                    while (toSearchCount > -1)
                    {
                        totalVoxelsSearched++;
                        if (totalVoxelsSearched > maxVoxelsSearched || closestVoxI == endVoxI) break;

                        //Get voxel index to search
                        tempI = toSearchIndex[toSearchCount];
                        toSearchIndex.RemoveAt(toSearchCount);
                        toSearchValue.RemoveAt(toSearchCount);
                        toSearchCount--;

                        activeVoxI = tempI + 1;
                        activeDirI = 1;
                        CheckVoxIndexClimbing();

                        activeVoxI = tempI - 1;
                        activeDirI = 2;
                        CheckVoxIndexClimbing();

                        activeVoxI = tempI + vCountZ;
                        activeDirI = 3;
                        CheckVoxIndexClimbing();

                        activeVoxI = tempI - vCountZ;
                        activeDirI = 4;
                        CheckVoxIndexClimbing();

                        activeVoxI = tempI + vCountYZ;
                        activeDirI = 5;
                        CheckVoxIndexClimbing();

                        activeVoxI = tempI - vCountYZ;
                        activeDirI = 6;
                        CheckVoxIndexClimbing();
                    }
                }

                void CheckVoxIndexClimbing()
                {
                    //Send 8 sideways voxel casts. If aiSize - 1 is free for all casts and atleast one aiSize is overlapping voxel is valid
                    if (vSearched.TryAdd(activeVoxI, activeDirI) == false) return;

                    //Check if voxel is valid
                    if (vTypes[activeVoxI] > 0) return;

                    activeType = 0;
                    for (tempLoop = 1; tempLoop < aiSize; tempLoop++)
                    {
                        //+X+Z-Y
                        if (vTypes[activeVoxI + ((1 + vCountYZ - vCountZ) * tempLoop)] > 0) { activeType = 1; break; }

                         //+X+Z+Y
                        if (vTypes[activeVoxI + ((1 + vCountYZ + vCountZ) * tempLoop)] > 0) { activeType = 1; break; }
                        
                         //+X-Z+Y
                        if (vTypes[activeVoxI + ((1 - vCountYZ + vCountZ) * tempLoop)] > 0) { activeType = 1; break; }
                         
                         //+X-Z-Y
                        if (vTypes[activeVoxI + ((1 - vCountYZ - vCountZ) * tempLoop)] > 0) { activeType = 1; break; }
                        
                         //-X+Z-Y
                        if (vTypes[activeVoxI - ((1 + vCountYZ - vCountZ) * tempLoop)] > 0) { activeType = 1; break; }
                        
                         //-X+Z+Y
                        if (vTypes[activeVoxI - ((1 + vCountYZ + vCountZ) * tempLoop)] > 0) { activeType = 1; break; }
                        
                         //-X-Z+Y
                        if (vTypes[activeVoxI - ((1 - vCountYZ + vCountZ) * tempLoop)] > 0) { activeType = 1; break; }
                        
                         //-X-Z-Y
                        if (vTypes[activeVoxI - ((1 - vCountYZ - vCountZ) * tempLoop)] > 0) { activeType = 1; break; }
                    }

                    if (activeType > 0) return;

                    for (tempLoop = aiSize; tempLoop < aiSizeExtented; tempLoop++)
                    {
                        //+X+Z-Y
                        activeType = vTypes[activeVoxI + ((1 + vCountYZ - vCountZ) * tempLoop)]; if (activeType > 0) { goto SkipReturnInvalid; }

                        //+X+Z+Y
                        activeType = vTypes[activeVoxI + ((1 + vCountYZ + vCountZ) * tempLoop)]; if (activeType > 0) { goto SkipReturnInvalid; }

                        //+X-Z+Y
                        activeType = vTypes[activeVoxI + ((1 - vCountYZ + vCountZ) * tempLoop)]; if (activeType > 0) { goto SkipReturnInvalid; }

                        //+X-Z-Y
                        activeType = vTypes[activeVoxI + ((1 - vCountYZ - vCountZ) * tempLoop)]; if (activeType > 0) { goto SkipReturnInvalid; }

                        //-X+Z-Y
                        activeType = vTypes[activeVoxI - ((1 + vCountYZ - vCountZ) * tempLoop)]; if (activeType > 0) { goto SkipReturnInvalid; }

                        //-X+Z+Y
                        activeType = vTypes[activeVoxI - ((1 + vCountYZ + vCountZ) * tempLoop)]; if (activeType > 0) { goto SkipReturnInvalid; }

                        //-X-Z+Y
                        activeType = vTypes[activeVoxI - ((1 - vCountYZ + vCountZ) * tempLoop)]; if (activeType > 0) { goto SkipReturnInvalid; }

                        //-X-Z-Y
                        activeType = vTypes[activeVoxI - ((1 - vCountYZ - vCountZ) * tempLoop)]; if (activeType > 0) { goto SkipReturnInvalid; }
                    }

                    return;
                    SkipReturnInvalid:;

                    //Voxel is valid, add it to path
                    AddVoxelToPath();
                }

                void AddVoxelToPath()
                {
                    ////Get distance between tempVoxI and endVoxI
                    //tempVoxA = activeVoxI / vCountZ;
                    //tempVoxB = endVoxI / vCountZ;
                    //tempDeltaX = math.abs((activeVoxI % vCountZ) - (endVoxI % vCountZ));
                    //tempDeltaY = math.abs((tempVoxA % vCountY) - (tempVoxB % vCountY));
                    //tempDeltaZ = math.abs((tempVoxA / vCountY) - (tempVoxB / vCountY));
                    ////tempVValue = tempDeltaX + tempDeltaY + tempDeltaZ;
                    //double result = Math.Sqrt((tempDeltaX * tempDeltaX) + (tempDeltaY * tempDeltaY) + (tempDeltaZ * tempDeltaZ));
                    //tempVValue = (ushort)(result * 100.0d);

                    tempVoxA = activeVoxI / vCountZ;
                    tempVoxB = endVoxI / vCountZ;
                    tempVValue = (ushort)(math.abs((activeVoxI % vCountZ) - (endVoxI % vCountZ))
                        + math.abs((tempVoxA % vCountY) - (tempVoxB % vCountY))
                        + math.abs((tempVoxA / vCountY) - (tempVoxB / vCountY)));

                    if (voxTypeToMultiply.TryGetValue(activeType, out float tempMultiply) == true) tempVValue = (ushort)math.round(tempVValue * tempMultiply);

                    //Get where to insert it
                    if (toSearchCount > -1)
                    {
                        for (tempLoop = toSearchCount; tempLoop > -1; tempLoop--)
                        {
                            if (tempVValue > toSearchValue[tempLoop]) continue;

                            if (tempLoop + 2 > toSearchCount)
                            {
                                toSearchValue.Add(tempVValue);
                                toSearchIndex.Add(activeVoxI);
                                toSearchCount++;
                                break;
                            }

                            toSearchValue.InsertRangeWithBeginEnd(tempLoop + 1, tempLoop + 2);
                            toSearchValue[tempLoop] = tempVValue;
                            toSearchIndex.InsertRangeWithBeginEnd(tempLoop + 1, tempLoop + 2);
                            toSearchIndex[tempLoop] = activeVoxI;
                            toSearchCount++;
                            break;
                        }
                    }
                    else
                    {
                        toSearchValue.Add(tempVValue);
                        toSearchIndex.Add(activeVoxI);
                        toSearchCount++;
                    }

                    if (closestVoxV > tempVValue)
                    {
                        //When found new valid voxel closer to end
                        closestVoxI = activeVoxI;
                        closestVoxV = tempVValue;
                    }
                }

                int SnapToValidVoxelClimbing(int snapThis)
                {
                    activeVoxI = snapThis; CheckVoxIndexClimbing(); if (toSearchCount > 0) return activeVoxI;

                    for (int rad = 1; rad < snapRadius; rad++)
                    {
                        activeVoxI = snapThis + rad; CheckVoxIndexClimbing(); if (toSearchCount > 0) return activeVoxI;
                        activeVoxI = snapThis - rad; CheckVoxIndexClimbing(); if (toSearchCount > 0) return activeVoxI;

                        activeVoxI = snapThis + (vCountZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > 0) return activeVoxI;
                        activeVoxI = snapThis - (vCountZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > 0) return activeVoxI;

                        activeVoxI = snapThis + (vCountYZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > 0) return activeVoxI;
                        activeVoxI = snapThis - (vCountYZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > 0) return activeVoxI;
                    }

                    return snapThis;
                }

                bool voxelLineCast(int voxIA, int voxIB)
                {
                    VoxHelpBurst.WVoxIndexToPos(ref voxIA, ref tempPosA, ref vWorld);
                    VoxHelpBurst.WVoxIndexToPos(ref voxIB, ref tempPosB, ref vWorld);
                    tempDirA = tempPosB - tempPosA;

                    vSearched.Clear();
                    toSearchIndex.Clear();
                    toSearchIndex.Add(voxIA);
                    toSearchCount = 0;

                    while (toSearchCount > -1)
                    {
                        activeVoxI = toSearchIndex[toSearchCount];
                        if (activeVoxI == voxIB) break;

                        toSearchIndex.RemoveAt(toSearchCount);
                        toSearchCount--;

                        if (vSearched.TryAdd(activeVoxI, 0) == false) continue;
                        if (vTypes[activeVoxI] > 0) continue;
                        VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref tempPosC, ref vWorld);
                        if (DisToLine() > voxSizeSqr) continue;

                        //+X+Z-Y
                        if (vTypes[activeVoxI + 1 + vCountYZ - vCountZ] > 0) goto SkipIgnoreSpread;
                        //+X+Z+Y
                        if (vTypes[activeVoxI + 1 + vCountYZ + vCountZ] > 0) goto SkipIgnoreSpread;
                        //+X-Z+Y
                        if (vTypes[activeVoxI + 1 - vCountYZ + vCountZ] > 0) goto SkipIgnoreSpread;
                        //+X-Z-Y
                        if (vTypes[activeVoxI + 1 - vCountYZ - vCountZ] > 0) goto SkipIgnoreSpread;
                        //-X+Z-Y
                        if (vTypes[activeVoxI - 1 + vCountYZ - vCountZ] > 0) goto SkipIgnoreSpread;
                        //-X+Z+Y
                        if (vTypes[activeVoxI - 1 + vCountYZ + vCountZ] > 0) goto SkipIgnoreSpread;
                        //-X-Z+Y
                        if (vTypes[activeVoxI - 1 - vCountYZ + vCountZ] > 0) goto SkipIgnoreSpread;
                        //-X-Z-Y
                        if (vTypes[activeVoxI - 1 - vCountYZ - vCountZ] > 0) goto SkipIgnoreSpread;
                        //if (vTypes[activeVoxI + 1] > 0) goto SkipIgnoreSpread;
                        //if (vTypes[activeVoxI - 1] > 0) goto SkipIgnoreSpread;
                        //if (vTypes[activeVoxI + vCountZ] > 0) goto SkipIgnoreSpread;
                        //if (vTypes[activeVoxI - vCountZ] > 0) goto SkipIgnoreSpread;
                        //if (vTypes[activeVoxI + vCountYZ] > 0) goto SkipIgnoreSpread;
                        //if (vTypes[activeVoxI - vCountYZ] > 0) goto SkipIgnoreSpread;
                        continue;

                    SkipIgnoreSpread:;
                        tempVoxA = activeVoxI + 1; if (vSearched.ContainsKey(tempVoxA) == false) { toSearchCount++; toSearchIndex.Add(tempVoxA); }
                        tempVoxA = activeVoxI - 1; if (vSearched.ContainsKey(tempVoxA) == false) { toSearchCount++; toSearchIndex.Add(tempVoxA); }
                        tempVoxA = activeVoxI + vCountZ; if (vSearched.ContainsKey(tempVoxA) == false) { toSearchCount++; toSearchIndex.Add(tempVoxA); }
                        tempVoxA = activeVoxI - vCountZ; if (vSearched.ContainsKey(tempVoxA) == false) { toSearchCount++; toSearchIndex.Add(tempVoxA); }
                        tempVoxA = activeVoxI + vCountYZ; if (vSearched.ContainsKey(tempVoxA) == false) { toSearchCount++; toSearchIndex.Add(tempVoxA); }
                        tempVoxA = activeVoxI - vCountYZ; if (vSearched.ContainsKey(tempVoxA) == false) { toSearchCount++; toSearchIndex.Add(tempVoxA); }
                    }

                    return toSearchCount > -1;

                    float DisToLine()
                    {
                        Vector3 pointToLineStart = tempPosC - tempPosA;

                        // Calculate the projection of pointToLineStart onto the lineDirection
                        float t = Vector3.Dot(pointToLineStart, tempDirA) / tempDirA.sqrMagnitude;

                        // If t is less than 0, the closest point is linePoint1
                        if (t < 0)
                        {
                            return (tempPosC - tempPosA).sqrMagnitude;
                        }
                        // If t is greater than 1, the closest point is linePoint2
                        else if (t > 1)
                        {
                            return (tempPosC - tempPosB).sqrMagnitude;
                        }
                        // Otherwise, the closest point is along the line between linePoint1 and linePoint2
                        else
                        {
                            Vector3 closestPoint = tempPosA + t * tempDirA;
                            return (tempPosC - closestPoint).sqrMagnitude;
                        }
                    }
                }
            }
        }

        #endregion ActualPathfinding

#if UNITY_EDITOR
        //debug stopwatch
        private System.Diagnostics.Stopwatch stopwatch = new();

        public void Debug_toggleTimer(string note = "")
        {
            if (stopwatch.IsRunning == false)
            {
                stopwatch.Restart();
            }
            else
            {
                stopwatch.Stop();
                Debug.Log(note + " time: " + stopwatch.Elapsed.TotalMilliseconds + "ms");
            }
        }
#endif
    }
}
