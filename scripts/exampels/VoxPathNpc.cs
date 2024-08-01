using System.Collections;
using System.Collections.Generic;
using Unity.Collections.NotBurstCompatible;
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
        [SerializeField] private float pathSimplificationTolerance = 0.1f;
        private VoxGlobalHandler globalHandler;

#if UNITY_EDITOR
        [SerializeField] private DebugMode debugMode = DebugMode.None;

        private enum DebugMode
        {
            None,
            DrawPath,
            DrawSearched
        }
#endif

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
                pathfinder.OnPathRequestCompleted -= OnPathRequestCompleted;
                pathfinder.OnPathRequestDiscarded -= OnPathRequestDiscarded;
            }

            if (newPathfinder == null)
            {
                pathfinder = null;
                return;
            }

            pathfinder = newPathfinder;
            pathfinder.OnPathRequestCompleted += OnPathRequestCompleted;//Is it worth only being subscribed while we have a pending request?
            pathfinder.OnPathRequestDiscarded += OnPathRequestDiscarded;
        }

        private void OnDestroy()
        {
            SetPathfinder(null);
        }

        private void Update()
        {
            if (startTarget == null || endTarget == null) return;

#if UNITY_EDITOR
            if (debugMode != DebugMode.None) DoPathDebug(false);
#endif

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

        /// <summary>
        /// The latest path found, end to start
        /// </summary>
        [System.NonSerialized] public List<Vector3> pathResult = new();

        private void OnPathRequestCompleted(int requestId)
        {
            if (pendingRequestIds.Remove(requestId) == false) return;

            //Extract path result
            pathResult.Clear();
            foreach (Vector3 pos in pathfinder.fp_job._resultPath)
            {
                pathResult.Add(pos);
            }

            if (pathSimplificationTolerance > 0.0f) LineUtility.Simplify(pathResult, pathSimplificationTolerance, pathResult);

#if UNITY_EDITOR
            if (debugMode != DebugMode.None) DoPathDebug(true);
#endif
        }

        private void OnPathRequestDiscarded(int requestId)
        {
            if (pendingRequestIds.Remove(requestId) == false) return;

        }

        #endregion HandlePathRequesting

#if UNITY_EDITOR
        private void DoPathDebug(bool drawSearched = false)
        {
            //Debug draw path
            if (debugMode == DebugMode.None) return;

            Vector3 prevPos = endTarget.position;
            foreach (var voxPos in pathResult)
            {
                Debug.DrawLine(prevPos, voxPos, Color.red, 0.0f, true);
                prevPos = voxPos;
            }

            //Debug draw searced voxels
            if (debugMode == DebugMode.DrawPath || drawSearched == false) return;

            int voxOnPath;
            byte activeDirI;
            int vCountZ = globalHandler.voxWorld.vCountZ;
            int vCountYZ = globalHandler.voxWorld.vCountYZ;
            Vector3 nowPos = Vector3.zero;
            float delta = Time.deltaTime * 2.0f;

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
                Debug.DrawLine(prevPos, nowPos, Color.yellow, delta, true);
            }
        }
#endif
    }
}
