using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace zombVoxels
{
    public class VoxPathNpc : MonoBehaviour
    {
        #region SetupPathNpc

        [SerializeField] private bool autoUpdatePath = true;
        [SerializeField] private bool discardPendingUpdatesOnRequest = true;
        [SerializeField] private VoxPathfinder.PathRequest pathProperties = new();
        [SerializeField] private VoxPathfinder pathfinder = null;

        private void Awake()
        {
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


        }

        private void OnPathRequestDiscarded(int requestId)
        {
            if (pendingRequestIds.Remove(requestId) == false) return;

        }

        #endregion HandlePathRequesting
    }
}
