using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
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

                //Reset arrays
                toSearchValue.Clear();
                toSearchIndex.Clear();
                _resultPath.Clear();
                vSearched.Clear();

                //Create temp variabels
                int tempI;
                int tempVoxI;
                int tempVoxA;
                int tempVoxB;
                byte tempDirI;
                byte tempType;
                byte tempTypeUsed;
                bool tempDirXA;
                bool tempDirXB;
                bool tempDirYA;
                bool tempDirYB;
                bool tempDirZA;
                bool tempDirZB;
                int tempVValue;
                int tempLoop;

                int toSearchCount;
                int closestVoxV;

                //Get start voxel
                int startVoxI = request.startVoxIndex;
                int endVoxI = request.endVoxIndex;
                if (snapRadius > 0)
                {
                    toSearchCount = 0;
                    tempDirI = 1;
                    closestVoxV = 0;

                    startVoxI = SnapToValidVoxel(startVoxI);

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
                //vSearched[startVoxI] = 0;//So while loop stops when reached startVoxI
                vSearched.Remove(startVoxI);
                while (true)
                {
                    if (vSearched.TryGetValue(voxOnPath, out tempDirI) == false) break;
                    //if (tempDirI == 0) return;
                    _resultPath.Add(voxOnPath);

                    switch (tempDirI)
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

                        tempVoxI = tempI + 1;
                        tempDirI = 1;
                        CheckVoxIndexClimbing();

                        tempVoxI = tempI - 1;
                        tempDirI = 2;
                        CheckVoxIndexClimbing();

                        tempVoxI = tempI + vCountZ;
                        tempDirI = 3;
                        CheckVoxIndexClimbing();

                        tempVoxI = tempI - vCountZ;
                        tempDirI = 4;
                        CheckVoxIndexClimbing();

                        tempVoxI = tempI + vCountYZ;
                        tempDirI = 5;
                        CheckVoxIndexClimbing();

                        tempVoxI = tempI - vCountYZ;
                        tempDirI = 6;
                        CheckVoxIndexClimbing();
                    }
                }

                void CheckVoxIndexClimbing()
                {
                    if (vSearched.TryAdd(tempVoxI, tempDirI) == false) return;

                    //Check if voxel is valid
                    if (vTypes[tempVoxI] > 0) return;

                    tempTypeUsed = 0;
                    tempDirXA = false;
                    tempDirXB = false;
                    tempDirYA = false;
                    tempDirYB = false;
                    tempDirZA = false;
                    tempDirZB = false;

                    //Always check 1 radius
                    //X
                    tempType = vTypes[tempVoxI + 1];
                    if (tempType > 0)
                    {
                        tempTypeUsed = tempType;
                        tempDirXA = true;
                    }

                    tempType = vTypes[tempVoxI - 1];
                    if (tempType > 0)
                    {
                        tempTypeUsed = tempType;
                        tempDirXB = true;
                    }

                    //Y
                    tempType = vTypes[tempVoxI + vCountZ];
                    if (tempType > 0)
                    {
                        tempTypeUsed = tempType;
                        tempDirYA = true;
                    }

                    tempType = vTypes[tempVoxI - vCountZ];
                    if (tempType > 0)
                    {
                        tempTypeUsed = tempType;
                        tempDirYB = true;
                    }

                    //Z
                    tempType = vTypes[tempVoxI + vCountYZ];
                    if (tempType > 0)
                    {
                        tempTypeUsed = tempType;
                        tempDirZA = true;
                    }

                    tempType = vTypes[tempVoxI - vCountYZ];
                    if (tempType > 0)
                    {
                        tempTypeUsed = tempType;
                        tempDirZB = true;
                    }

                    if (tempTypeUsed == 0)
                    {
                        //Found no ground in straight directions but we also must check sideways for any ground
                        //+X+Z-Y
                        tempType = vTypes[tempVoxI + 1 + vCountYZ - vCountZ];
                        if (tempType > 0)
                        {
                            tempDirXA = true;
                            tempDirZA = true;
                            tempDirYB = true;
                            tempTypeUsed = tempType;
                            goto SkipNoGroundReturn;
                        }

                        //+X+Z+Y
                        tempType = vTypes[tempVoxI + 1 + vCountYZ + vCountZ];
                        if (tempType > 0)
                        {
                            tempDirXA = true;
                            tempDirZA = true;
                            tempDirYA = true;
                            tempTypeUsed = tempType;
                            goto SkipNoGroundReturn;
                        }

                        //+X-Z+Y
                        tempType = vTypes[tempVoxI + 1 - vCountYZ + vCountZ];
                        if (tempType > 0)
                        {
                            tempDirXA = true;
                            tempDirZB = true;
                            tempDirYA = true;
                            tempTypeUsed = tempType;
                            goto SkipNoGroundReturn;
                        }

                        //+X-Z-Y
                        tempType = vTypes[tempVoxI + 1 - vCountYZ - vCountZ];
                        if (tempType > 0)
                        {
                            tempDirXA = true;
                            tempDirZB = true;
                            tempDirYB = true;
                            tempTypeUsed = tempType;
                            goto SkipNoGroundReturn;
                        }

                        //-X+Z-Y
                        tempType = vTypes[tempVoxI - 1 + vCountYZ - vCountZ];
                        if (tempType > 0)
                        {
                            tempDirXB = true;
                            tempDirZA = true;
                            tempDirYB = true;
                            tempTypeUsed = tempType;
                            goto SkipNoGroundReturn;
                        }

                        //-X+Z+Y
                        tempType = vTypes[tempVoxI - 1 + vCountYZ + vCountZ];
                        if (tempType > 0)
                        {
                            tempDirXB = true;
                            tempDirZA = true;
                            tempDirYA = true;
                            tempTypeUsed = tempType;
                            goto SkipNoGroundReturn;
                        }

                        //-X-Z+Y
                        tempType = vTypes[tempVoxI - 1 - vCountYZ + vCountZ];
                        if (tempType > 0)
                        {
                            tempDirXB = true;
                            tempDirZB = true;
                            tempDirYA = true;
                            tempTypeUsed = tempType;
                            goto SkipNoGroundReturn;
                        }

                        //-X-Z-Y
                        tempType = vTypes[tempVoxI - 1 - vCountYZ - vCountZ];
                        if (tempType > 0)
                        {
                            tempDirXB = true;
                            tempDirZB = true;
                            tempDirYB = true;
                            tempTypeUsed = tempType;
                            goto SkipNoGroundReturn;
                        }

                        return;
                    }

                    SkipNoGroundReturn:;

                    //Check wider radius
                    for (tempType = 2; tempType < aiSize; tempType++)
                    {
                        if (vTypes[tempVoxI + tempType] > 0) tempDirXA = true;
                        if (vTypes[tempVoxI - tempType] > 0) tempDirXB = true;

                        if (vTypes[tempVoxI + (vCountZ * tempType)] > 0) tempDirYA = true;
                        if (vTypes[tempVoxI - (vCountZ * tempType)] > 0) tempDirYB = true;

                        if (vTypes[tempVoxI + (vCountYZ * tempType)] > 0) tempDirZA = true;
                        if (vTypes[tempVoxI - (vCountYZ * tempType)] > 0) tempDirZB = true;
                    }

                    //Check if fit
                    if (tempDirXA == true && tempDirXB == true) return;
                    if (tempDirYA == true && tempDirYB == true) return;
                    if (tempDirZA == true && tempDirZB == true) return;

                    //Voxel is valid, add it to path
                    AddVoxelToPath();
                }

                void AddVoxelToPath()
                {
                    //Get distance between tempVoxI and endVoxI
                    tempVValue = math.abs((tempVoxI % vCountZ) - (endVoxI % vCountZ));
                    tempVoxA = tempVoxI / vCountZ;
                    tempVoxB = endVoxI / vCountZ;
                    tempVValue += math.abs((tempVoxA % vCountY) - (tempVoxB % vCountY))
                        + math.abs((tempVoxA / vCountY) - (tempVoxB / vCountY));

                    if (voxTypeToMultiply.TryGetValue(tempTypeUsed, out float tempMultiply) == true) tempVValue = (int)math.round(tempVValue * tempMultiply);

                    //Get where to insert it
                    if (toSearchCount > 0)
                    {
                        for (tempLoop = toSearchCount; tempLoop > -1; tempLoop--)
                        {
                            if (tempVValue > toSearchValue[tempLoop]) continue;

                            toSearchValue.InsertRangeWithBeginEnd(tempLoop, tempLoop + 1);
                            toSearchValue[tempLoop] = (ushort)tempVValue;
                            toSearchIndex.InsertRangeWithBeginEnd(tempLoop, tempLoop + 1);
                            toSearchIndex[tempLoop] = tempVoxI;
                            toSearchCount++;
                            break;
                        }
                    }
                    else
                    {
                        toSearchValue.Add((ushort)tempVValue);
                        toSearchIndex.Add(tempVoxI);
                        toSearchCount++;
                    }

                    if (closestVoxV > tempVValue)
                    {
                        //When found new valid voxel closer to end
                        closestVoxI = tempVoxI;
                        closestVoxV = tempVValue;
                    }
                }

                int SnapToValidVoxel(int snapThis)
                {
                    tempVoxI = snapThis; CheckVoxIndexClimbing(); if (toSearchCount > 0) return tempVoxI;

                    for (int rad = 1; rad < snapRadius; rad++)
                    {
                        tempVoxI = snapThis + rad; CheckVoxIndexClimbing(); if (toSearchCount > 0) return tempVoxI;
                        tempVoxI = snapThis - rad; CheckVoxIndexClimbing(); if (toSearchCount > 0) return tempVoxI;

                        tempVoxI = snapThis + (vCountZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > 0) return tempVoxI;
                        tempVoxI = snapThis - (vCountZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > 0) return tempVoxI;

                        tempVoxI = snapThis + (vCountYZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > 0) return tempVoxI;
                        tempVoxI = snapThis - (vCountYZ * rad); CheckVoxIndexClimbing(); if (toSearchCount > 0) return tempVoxI;
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
