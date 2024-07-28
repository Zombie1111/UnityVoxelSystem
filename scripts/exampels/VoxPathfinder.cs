using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

namespace zombVoxels
{
    public class VoxPathfinder : MonoBehaviour
    {
        #region SetupPathfinder

        private VoxGlobalHandler voxHandler;

        private void Awake()
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
                activeRequest = new(Allocator.Persistent)
            };
        }

        private void ClearAllocatedMemory()
        {
            if (fp_job.activeRequest.IsCreated == true) fp_job.activeRequest.Dispose();
        }

        #endregion SetupPathfinder




        #region HandlePathRequest

        [System.Serializable]
        public struct PathRequest
        {
            public int startVoxIndex;
            public int endVoxIndex;
            public int radius;
            public PathType pathType;
        }

        private struct PathPendingRequest
        {
            public int startVoxIndex;
            public int endVoxIndex;
            public int radius;
            public PathType pathType;
            public int requestId;
        }

        public enum PathType
        {
            walk,
            climbing,
            flying
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
                 requestId = nextRequestId
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
            fp_job.activeRequest.Value = activeRequest;
            fp_handle = fp_job.Schedule();
        }

        private void OnGlobalReadAccessStop()
        {
            if (fp_jobIsActive == false) return;

            fp_jobIsActive = false;
            fp_handle.Complete();
            OnPathRequestCompleted?.Invoke(fp_job.activeRequest.Value.requestId);
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

        private FindPath_work fp_job;
        private JobHandle fp_handle;
        private bool fp_jobIsActive = false;

        [BurstCompile]
        private struct FindPath_work : IJob
        {
            public NativeReference<PathPendingRequest> activeRequest;
            /// <summary>
            /// The type this global voxel is, always air if voxsCount[X] is 0
            /// </summary>
            public NativeArray<byte> voxsType;
            public NativeReference<VoxWorld> voxWorld;

            public unsafe void Execute()
            {
                
            }
        }

        #endregion ActualPathfinding
    }
}
