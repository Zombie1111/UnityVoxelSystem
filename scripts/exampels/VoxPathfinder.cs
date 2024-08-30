//https://github.com/Zombie1111/UnityVoxelSystem
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace zombVoxels
{
    public class VoxPathfinder : MonoBehaviour
    {
        #region Configuration

        [Tooltip("How many voxels can be searched before giving up on finding a valid path")] [SerializeField] private int maxVoxelsSearched = 10000;
        [Tooltip("The defualt lenght of some nativeContainers, not very important")] [SerializeField] private int maxVoxelsToSearch = 1000;
        [Tooltip("How much more expensive should it be to travel on a voxel of type X")]
        [SerializeField] private SerializableDictionary<byte, float> voxTypeCostMultiplier = new();

        #endregion Configuration


        #region SetupPathfinder

        [System.NonSerialized] public VoxGlobalHandler voxHandler;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (voxHandler != null) return;
            SetVoxGlobalHandler(VoxGlobalHandler.TryGetValidGlobalHandler(pendingRequests.Count > 0));
        }

        private void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SetVoxGlobalHandler(VoxGlobalHandler.TryGetValidGlobalHandler(pendingRequests.Count > 0));
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SetVoxGlobalHandler(null);
        }

        /// <summary>
        /// Sets what globalVoxelHandler to use, pass null to clear
        /// </summary>
        public void SetVoxGlobalHandler(VoxGlobalHandler newHandler)
        {
            if (voxHandler == newHandler)
            {
                if (voxHandler == null)
                {
                    ClearAllocatedMemory();//Make sure memory is always cleared if null
                    OnClearVoxelSystem();
                }

                return;
            }

            ClearAllocatedMemory();

            if (voxHandler != null)
            {
                voxHandler.OnGlobalReadAccessStart -= OnGlobalReadAccessStart;
                voxHandler.OnGlobalReadAccessStop -= OnGlobalReadAccessStop;
                voxHandler.OnClearVoxelSystem -= OnClearVoxelSystem;
                voxHandler.OnSetupVoxelSystem -= OnSetupVoxelSystem;
                if (voxHandler.voxRuntimeIsValid == true) OnClearVoxelSystem();//Not been cleared, so call manually
            }

            voxHandler = newHandler;
            if (voxHandler == null) return;

            SetupPathfindJob();
            voxHandler.OnGlobalReadAccessStart += OnGlobalReadAccessStart;
            voxHandler.OnGlobalReadAccessStop += OnGlobalReadAccessStop;
            voxHandler.OnClearVoxelSystem += OnClearVoxelSystem;
            voxHandler.OnSetupVoxelSystem += OnSetupVoxelSystem;
            if (voxHandler.voxRuntimeIsValid == true) OnSetupVoxelSystem();//Already setup so call manually
        }

        private void SetupPathfindJob()
        {
            fp_job = new()
            {
                //_voxsType and _voxWorld is assigned in OnSetupVoxelSystem()
                _activeRequest = new(Allocator.Persistent),
                _voxsSearched = new(maxVoxelsSearched, Allocator.Persistent),
                _toSearchValues = new(maxVoxelsToSearch, Allocator.Persistent),
                _toSearchIndex = new(maxVoxelsToSearch, Allocator.Persistent),
                _maxVoxelsSearched = new(maxVoxelsSearched, Allocator.Persistent),
                _voxTypeToMultiply = voxTypeCostMultiplier.ToNativeHashMap(Allocator.Persistent),
                _resultPath = new(maxVoxelsToSearch / 10, Allocator.Persistent)
            };
        }

        private void ClearAllocatedMemory()
        {
            OnGlobalReadAccessStop();
            DiscardAllPendingRequests();

            //_voxsType and _voxWorld is cleared in OnClearVoxelSystem()
            if (fp_job._activeRequest.IsCreated == true) fp_job._activeRequest.Dispose();
            if (fp_job._toSearchIndex.IsCreated == true) fp_job._toSearchIndex.Dispose();
            if (fp_job._toSearchValues.IsCreated == true) fp_job._toSearchValues.Dispose();
            if (fp_job._voxsSearched.IsCreated == true) fp_job._voxsSearched.Dispose();
            if (fp_job._maxVoxelsSearched.IsCreated == true) fp_job._maxVoxelsSearched.Dispose();
            if (fp_job._voxTypeToMultiply.IsCreated == true) fp_job._voxTypeToMultiply.Dispose();
            if (fp_job._resultPath.IsCreated == true) fp_job._resultPath.Dispose();
        }

        private bool voxelSystemIsValid = false;

        private void OnClearVoxelSystem()
        {
            //_voxsType and _voxWorld dont need to be cleared or disposed
            voxelSystemIsValid = false;
        }

        private void OnSetupVoxelSystem()
        {
            fp_job._voxsType = voxHandler.cvo_job.voxsType.AsReadOnly();
            fp_job._voxWorld = voxHandler.cvo_job.voxWorld.AsReadOnly();
            voxelSystemIsValid = true;
        }

        #endregion SetupPathfinder




        #region HandlePathRequest

        [System.Serializable]
        public struct PathRequest
        {
            [System.NonSerialized] public int startVoxIndex;
            [System.NonSerialized] public int endVoxIndex;
            [Tooltip("The radius of the character, min distance from walls (In voxel units)")] public byte radius;
            [Tooltip("The maximum distance the path can snap (In voxel units)")] public byte snapRadius;
            [Tooltip("The path navigation mode")] public PathType pathType;
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
            //Get if can start
            if (fp_jobIsActive == true || voxelSystemIsValid == false) return;

            //Get request
            TryGetActiveRequestAgain:;
            if (pendingRequests.TryDequeue(out var activeRequest) == false) return;
            if (discardedPendingRequests.Remove(activeRequest.requestId) == true)
            {
                OnPathRequestDiscarded?.Invoke(activeRequest.requestId);
                goto TryGetActiveRequestAgain;
            }

            //Schedule request
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
            while (pendingRequests.TryDequeue(out var request) == true)
            {
                OnPathRequestDiscarded?.Invoke(request.requestId);
            }

            pendingRequests.Clear();
            discardedPendingRequests.Clear();
        }

        #endregion HandlePathRequest




        #region ActualPathfinding

        [System.NonSerialized] public FindPath_work fp_job;
        private JobHandle fp_handle;
        private bool fp_jobIsActive = false;

        public struct PathNode
        {
            public Vector3 position;
            public Vector3 normal;
        }

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
            /// path nodes from end to start, only garanteed to be valid in OnPathRequestComplete() callback
            /// (_resultPath[0] == closestToEnd, _resultPath[^1] == closestToStart)
            /// </summary>
            public NativeList<PathNode> _resultPath;

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
                int snapRadiusExtended = snapRadius * 3;

                int vCountY = vWorld.vCountY;
                int vCountZ = vWorld.vCountZ;
                int vCountYZ = vWorld.vCountYZ;
                byte aiSize = request.radius;
                int aiSizeReduced = aiSize - 1;
                int aiSizeExtended = aiSize + 1;
                float tempCostMultiplier = 1.0f;
                int maxVoxIndex = vWorld.vCountXYZ - 1;

                //Precomputed directions
                int iDir_upL = 1 + vCountYZ - vCountZ;
                int iDir_upR = 1 + vCountYZ + vCountZ;
                int iDir_downR = 1 - vCountYZ + vCountZ;
                int iDir_downL = 1 - vCountYZ - vCountZ;

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
                int tempReminderA;
                int tempReminderB;

                int toSearchCount;
                int closestVoxV;
                int closestVoxI;
                bool tempDidAdd;
                byte tempType;

                //Get start and end voxel
                int startVoxI = request.startVoxIndex;
                int endVoxI = request.endVoxIndex;

                if (startVoxI < 0 || startVoxI > maxVoxIndex
                    || endVoxI < 0 || endVoxI > maxVoxIndex) return;//Start or end is out of bounds, no path will exist

                if (snapRadius > 0)
                {
                    toSearchCount = -1;
                    activeDirI = 1;
                    closestVoxV = 0;
                    closestVoxI = 0;

#pragma warning disable CS0162
                    switch (request.pathType)//Mode diff here
                    {
                        case PathType.climbing: startVoxI = SnapToValidVoxelClimbing(startVoxI); break;
                        case PathType.walk: startVoxI = SnapToValidVoxelWalking(startVoxI); break;
                        case PathType.flying: startVoxI = SnapToValidVoxelFlying(startVoxI); break;
                    }

                    toSearchCount = -1;
                    activeDirI = 1;
                    vSearched.Clear();//Reset stuff used in snapping
                    toSearchValue.Clear();
                    toSearchIndex.Clear();

                    switch (request.pathType)//Mode diff here
                    {
                        case PathType.climbing: endVoxI = SnapToValidVoxelClimbing(endVoxI); break;
                        case PathType.walk: endVoxI = SnapToValidVoxelWalking(endVoxI); break;
                        case PathType.flying: endVoxI = SnapToValidVoxelFlying(endVoxI); break;
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
                toSearchValue.Add(62000);
                toSearchIndex.Add(startVoxI);//Temp debug
                toSearchCount++;//Temp debug
                toSearchValue.Add(tempS);
                toSearchIndex.Add(startVoxI);
                vSearched[startVoxI] = 1;//We must set it so its not searched 

                //Do pathfinding
#pragma warning disable CS0162
                switch (request.pathType)//Mode diff here
                {
                    case PathType.climbing: DoPathfindClimbing(); break;
                    case PathType.walk: DoPathfindWalking(); break;
                    case PathType.flying: DoPathfindFlying(); break;
                }
#pragma warning restore CS0162

                //Recreate the path 
                int voxOnPath = closestVoxI;
                int prevDir = 0;
                Vector3 tempVoxPos = Vector3.zero;
                Vector3 halfVox = 0.5f * VoxGlobalSettings.voxelSizeWorld * Vector3.one;
                vSearched.Remove(startVoxI);

                while (true)
                {
                    if (vSearched.TryGetValue(voxOnPath, out activeDirI) == false)
                    {
                        VoxHelpBurst.WVoxIndexToPos(ref voxOnPath, ref tempVoxPos, ref vWorld);
                        _resultPath.Add(new()
                        {
                          position = tempVoxPos + halfVox,
                          normal = GetVoxelNormal(voxOnPath, ref tempVoxPos)
                        });
                        break;
                    }

                    if (activeDirI != prevDir)
                    {
                        VoxHelpBurst.WVoxIndexToPos(ref voxOnPath, ref tempVoxPos, ref vWorld);
                        _resultPath.Add(new()
                        {
                            position = tempVoxPos + halfVox,
                            normal = GetVoxelNormal(voxOnPath, ref tempVoxPos)
                        });
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

                void AddVoxelToPath()
                {
                    //Get distance between activeVoxI and endVoxI
                    //Manhattan distance
                    tempReminderA = activeVoxI % (vCountY * vCountZ);
                    tempReminderB = endVoxI % (vCountY * vCountZ);

                    tempVValue = (ushort)(
                        math.abs((activeVoxI / (vCountY * vCountZ)) - (endVoxI / (vCountY * vCountZ)))
                        + math.abs((tempReminderA / vCountZ) - (tempReminderB / vCountZ))
                        + math.abs((tempReminderA % vCountZ) - (tempReminderB % vCountZ))
                        );

                    //Weird distance
                    //tempVValue = (ushort)(
                    //    math.abs((activeVoxI % vCountZ) - (endVoxI % vCountZ))
                    //    + math.abs((activeVoxI % vCountY) - (endVoxI % vCountY))
                    //    + math.abs((activeVoxI / (vCountY * vCountZ)) - (endVoxI / (vCountY * vCountZ)))
                    //    );

                    if (tempCostMultiplier != 1.0f) tempVValue = (ushort)math.round(tempVValue * tempCostMultiplier);
                    if (voxTypeToMultiply.TryGetValue(activeType, out float tempMultiply) == true) tempVValue = (ushort)math.round(tempVValue * tempMultiply);

                    tempDidAdd = false;
                    //Get where to insert it
                    if (toSearchCount > -1)
                    {
                        for (tempLoop = toSearchCount; tempLoop > -1; tempLoop--)
                        {
                            if (tempVValue >= toSearchValue[tempLoop]) continue;

                            if (tempLoop >= toSearchCount)
                            {
                                tempDidAdd = true;
                                toSearchValue.Add(tempVValue);
                                toSearchIndex.Add(activeVoxI);
                                toSearchCount++;
                                break;
                            }

                            tempDidAdd = true;
                            toSearchValue.InsertRangeWithBeginEnd(tempLoop + 1, tempLoop + 2);
                            toSearchValue[tempLoop + 1] = tempVValue;
                            toSearchIndex.InsertRangeWithBeginEnd(tempLoop + 1, tempLoop + 2);
                            toSearchIndex[tempLoop + 1] = activeVoxI;
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

                Vector3 GetVoxelNormal(int voxI, ref Vector3 voxPos)
                {
                    //Get avg pos of nearby solid voxels
                    Vector3 avgPos = Vector3.zero;
                    Vector3 activePos = Vector3.zero;
                    int avgCount = 0;

                    activeVoxI = voxI + aiSize; if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                        { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                    activeVoxI = voxI - aiSize; if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                        { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                    activeVoxI = voxI + (vCountZ * aiSize); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                        { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                    activeVoxI = voxI - (vCountZ * aiSize); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                        { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                    activeVoxI = voxI + (vCountYZ * aiSize); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                        { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                    activeVoxI = voxI - (vCountYZ * aiSize); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                        { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }

                    tempVoxI = voxI + iDir_upL; CheckShit();
                    tempVoxI = voxI + iDir_upR; CheckShit();
                    tempVoxI = voxI + iDir_downR; CheckShit();
                    tempVoxI = voxI + iDir_downL; CheckShit();
                    tempVoxI = voxI - iDir_upL; CheckShit();
                    tempVoxI = voxI - iDir_upR; CheckShit();
                    tempVoxI = voxI - iDir_downR; CheckShit();
                    tempVoxI = voxI - iDir_downL; CheckShit();

                    void CheckShit()
                    {
                        //+X+Z-Y
                        activeVoxI = tempVoxI + (iDir_upL * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI + (iDir_upR * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI + (iDir_downR * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI + (iDir_downL * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI - (iDir_upL * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI - (iDir_upR * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI - (iDir_downR * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI - (iDir_downL * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }

                        activeVoxI = tempVoxI + aiSizeReduced; if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI - aiSizeReduced; if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI + (vCountZ * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart) 
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI - (vCountZ * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI + (vCountYZ * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart) 
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                        activeVoxI = tempVoxI - (vCountYZ * aiSizeReduced); if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart)
                            { VoxHelpBurst.WVoxIndexToPos(ref activeVoxI, ref activePos, ref vWorld); avgPos += activePos; avgCount++; }
                    }

                    //Get dir to avg nearby solid voxel pos
                    return (voxPos - (avgPos / avgCount)).normalized;

                }

                #region ModeClimbing

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
                    //Return if already searched
                    if (vSearched.TryAdd(activeVoxI, activeDirI) == false) return;

                    //Prevent out of bounds
                    tempVoxI = activeVoxI + (iDir_upL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_upR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_downR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_downL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_upL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_upR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_downR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_downL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;

                    //Check if voxel is overlapping
                    if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart) return;

                    activeType = 0;
                    for (tempLoop = 1; tempLoop < aiSize; tempLoop++)
                    {
                        if (vTypes[activeVoxI + tempLoop] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI - tempLoop] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI + (vCountZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI - (vCountZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI + (vCountYZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI - (vCountYZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X+Z-Y
                        if (vTypes[activeVoxI + (iDir_upL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X+Z+Y
                        if (vTypes[activeVoxI + (iDir_upR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X-Z+Y
                        if (vTypes[activeVoxI + (iDir_downR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X-Z-Y
                        if (vTypes[activeVoxI + (iDir_downL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X+Z-Y
                        if (vTypes[activeVoxI - (iDir_upL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X+Z+Y
                        if (vTypes[activeVoxI - (iDir_upR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X-Z+Y
                        if (vTypes[activeVoxI - (iDir_downR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X-Z-Y
                        if (vTypes[activeVoxI - (iDir_downL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                    }

                    if (activeType > 0) return;

                    //Check if voxel is close enough to any surface, do we really need to check this much?
                    activeType = vTypes[activeVoxI + aiSize]; if (activeType > VoxGlobalSettings.solidTypeStart) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI - aiSize]; if (activeType > VoxGlobalSettings.solidTypeStart) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI + (vCountZ * aiSize)]; if (activeType > VoxGlobalSettings.solidTypeStart) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI - (vCountZ * aiSize)]; if (activeType > VoxGlobalSettings.solidTypeStart) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI + (vCountYZ * aiSize)]; if (activeType > VoxGlobalSettings.solidTypeStart) { goto SkipReturnInvalid; }
                    activeType = vTypes[activeVoxI - (vCountYZ * aiSize)]; if (activeType > VoxGlobalSettings.solidTypeStart) { goto SkipReturnInvalid; }

                    tempVoxI = activeVoxI + iDir_upL; if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI + iDir_upR; if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI + iDir_downR; if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI + iDir_downL; if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI - iDir_upL; if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI - iDir_upR; if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI - iDir_downR; if (CheckShit() == true) { goto SkipReturnInvalid; };
                    tempVoxI = activeVoxI - iDir_downL; if (CheckShit() == true) { goto SkipReturnInvalid; };

                    bool CheckShit()
                    {
                        //+X+Z-Y
                        activeType = vTypes[tempVoxI + (iDir_upL * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //+X+Z+Y
                        activeType = vTypes[tempVoxI + (iDir_upR * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //+X-Z+Y
                        activeType = vTypes[tempVoxI + (iDir_downR * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //+X-Z-Y
                        activeType = vTypes[tempVoxI + (iDir_downL * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //-X+Z-Y
                        activeType = vTypes[tempVoxI - (iDir_upL * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //-X+Z+Y
                        activeType = vTypes[tempVoxI - (iDir_upR * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //-X-Z+Y
                        activeType = vTypes[tempVoxI - (iDir_downR * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //-X-Z-Y
                        activeType = vTypes[tempVoxI - (iDir_downL * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }

                        activeType = vTypes[tempVoxI + aiSizeReduced]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI - aiSizeReduced]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI + (vCountZ * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI - (vCountZ * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI + (vCountYZ * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI - (vCountYZ * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }

                        return false;
                    }

                    return;
                SkipReturnInvalid:;

                    //Voxel is valid, add it to the path
                    tempType = vTypes[activeVoxI];
                    if (tempType > 0) activeType = tempType;//If inside soft voxel, use it as type

                    AddVoxelToPath();
                }

                //activeVoxI + (iDir_upL * tempLoop)
                //activeVoxI + (iDir_upR * tempLoop)
                //activeVoxI + (iDir_downR * tempLoop)
                //activeVoxI + (iDir_downL * tempLoop)
                //activeVoxI - (iDir_upL * tempLoop)
                //activeVoxI - (iDir_upR * tempLoop)
                //activeVoxI - (iDir_downR * tempLoop)
                //activeVoxI - (iDir_downL * tempLoop)

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

                #endregion ModeClimbing

                #region ModeFlying

                void DoPathfindFlying()
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
                        CheckVoxIndexFlying();

                        activeVoxI = tempI - 1;
                        activeDirI = 2;
                        CheckVoxIndexFlying();

                        activeVoxI = tempI + vCountZ;
                        activeDirI = 3;
                        CheckVoxIndexFlying();

                        activeVoxI = tempI - vCountZ;
                        activeDirI = 4;
                        CheckVoxIndexFlying();

                        activeVoxI = tempI + vCountYZ;
                        activeDirI = 5;
                        CheckVoxIndexFlying();

                        activeVoxI = tempI - vCountYZ;
                        activeDirI = 6;
                        CheckVoxIndexFlying();
                    }
                }

                void CheckVoxIndexFlying()
                {
                    //Return if already searched
                    if (vSearched.TryAdd(activeVoxI, activeDirI) == false) return;

                    //Prevent out of bounds
                    tempVoxI = activeVoxI + (iDir_upL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_upR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_downR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_downL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_upL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_upR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_downR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_downL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;

                    //Check if voxel is overlapping
                    if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart) return;

                    activeType = 0;
                    for (tempLoop = 1; tempLoop < aiSize; tempLoop++)
                    {
                        if (vTypes[activeVoxI + tempLoop] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI - tempLoop] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI + (vCountZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI - (vCountZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI + (vCountYZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI - (vCountYZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X+Z-Y
                        if (vTypes[activeVoxI + (iDir_upL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X+Z+Y
                        if (vTypes[activeVoxI + (iDir_upR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X-Z+Y
                        if (vTypes[activeVoxI + (iDir_downR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X-Z-Y
                        if (vTypes[activeVoxI + (iDir_downL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X+Z-Y
                        if (vTypes[activeVoxI - (iDir_upL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X+Z+Y
                        if (vTypes[activeVoxI - (iDir_upR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X-Z+Y
                        if (vTypes[activeVoxI - (iDir_downR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X-Z-Y
                        if (vTypes[activeVoxI - (iDir_downL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                    }

                    if (activeType > 0) return;

                    //Voxel is valid, add it to the path
                    activeType = vTypes[activeVoxI];

                    AddVoxelToPath();
                }

                int SnapToValidVoxelFlying(int snapThis)
                {
                    activeVoxI = snapThis; CheckVoxIndexFlying(); if (toSearchCount > -1) return activeVoxI;

                    for (int rad = 1; rad < snapRadius; rad++)
                    {
                        activeVoxI = snapThis + rad; CheckVoxIndexFlying(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - rad; CheckVoxIndexFlying(); if (toSearchCount > -1) return activeVoxI;

                        activeVoxI = snapThis + (vCountZ * rad); CheckVoxIndexFlying(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - (vCountZ * rad); CheckVoxIndexFlying(); if (toSearchCount > -1) return activeVoxI;

                        activeVoxI = snapThis + (vCountYZ * rad); CheckVoxIndexFlying(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - (vCountYZ * rad); CheckVoxIndexFlying(); if (toSearchCount > -1) return activeVoxI;
                    }

                    return snapThis;
                }

                #endregion ModeFlying


                #region ModeWalking

                void DoPathfindWalking()
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
                        CheckVoxIndexWalking();

                        activeVoxI = tempI - 1;
                        activeDirI = 2;
                        CheckVoxIndexWalking();

                        activeVoxI = tempI + vCountZ;
                        activeDirI = 3;
                        CheckVoxIndexWalking();

                        activeVoxI = tempI - vCountZ;
                        activeDirI = 4;
                        CheckVoxIndexWalking();

                        activeVoxI = tempI + vCountYZ;
                        activeDirI = 5;
                        CheckVoxIndexWalking();

                        activeVoxI = tempI - vCountYZ;
                        activeDirI = 6;
                        CheckVoxIndexWalking();
                    }
                }

                void CheckVoxIndexWalking()
                {
                    //Return if already searched
                    if (vSearched.TryAdd(activeVoxI, activeDirI) == false) return;

                    //Prevent out of bounds
                    tempVoxI = activeVoxI + (iDir_upL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_upR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_downR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI + (iDir_downL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_upL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_upR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_downR * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;
                    tempVoxI = activeVoxI - (iDir_downL * aiSizeExtended); if (tempVoxI < 0 || tempVoxI > maxVoxIndex) return;

                    //Check if voxel is overlapping
                    if (vTypes[activeVoxI] > VoxGlobalSettings.solidTypeStart) return;

                    activeType = 0;
                    for (tempLoop = 1; tempLoop < aiSize; tempLoop++)
                    {
                        if (vTypes[activeVoxI - (vCountZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 2; break; }
                        if (vTypes[activeVoxI + tempLoop] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI - tempLoop] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI + (vCountZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI + (vCountYZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                        if (vTypes[activeVoxI - (vCountYZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X+Z-Y
                        if (vTypes[activeVoxI + (iDir_upL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X+Z+Y
                        if (vTypes[activeVoxI + (iDir_upR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X-Z+Y
                        if (vTypes[activeVoxI + (iDir_downR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //+X-Z-Y
                        if (vTypes[activeVoxI + (iDir_downL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X+Z-Y
                        if (vTypes[activeVoxI - (iDir_upL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X+Z+Y
                        if (vTypes[activeVoxI - (iDir_upR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X-Z+Y
                        if (vTypes[activeVoxI - (iDir_downR * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }

                        //-X-Z-Y
                        if (vTypes[activeVoxI - (iDir_downL * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 1; break; }
                    }

                    //Do falling
                    if (activeDirI == 4 && activeType != 2)
                    {
                        for (tempLoop = aiSize; tempLoop < snapRadiusExtended; tempLoop++)
                        {
                            if (vTypes[activeVoxI - (vCountZ * tempLoop)] > VoxGlobalSettings.solidTypeStart) { activeType = 2; break; }
                        }

                        if (activeType != 2) return;

                        tempLoop = 4;
                        goto SkipReturnInvalid;
                    }

                    if (activeType > 0) return;

                    //Check if voxel is close enough to any surface
                    if (activeDirI == 4) goto SkipReturnInvalid;

                    for (tempLoop = 1; tempLoop < snapRadius; tempLoop++)
                    {
                        tempVoxI = activeVoxI - (vCountZ * tempLoop); if (CheckShit() == true) { goto SkipReturnInvalid; };
                    }

                    bool CheckShit()
                    {
                        //+X+Z-Y
                        activeType = vTypes[tempVoxI + (iDir_upL * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //+X-Z-Y
                        activeType = vTypes[tempVoxI + (iDir_downL * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //-X+Z-Y
                        activeType = vTypes[tempVoxI - (iDir_upL * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        //-X-Z-Y
                        activeType = vTypes[tempVoxI - (iDir_downL * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }

                        activeType = vTypes[tempVoxI + aiSizeReduced]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI - aiSizeReduced]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI - (vCountZ * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI + (vCountYZ * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }
                        activeType = vTypes[tempVoxI - (vCountYZ * aiSizeReduced)]; if (activeType > VoxGlobalSettings.solidTypeStart) { return true; }

                        return false;
                    }

                    return;
                SkipReturnInvalid:;

                    //Voxel is valid, add it to the path
                    tempType = vTypes[activeVoxI];
                    if (tempType > 0) activeType = tempType;//If inside soft voxel, use it as type
                    tempCostMultiplier = 1.0f + (0.2f * (tempLoop - 1));

                    AddVoxelToPath();
                }

                //activeVoxI + (iDir_upL * tempLoop)
                //activeVoxI + (iDir_upR * tempLoop)
                //activeVoxI + (iDir_downR * tempLoop)
                //activeVoxI + (iDir_downL * tempLoop)
                //activeVoxI - (iDir_upL * tempLoop)
                //activeVoxI - (iDir_upR * tempLoop)
                //activeVoxI - (iDir_downR * tempLoop)
                //activeVoxI - (iDir_downL * tempLoop)

                int SnapToValidVoxelWalking(int snapThis)
                {
                    activeVoxI = snapThis; CheckVoxIndexWalking(); if (toSearchCount > -1) return activeVoxI;

                    for (int rad = 1; rad < snapRadius; rad++)
                    {
                        activeVoxI = snapThis + rad; CheckVoxIndexWalking(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - rad; CheckVoxIndexWalking(); if (toSearchCount > -1) return activeVoxI;

                        activeVoxI = snapThis + (vCountZ * rad); CheckVoxIndexWalking(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - (vCountZ * rad); CheckVoxIndexWalking(); if (toSearchCount > -1) return activeVoxI;

                        activeVoxI = snapThis + (vCountYZ * rad); CheckVoxIndexWalking(); if (toSearchCount > -1) return activeVoxI;
                        activeVoxI = snapThis - (vCountYZ * rad); CheckVoxIndexWalking(); if (toSearchCount > -1) return activeVoxI;
                    }

                    return snapThis;
                }

                #endregion ModeWalking
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

        //########################Custom Editor######################################
        [CustomEditor(typeof(VoxPathfinder))]
        public class YourScriptEditor : Editor
        {
            private static readonly string[] hiddenFields = new string[]
            {
                "m_Script"
            };

            public override void OnInspectorGUI()
            {
                serializedObject.Update();

                EditorGUILayout.Space();

                DrawPropertiesExcluding(serializedObject, hiddenFields);//Script is not visible at runtime, so we dont need to disable stuff at runtime
                if (Application.isPlaying == true)
                    EditorGUILayout.HelpBox("Changes made at runtime is likely to not have any affect!", MessageType.Info);

                //Apply changes
                serializedObject.ApplyModifiedProperties();
            }
        }
#endif
    }
}
