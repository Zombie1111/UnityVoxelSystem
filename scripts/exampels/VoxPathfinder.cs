using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.VisualScripting;
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

        public float toll;

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
            public NativeList<Vector3> _resultPath;

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
                int aiSizeExtented = aiSize - 1;
                float voxSizeSqr = VoxGlobalSettings.voxelSizeWorld * VoxGlobalSettings.voxelSizeWorld * (aiSize / 2.0f);

                //Reset arrays
                toSearchValue.Clear();
                toSearchIndex.Clear();
                _resultPath.Clear();
                vSearched.Clear();

                //Create temp variabels
                int tempI;
                int activeVoxI;
                int tempVoxI;
                byte activeDirI;
                byte activeType;
                ushort tempVValue;
                int tempLoop;
                Vector3 tempVoxPos = Vector3.zero;
                int tempReminderA;
                int tempReminderB;

                int toSearchCount;
                int closestVoxV;
                int closestVoxI;
                bool tempDidAdd;

                //Get start and end voxel
                int startVoxI = request.startVoxIndex;
                int endVoxI = request.endVoxIndex;

                if (snapRadius > 0)
                {
                    toSearchCount = -1;
                    activeDirI = 1;
                    closestVoxV = 0;
                    closestVoxI = 0;

#pragma warning disable CS0162
                    switch (request.pathType)
                    {
                        case PathType.climbing: startVoxI = SnapToValidVoxelClimbing(startVoxI); break;
                        case PathType.walk: throw new NotImplementedException(); break;
                        case PathType.flying: throw new NotImplementedException(); break;
                    }

                    toSearchCount = -1;
                    activeDirI = 1;
                    vSearched.Clear();//Reset stuff used in snapping
                    toSearchValue.Clear();
                    toSearchIndex.Clear();

                    switch (request.pathType)
                    {
                        case PathType.climbing: endVoxI = SnapToValidVoxelClimbing(endVoxI); break;
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
                closestVoxI = startVoxI;
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
                        VoxHelpBurst.WVoxIndexToPos(ref voxOnPath, ref tempVoxPos, ref vWorld);
                        _resultPath.Add(tempVoxPos);
                        break;
                    }

                    //_resultPath.Add(voxOnPath);
                    if (activeDirI != prevDir)
                    {
                        VoxHelpBurst.WVoxIndexToPos(ref voxOnPath, ref tempVoxPos, ref vWorld);
                        _resultPath.Add(tempVoxPos);
                    }
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
                    if (vSearched.TryAdd(activeVoxI, activeDirI) == false) return;

                    //Check if voxel is overlapping
                    if (vTypes[activeVoxI] > 0) return;

                    activeType = 0;
                    for (tempLoop = 1; tempLoop < aiSize; tempLoop++)
                    {
                        if (vTypes[activeVoxI + tempLoop] > 0) { activeType = 1; break; }
                        if (vTypes[activeVoxI - tempLoop] > 0) { activeType = 1; break; }
                        if (vTypes[activeVoxI + (vCountZ * tempLoop)] > 0) { activeType = 1; break; }
                        if (vTypes[activeVoxI - (vCountZ * tempLoop)] > 0) { activeType = 1; break; }
                        if (vTypes[activeVoxI + (vCountYZ * tempLoop)] > 0) { activeType = 1; break; }
                        if (vTypes[activeVoxI - (vCountYZ * tempLoop)] > 0) { activeType = 1; break; }
                    
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

                    //Check if voxel is close enough to any surface, do we really need to check this much?
                    activeType = vTypes[activeVoxI + aiSize]; if (activeType > 0) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI - aiSize]; if (activeType > 0) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI + (vCountZ * aiSize)]; if (activeType > 0) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI - (vCountZ * aiSize)]; if (activeType > 0) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI + (vCountYZ * aiSize)]; if (activeType > 0) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI - (vCountYZ * aiSize)]; if (activeType > 0) { goto SkipReturnInvalid; }

                    tempVoxI = activeVoxI + (1 + vCountYZ - vCountZ); if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI + (1 + vCountYZ + vCountZ); if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI + (1 - vCountYZ + vCountZ); if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI + (1 - vCountYZ - vCountZ); if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI - (1 + vCountYZ - vCountZ); if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI - (1 + vCountYZ + vCountZ); if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI - (1 - vCountYZ + vCountZ); if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI - (1 - vCountYZ - vCountZ); if (CheckShit() == true) { goto SkipReturnInvalid; };

                    bool CheckShit()
                    {
                        //+X+Z-Y
                        activeType = vTypes[tempVoxI + ((1 + vCountYZ - vCountZ) * aiSizeExtented)]; if (activeType > 0) { return true; }
                        //+X+Z+Y
                        activeType = vTypes[tempVoxI + ((1 + vCountYZ + vCountZ) * aiSizeExtented)]; if (activeType > 0) { return true; }
                        //+X-Z+Y
                        activeType = vTypes[tempVoxI + ((1 - vCountYZ + vCountZ) * aiSizeExtented)]; if (activeType > 0) { return true; }
                        //+X-Z-Y
                        activeType = vTypes[tempVoxI + ((1 - vCountYZ - vCountZ) * aiSizeExtented)]; if (activeType > 0) { return true; }
                        //-X+Z-Y
                        activeType = vTypes[tempVoxI - ((1 + vCountYZ - vCountZ) * aiSizeExtented)]; if (activeType > 0) { return true; }
                        //-X+Z+Y
                        activeType = vTypes[tempVoxI - ((1 + vCountYZ + vCountZ) * aiSizeExtented)]; if (activeType > 0) { return true; }
                        //-X-Z+Y
                        activeType = vTypes[tempVoxI - ((1 - vCountYZ + vCountZ) * aiSizeExtented)]; if (activeType > 0) { return true; }
                        //-X-Z-Y
                        activeType = vTypes[tempVoxI - ((1 - vCountYZ - vCountZ) * aiSizeExtented)]; if (activeType > 0) { return true; }

                        activeType = vTypes[tempVoxI + aiSizeExtented]; if (activeType > 0) { return true; }
                        activeType = vTypes[tempVoxI - aiSizeExtented]; if (activeType > 0) { return true; }
                        activeType = vTypes[tempVoxI + (vCountZ * aiSizeExtented)]; if (activeType > 0) { return true; }
                        activeType = vTypes[tempVoxI - (vCountZ * aiSizeExtented)]; if (activeType > 0) { return true; }
                        activeType = vTypes[tempVoxI + (vCountYZ * aiSizeExtented)]; if (activeType > 0) { return true; }
                        activeType = vTypes[tempVoxI - (vCountYZ * aiSizeExtented)]; if (activeType > 0) { return true; }

                        return false;
                    }

                    return;
                    SkipReturnInvalid:;

                    //Voxel is valid, add it to the path
                    AddVoxelToPath();
                }

                //activeVoxI + ((1 + vCountYZ - vCountZ) * tempLoop)
                //activeVoxI + ((1 + vCountYZ + vCountZ) * tempLoop)
                //activeVoxI + ((1 - vCountYZ + vCountZ) * tempLoop)
                //activeVoxI + ((1 - vCountYZ - vCountZ) * tempLoop)
                //activeVoxI - ((1 + vCountYZ - vCountZ) * tempLoop)
                //activeVoxI - ((1 + vCountYZ + vCountZ) * tempLoop)
                //activeVoxI - ((1 - vCountYZ + vCountZ) * tempLoop)
                //activeVoxI - ((1 - vCountYZ - vCountZ) * tempLoop)

                void AddVoxelToPath()
                {
                    //Get distance between tempVoxI and endVoxI
                    // Extract x, y, z coordinates for activeVoxI
                    tempReminderA = activeVoxI % (vCountY * vCountZ);
                    
                    // Extract x, y, z coordinates for endVoxI
                    tempReminderB = endVoxI % (vCountY * vCountZ);

                    // Calculate Manhattan distance
                    tempVValue = (ushort)(
                        math.abs((activeVoxI / (vCountY * vCountZ)) - (endVoxI / (vCountY * vCountZ)))
                        + math.abs((tempReminderA / vCountZ) - (tempReminderB / vCountZ))
                        + math.abs((tempReminderA % vCountZ) - (tempReminderB % vCountZ))
                        );

                    //tempVoxA = math.abs((activeVoxI / (vCountY * vCountZ)) - (endVoxI / (vCountY * vCountZ)));
                    //tempVoxB = math.abs((tempDeltaX / vCountZ) - (tempDeltaY / vCountZ));
                    //tempDeltaZ = math.abs((tempDeltaX % vCountZ) - (tempDeltaY % vCountZ));
                    //tempVValue = (ushort)(Math.Sqrt((tempVoxA * tempVoxA) + (tempVoxB * tempVoxB) + (tempDeltaZ * tempDeltaZ)) * 10);

                    //tempVValue = (ushort)(
                    //    math.abs((activeVoxI % vCountZ) - (endVoxI % vCountZ))
                    //    + math.abs((activeVoxI % vCountY) - (endVoxI % vCountY))
                    //    + math.abs((activeVoxI / (vCountY * vCountZ)) - (endVoxI / (vCountY * vCountZ)))
                    //    );

                    //tempVValue = (ushort)(
                    //    math.abs((activeVoxI % vCountZ) - (endVoxI % vCountZ))
                    //    + math.abs((activeVoxI % vCountY) - (endVoxI % vCountY))
                    //    + math.abs((activeVoxI / vCountY) - (endVoxI / vCountY))
                    //    );

                    //tempDeltaX = math.abs((activeVoxI % vCountZ) - (endVoxI % vCountZ));
                    //tempDeltaY = math.abs((activeVoxI % vCountY) - (endVoxI % vCountY));
                    //tempDeltaZ = math.abs((activeVoxI / vCountY) - (endVoxI / vCountY));
                    //tempVValue = (ushort)(Math.Sqrt((tempDeltaX * tempDeltaX) + (tempDeltaY * tempDeltaY) + (tempDeltaZ * tempDeltaZ)) * 100);

                    if (voxTypeToMultiply.TryGetValue(activeType, out float tempMultiply) == true) tempVValue = (ushort)math.round(tempVValue * tempMultiply);

                    tempDidAdd = false;
                    //Get where to insert it
                    if (toSearchCount > -1)
                    {
                        for (tempLoop = toSearchCount; tempLoop > -1; tempLoop--)
                        {
                            if (tempVValue >= toSearchValue[tempLoop]) continue;

                            if (tempLoop + 1 > toSearchCount)
                            {
                                tempDidAdd = true;
                                toSearchValue.Add(tempVValue);
                                toSearchIndex.Add(activeVoxI);
                                toSearchCount++;
                                break;
                            }

                            tempDidAdd = true;
                            toSearchValue.InsertRangeWithBeginEnd(tempLoop, tempLoop + 1);
                            toSearchValue[tempLoop] = tempVValue;
                            toSearchIndex.InsertRangeWithBeginEnd(tempLoop, tempLoop + 1);
                            toSearchIndex[tempLoop] = activeVoxI;
                            toSearchCount++;
                            break;
                        }
                    }
                    else
                    {
                        tempDidAdd = true;
                        toSearchValue.Add(tempVValue);
                        toSearchIndex.Add(activeVoxI);
                        toSearchCount++;
                    }

                    if (tempDidAdd == false)
                    {
                        //Dont quite understand how didAdd is false sometimes
                        toSearchValue.InsertRangeWithBeginEnd(0, 1);
                        toSearchValue[0] = tempVValue;
                        toSearchIndex.InsertRangeWithBeginEnd(0, 1);
                        toSearchIndex[0] = activeVoxI;
                        toSearchCount++;
                    }

                    if (closestVoxV >= tempVValue && closestVoxI != endVoxI)
                    {
                        //When found new valid voxel closer to end
                        closestVoxI = activeVoxI;
                        closestVoxV = tempVValue;
                    }
                }

                int SnapToValidVoxelClimbing(int snapThis)
                {
                    activeVoxI = snapThis; CheckVoxIndexClimbing(); if (toSearchCount > -1) return activeVoxI;

                    for (int rad = 1; rad < snapRadius; rad++)
                    {
                        activeVoxI = snapThis + rad; CheckVoxIndexClimbing(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - rad; CheckVoxIndexClimbing(); if (toSearchCount > -1) return activeVoxI;

                        activeVoxI = snapThis + (vCountZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - (vCountZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > -1) return activeVoxI;

                        activeVoxI = snapThis + (vCountYZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - (vCountYZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > -1) return activeVoxI;
                    }

                    return snapThis;
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
