using System.Collections.Generic;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.iOS;
using UnityEngine.Jobs;

namespace zombVoxels
{
    public class VoxGlobalHandler : MonoBehaviour
    {
        #region VoxelObjectManagement

        [System.NonSerialized] public Dictionary<int, VoxObject> colIdToVoxObject = new();
        private VoxSavedData savedVoxData;
        [System.NonSerialized] private bool voxIsValid = false;

        private void Awake()
        {
            if (ValidateVoxelSystem() == false) return;
            SetupVoxelSystem();
        }

        private void OnDestroy()
        {
            ClearVoxelSystem();
        }

        /// <summary>
        /// Creates a voxObject from the given collider unless colId already exist
        /// </summary>
        public void CreateVoxObjectFromCollider(Collider col, int colId, byte objVoxType = 0)
        {
            //Add colId to transform
            Transform trans = col.transform;
            if (transToColIds.TryGetValue(trans, out var voxT) == false)
            {
#if UNITY_EDITOR
                if (Application.isPlaying == true)
#endif    
                    newVoxelTrans.Add(trans);

                voxT = new()
                {
                    colIds = new(),
                    transIndex = -1//We set it later when we actually create the trans
                };

                transToColIds.Add(trans, voxT);
            }

            voxT.colIds.Add(colId);

            //Voxelize the collider if needed
            if (colIdToVoxObject.ContainsKey(colId) == true) return;

            VoxObject voxObj = VoxHelpFunc.VoxelizeCollider(col, objVoxType);
            colIdToVoxObject[colId] = voxObj;

#if UNITY_EDITOR
            if (Application.isPlaying == false) savedVoxData.AddVoxObject(colId, voxObj);
#endif
        }

        /// <summary>
        /// Removes a voxObject with the given colId
        /// </summary>
        public void DestroyVoxObject(int colId)
        {
            if (colIdToVoxObject.Remove(colId) == false) return;

#if UNITY_EDITOR
            if (Application.isPlaying == false) savedVoxData.RemoveVoxObject(colId);
#endif
        }

        #endregion VoxelObjectManagement






        #region VoxelWorldManagement

        
        [SerializeField] private Vector3 worldScaleAxis = Vector3.one;
        [SerializeField] private SerializableDictionary<Transform, VoxTransform.VoxTransformSavable> transToColIds = new();
        private HashSet<Transform> newVoxelTrans = new();
        [System.NonSerialized] public VoxWorld voxWorld = new();

        private bool ValidateVoxelSystem()
        {
            voxIsValid = false;

            //Get saved data
            if (savedVoxData == null)
            {
                savedVoxData = Resources.Load<VoxSavedData>("VoxSavedData");
                if (savedVoxData == null)
                {
                    Debug.LogError("Expected VoxSavedData.asset to exist at path _voxelSystem/Resources/VoxSavedData.asset, have you deleted it?");
                    return false;
                }
            }

            //Setup voxWorld count
            if (transform.position.magnitude > 0.0001f || transform.eulerAngles.magnitude > 0.0001f)
            {
                //Not actually needed but just to make it obvious that you cant move the voxel grid
                Debug.LogError(transform.name + " world position and rotation magnitude must equal ~0.0f");
                return false;
            }

            if (worldScaleAxis.x <= 0.0f || worldScaleAxis.y <= 0.0f || worldScaleAxis.z <= 0.0f)
            {
                Debug.LogError("All axis in worldScaleAxis must be positive!");
                return false;
            }
            
            float totScale = worldScaleAxis.TotalValue() / 3.0f;

            int vZCount = Mathf.RoundToInt((worldScaleAxis.z / totScale) * VoxGlobalSettings.voxelAxisCount);
            int vXCount = Mathf.RoundToInt((worldScaleAxis.x / totScale) * VoxGlobalSettings.voxelAxisCount);
            int vYCount = Mathf.RoundToInt((worldScaleAxis.y / totScale) * VoxGlobalSettings.voxelAxisCount);

            voxWorld.vCountXYZ = vXCount * vYCount * vZCount;
#if UNITY_EDITOR
            voxWorld.vCountX = vXCount;
            voxWorld.vCountY = vYCount;
#endif
            voxWorld.vCountZ = vZCount;
            voxWorld.vCountZY = vZCount * vYCount;

            if (voxWorld.vCountXYZ < 100)
            {
                Debug.LogError("Unable to create a valid voxel grid with the given voxelCount and scaleAxis, either voxelWorldCount or worldScaleAxis is bad");
                return false;
            }

            //Try load vox objects from saved data
            int savedCount = savedVoxData.voxIds.Count;
            for (int i = 0; i < savedCount; i++)
            {
                colIdToVoxObject.TryAdd(savedVoxData.voxIds[i], savedVoxData.voxObjects[i].ToVoxObject());
            }

            voxIsValid = true;
            return true;
        }

        private void SetupVoxelSystem()
        {
            //Allocate world voxel arrays
            voxWorld.voxs = new NativeArray<byte>(voxWorld.vCountXYZ, Allocator.Persistent);
            voxWorld.voxsTypes = new NativeArray<byte>(voxWorld.vCountXYZ * VoxGlobalSettings.voxelTypeCount, Allocator.Persistent);
        }

        private void ClearVoxelSystem()
        {
            //Dispose world voxel arrays
            if (voxWorld.voxs.IsCreated == true) voxWorld.voxs.Dispose();
            if (voxWorld.voxsTypes.IsCreated == true) voxWorld.voxsTypes.Dispose();
        }

        /// <summary>
        /// The colIds the transform at X uses
        /// </summary>
        private List<NativeArray<int>> transColIds;

        [BurstCompile]
        private struct GetTransformData_work : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction] public NativeList<VoxTransform> voxTranss;
            [NativeDisableParallelForRestriction] public NativeList<float> transOldLocValue;

            public void Execute(int index, TransformAccess transform)
            {
                //Get transform location value
                float newLocValue = transform.worldToLocalMatrix.GetHashCode();

                if (newLocValue - transOldLocValue[index] != 0.0f)
                {
                    //Transform has moved, update its voxels
                    Matrix4x4 newLToW = transform.localToWorldMatrix;

                    transOldLocValue[index] = newLocValue;
                }
            }
        }

        #endregion VoxelWorldManagement






        #region Editor

#if UNITY_EDITOR
        private Vector3 oldWorldScaleAxis = Vector3.zero;

        private void OnDrawGizmos()
        {
            if (Application.isPlaying == false && (voxIsValid == false || oldWorldScaleAxis != worldScaleAxis))
            {
                ValidateVoxelSystem();
                oldWorldScaleAxis = worldScaleAxis;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (voxIsValid == false) return;

            //Draw world voxel bounds
            Vector3 size = new Vector3(voxWorld.vCountX, voxWorld.vCountY, voxWorld.vCountZ) * VoxGlobalSettings.voxelSizeWorld;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(size * 0.5f, size + (Vector3.one * 0.1f));
            Gizmos.color = Color.red;
            Gizmos.DrawCube(size * 0.5f, size);
        }

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
        #endregion Editor
    }
}


