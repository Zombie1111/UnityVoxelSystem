using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using zombVoxels;

public class demoStartEndPath : MonoBehaviour
{
    [SerializeField] private Transform pathStart;
    [SerializeField] private Transform pathEnd;
    private VoxPathNpc pathNpc;

    private void Start()
    {
        pathNpc = GetComponent<VoxPathNpc>();
    }

    private void Update()
    {
        if (pathNpc.pendingRequestIds.Count > 0) return;
        pathNpc.SetPathTargetStartEndPosition(pathStart.position, pathEnd.position);
        pathNpc.RequestUpdatePath();
    }
}
