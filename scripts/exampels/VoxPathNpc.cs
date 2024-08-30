//https://github.com/Zombie1111/UnityVoxelSystem
using System.Collections.Generic;
using UnityEngine;

namespace zombVoxels
{
    public class VoxPathNpc : MonoBehaviour
    {
        #region SetupPathNpc

        [Tooltip("Should pending path requests be discarded when a new request is made?")] [SerializeField] private bool discardPendingUpdatesOnRequest = true;
        public VoxPathfinder.PathRequest pathProperties = new();
        [Tooltip("The pathfinder to use, assigned automatically if left unassigned in editor")] [SerializeField] private VoxPathfinder pathfinder = null;
        [Tooltip("If <= 0.0f, no path simplification")] [SerializeField] private float pathSimplificationTolerance = 0.1f;

        [Tooltip("If > 0.0f, the path will be snapped to the ground if ground exist within this distance")][SerializeField] private float pathSnapRayLenght = 0.0f;
        [Tooltip("The maximum radius of the pathSnap ray, unused if pathSnapRayLenght <= 0.0f")][SerializeField] private float pathSnapRayRadius = 0.5f;
        [Tooltip("Layermask for the pathSnap ray, unused if pathSnapRayLenght <= 0.0f")] [SerializeField] private LayerMask pathSnapMask = Physics.AllLayers;

#if UNITY_EDITOR
        [SerializeField] private DebugMode debugMode = DebugMode.None;

        private enum DebugMode
        {
            None,
            DrawPath,
            DrawSearched
        }
#endif

        private void Start()
        {
            if (pathfinder == null) pathfinder = GameObject.FindAnyObjectByType<VoxPathfinder>(FindObjectsInactive.Include);
            SetPathfinder(pathfinder);
        }

        private void OnDestroy()
        {
            SetPathfinder(null);
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

        #endregion SetupPathNpc




        #region HandlePathRequesting

        /// <summary>
        /// Contains the id of any pending request
        /// </summary>
        [System.NonSerialized] public HashSet<int> pendingRequestIds = new();

        /// <summary>
        /// Sets the target start and end position for the path. You should also call RequestUpdatePath() to actually update the path
        /// </summary>
        public void SetPathTargetStartEndPosition(Vector3 startPos, Vector3 endPos)
        {
            int resultV = 0;
            VoxHelpBurst.PosToWVoxIndex(ref startPos, ref resultV, ref pathfinder.voxHandler.voxWorld);
            pathProperties.startVoxIndex = resultV;

            VoxHelpBurst.PosToWVoxIndex(ref endPos, ref resultV, ref pathfinder.voxHandler.voxWorld);
            pathProperties.endVoxIndex = resultV;
        }

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
        public void DiscardPendingUpdates(bool discardAnyActiveRequest = false)
        {
            if (pathfinder != null)
            {
                foreach (int requestId in pendingRequestIds)
                {
                    pathfinder.DiscardPendingRequest(requestId);
                }
            }

            if (discardAnyActiveRequest == false) return;

            pendingRequestIds.Clear();
        }

        /// <summary>
        /// The path node positions of latest path found (if any), end to start
        /// </summary>
        [System.NonSerialized] public List<Vector3> pathResultPos = new();

        /// <summary>
        /// The path node normals of latest path found (Always same lenght as pathResultPos), end to start
        /// </summary>
        [System.NonSerialized] public List<Vector3> pathResultNor = new();
        private List<int> pathNodesToKeep = new();

        private void OnPathRequestCompleted(int requestId)
        {
            if (pendingRequestIds.Remove(requestId) == false) return;

            //Extract path result
            pathResultPos.Clear();
            pathResultNor.Clear();
            foreach (VoxPathfinder.PathNode pNode in pathfinder.fp_job._resultPath)
            {
                pathResultPos.Add(pNode.position);
                pathResultNor.Add(pNode.normal);
            }

            if (pathSimplificationTolerance > 0.0f)
            {
                LineUtility.Simplify(pathResultPos, pathSimplificationTolerance, pathNodesToKeep);

                for (int i = pathResultPos.Count - 1; i >= 0; i--)
                {
                    if (pathNodesToKeep.Contains(i) == true) continue;

                    pathResultPos.RemoveAt(i);
                    pathResultNor.RemoveAt(i);
                }
            }

            //Snap path to ground
            if (pathSnapRayLenght > 0.0f)
            {
                for (int i = 0; i < pathResultPos.Count; i++)
                {
                    var nHit = VoxHelpFunc.WideRaycast(pathResultPos[i], -pathResultNor[i], pathSnapRayLenght, pathSnapMask, pathSnapRayRadius, 0.05f);
                    if (nHit.collider == null) continue;
            
                    pathResultPos[i] = nHit.point;
                    pathResultNor[i] = nHit.normal;
                }
            }

#if UNITY_EDITOR
            DoPathDebug(true);
#endif
        }

        private void OnPathRequestDiscarded(int requestId)
        {
            if (pendingRequestIds.Remove(requestId) == false) return;
        }

        #endregion HandlePathRequesting




        #region PathApi

        /// <summary>
        /// Returns a position on the path at the given distance from the path start
        /// (Returns path end if the given distance exceeds path lenght), throws if no path has been found yet
        /// </summary>
        public Vector3 GetPositionOnPath(float disFromStart, out int pathIndex)
        {
            float tempDis;
            float totDis = 0.0f;

            for (pathIndex = pathResultPos.Count - 1; pathIndex > 0; pathIndex--)
            {
                tempDis = (pathResultPos[pathIndex - 1] - pathResultPos[pathIndex]).magnitude;
                if (totDis + tempDis < disFromStart)
                {
                    totDis += tempDis;
                    continue;
                }

                return pathResultPos[pathIndex] + ((pathResultPos[pathIndex - 1] - pathResultPos[pathIndex]).normalized * (disFromStart - totDis));
            }

            if (pathResultPos.Count == 0) throw new System.Exception("No path has been found for " + transform.name + " yet!");
            return pathResultPos[0];//pathIndex is always already 0
        }

        /// <summary>
        /// Returns the closest point on the path to the given position
        /// (If no path has been found yet, returns given position, pathIndex = -1 and disToPath = float.MaxValue)
        /// </summary>
        public Vector3 GetClosestPositionOnPath(Vector3 pos, out int pathIndex, out float disToPath)
        {
            Vector3 bestPos = pos;
            disToPath = float.MaxValue;
            pathIndex = -1;
            Vector3 tempPos;
            float tempDis;

            for (int i = pathResultPos.Count - 1; i > 0; i--)
            {
                tempPos = VoxHelpFunc.ClosestPointOnLine(pathResultPos[i], pathResultPos[i - 1], ref pos);
                tempDis = (tempPos - pos).magnitude;

                if (tempDis > disToPath) continue;

                disToPath = tempDis;
                bestPos = tempPos;
                pathIndex = i;
            }

            return bestPos;
        }

        /// <summary>
        /// Returns the distance from path start to pathIndex + distance from pathIndex to the given position
        /// (Returns 0.0f if no path has been found yet, throws if pathIndex is out of bounds)
        /// </summary>
        public float GetPathDistanceFromStart(Vector3 pos, int pathIndex)
        {
            float dis = 0.0f;

            for (int i = pathResultPos.Count - 1; i >= 0; i--)
            {
                if (i == pathIndex) return dis + (pos - pathResultPos[i]).magnitude;

                dis += (pathResultPos[i - 1] - pathResultPos[i]).magnitude;
            }

            return dis;
        }

        /// <summary>
        /// Returns a position on the path that is disToMove further away from path start than currentPos
        /// (Returns path end if currentPos is too close to path end, throws if no path has been found yet)
        /// </summary>
        public Vector3 GetOtherPositionFurtherFromStart(Vector3 currentPos, float currentDisFromStart, float disToMove, out float otherDisFromStart, out int otherPathIndex, bool unclamped = false)
        {
            otherDisFromStart = currentDisFromStart + disToMove;
            Vector3 otherPos;
            float actualDisMoved;

            while (true)
            {
                otherPos = GetPositionOnPath(otherDisFromStart, out otherPathIndex);
                actualDisMoved = (otherPos - currentPos).magnitude;
                if (disToMove > actualDisMoved && otherPathIndex > 0) otherDisFromStart += (disToMove - actualDisMoved) + 0.01f;
                else break;
            }

            if (unclamped == true && disToMove > actualDisMoved)
            {
                float disLeft = disToMove - actualDisMoved;
                otherPos += (otherPos - (pathResultPos.Count > 1 ? pathResultPos[1] : currentPos)).normalized * disLeft;
                otherDisFromStart += disLeft;//Not needed but I think this is the expected behaviour
            }

            return otherPos;
        }

        public class PathOrientation
        {
            public Vector3 forward;
            public Vector3 up;
        }

        /// <summary>
        /// Returns the path normal and forward at the given position, throws if pathIndex is out of bounds
        /// </summary>
        public PathOrientation GetPathOrientationAtPosition(Vector3 pos, int pathIndex)
        {
            if (pathIndex == 0)
            {
                return new()
                {
                    forward = pathResultPos[pathIndex] - (pathIndex + 1 < pathResultPos.Count ? pathResultPos[pathIndex + 1] : pos),
                    up = pathResultNor[pathIndex]
                };
            }

            Vector3 forward = pathResultPos[pathIndex - 1] - pathResultPos[pathIndex];
            float nodeDis = forward.magnitude;
            float posDis = (pos - pathResultPos[pathIndex]).magnitude;

            return new()
            {
                forward = forward.normalized,
                up = Vector3.Slerp(pathResultNor[pathIndex], pathResultNor[pathIndex - 1], posDis / nodeDis)
            };
        }

        /// <summary>
        /// Returns true if a path has been found and pathIndex is within bounds
        /// (Pass 0 to only check if path has been found, since 0 is always inside bounds if path exist)
        /// </summary>
        public bool IsPathIndexValid(int pathIndex)
        {
            return pathIndex >= 0 && pathIndex < pathResultPos.Count;
        }

        /// <summary>
        /// Clears any path found
        /// </summary>
        public void ClearPathResult()
        {
            pathResultPos.Clear();
            pathResultNor.Clear();
        }

        #endregion PathApi




        #region EditorShit

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (Application.isPlaying == false) return;
            DoPathDebug(false);
        }

        private void DoPathDebug(bool drawSearched = false)
        {
            //Debug draw path
            if (debugMode == DebugMode.None || pathfinder == null) return;

            Vector3 prevPos = Vector3.zero;
            Vector3 nowPos;

            for (int i = 0; i < pathResultPos.Count; i++)
            {
                nowPos = pathResultPos[i];
                if (prevPos != Vector3.zero) Debug.DrawLine(prevPos, nowPos, Color.red, 0.0f, true);
                Debug.DrawLine(nowPos, nowPos + (pathResultNor[i] * VoxGlobalSettings.voxelSizeWorld), Color.yellow, 0.0f, true);
                prevPos = nowPos;
            }

            //Debug draw searched voxels
            if (debugMode == DebugMode.DrawPath || drawSearched == false) return;

            int voxOnPath;
            byte activeDirI;
            int vCountZ = pathfinder.voxHandler.voxWorld.vCountZ;
            int vCountYZ = pathfinder.voxHandler.voxWorld.vCountYZ;
            nowPos = Vector3.zero;
            float delta = Time.deltaTime * 2.0f;

            foreach (var vox in pathfinder.fp_job._voxsSearched)
            {
                voxOnPath = vox.Key;
                activeDirI = vox.Value;
                VoxHelpBurst.WVoxIndexToPos(ref voxOnPath, ref nowPos, ref pathfinder.voxHandler.voxWorld);

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

                VoxHelpBurst.WVoxIndexToPos(ref voxOnPath, ref prevPos, ref pathfinder.voxHandler.voxWorld);
                Debug.DrawLine(prevPos, nowPos, Color.yellow, delta, true);
            }
        }
#endif

        #endregion EditorShit
    }
}
