using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace zombVoxels
{
    public class VoxPathNpc : MonoBehaviour
    {
        #region SetupPathNpc

        [SerializeField] private Transform startTarget;
        [SerializeField] private Transform endTarget;
        [SerializeField] private bool discardPendingUpdatesOnRequest = true;
        [SerializeField] private VoxPathfinder.PathRequest pathProperties = new();
        [SerializeField] private VoxPathfinder pathfinder = null;
        private VoxGlobalHandler globalHandler;

        private void Awake()
        {
            globalHandler = VoxGlobalHandler.TryGetValidGlobalHandler();
            if (globalHandler == null) return;
            if (pathfinder == null) pathfinder = GameObject.FindAnyObjectByType<VoxPathfinder>(FindObjectsInactive.Include);
            SetPathfinder(pathfinder);
        }

        /// <summary>
        /// Sets the pathfinder to use when requesting path
        /// </summary>
        public void SetPathfinder(VoxPathfinder newPathfinder)
        {
            if (pathfinder != null)
            {
                if (pathfinder != newPathfinder) DiscardPendingUpdates(true);
                pathfinder.OnPathRequestCompleted -= OnPathRequestComplete;
                pathfinder.OnPathRequestCompleted -= OnPathRequestDiscarded;
            }

            if (newPathfinder == null)
            {
                pathfinder = null;
                return;
            }

            pathfinder = newPathfinder;
            pathfinder.OnPathRequestCompleted += OnPathRequestComplete;//Is it worth only being subscribed while we have a pending request?
            pathfinder.OnPathRequestCompleted += OnPathRequestDiscarded;
        }

        private void OnDestroy()
        {
            SetPathfinder(null);
        }

        private void Update()
        {
            if (startTarget == null || endTarget == null) return;

            int resultV = 0;
            Vector3 pos = startTarget.position;
            VoxHelpBurst.PosToWVoxIndex(ref pos, ref resultV, ref globalHandler.voxWorld);
            pathProperties.startVoxIndex = resultV;

            pos = endTarget.position;
            VoxHelpBurst.PosToWVoxIndex(ref pos, ref resultV, ref globalHandler.voxWorld);
            pathProperties.endVoxIndex = resultV;

            RequestUpdatePath();
        }

        #endregion SetupPathNpc




        #region HandlePathRequesting

        private HashSet<int> pendingRequestIds = new();

        /// <summary>
        /// Updates the path as soon as possible
        /// </summary>
        public void RequestUpdatePath()
        {
            if (pathfinder == null)
            {
                Debug.LogError("No pathfinder is assigned for " + transform.name);
                return;
            }

            if (discardPendingUpdatesOnRequest == true) DiscardPendingUpdates(false);
            pendingRequestIds.Add(pathfinder.RequestPath(ref pathProperties));
        }

        /// <summary>
        /// Discards any pending path updated requested
        /// </summary>
        public void DiscardPendingUpdates(bool instantDiscard = false)
        {
            if (pathfinder != null)
            {
                foreach (int requestId in pendingRequestIds)
                {
                    pathfinder.DiscardPendingRequest(requestId);
                }
            }

            if (instantDiscard == false) return;

            pendingRequestIds.Clear();
        }

        private void OnPathRequestComplete(int requestId)
        {
            if (pendingRequestIds.Remove(requestId) == false) return;

            //Debug log path
            Vector3 prevPos = endTarget.position; 
            Vector3 nowPos = Vector3.zero; 
            foreach (int voxI in pathfinder.fp_job._resultPath)
            {
                int voxII = voxI;
                VoxHelpBurst.WVoxIndexToPos(ref voxII, ref nowPos, ref globalHandler.voxWorld);
                Debug.DrawLine(prevPos, nowPos, Color.red, 0.1f, true);
                prevPos = nowPos;
            }

            int voxOnPath;
            byte activeDirI;
            int vCountZ = globalHandler.voxWorld.vCountZ;
            int vCountYZ = globalHandler.voxWorld.vCountYZ;

            foreach (var vox in pathfinder.fp_job._voxsSearched)
            {
                voxOnPath = vox.Key;
                activeDirI = vox.Value;
                VoxHelpBurst.WVoxIndexToPos(ref voxOnPath, ref nowPos, ref globalHandler.voxWorld);

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

                VoxHelpBurst.WVoxIndexToPos(ref voxOnPath, ref prevPos, ref globalHandler.voxWorld);
                Debug.DrawLine(prevPos, nowPos, Color.yellow, 0.0f, true);
            }
        }

        private void OnPathRequestDiscarded(int requestId)
        {
            if (pendingRequestIds.Remove(requestId) == false) return;

        }

        #endregion HandlePathRequesting
    }
}
