using System.Collections.Generic;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.InputSystem.iOS;
using UnityEngine.Jobs;

namespace zombVoxels
{
    public class VoxGlobalHandler : MonoBehaviour
    {
        #region VoxelObjectManagement

        private VoxSavedData savedVoxData;
        private bool voxEditorIsValid = false;
        private bool voxRuntimeIsValid = false;

        private void Awake()
        {
            if (ValidateVoxelSystem() == false) return;
            SetupVoxelSystem();
        }

        private void OnDestroy()
        {
            ClearRuntimeVoxelSystem();
        }

        /// <summary>
        /// Creates a voxObject from the given collider and adds it to the collider transform
        /// (Unexpected behaviour may occure if collider transform already has colId)
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
                    transIndex = -1//We set it later when we actually add the trans
                };

                transToColIds.Add(trans, voxT);
            }

            voxT.colIds.Add(colId);

            //Voxelize the collider if needed
            if (savedVoxData.colIdToVoxObjectSave.ContainsKey(colId) == true) return;

            var voxObjSave = VoxHelpFunc.VoxelizeCollider(col, objVoxType);
            savedVoxData.AddVoxObject(colId, voxObjSave);
#if UNITY_EDITOR
            if (Application.isPlaying == true)
#endif
                newVoxelObj.Add(colId);
        }

        /// <summary>
        /// Removes a voxObject with the given colId, at runtime call ClearRuntimeVoxelSystem() first
        /// </summary>
        public void DestroyVoxObject(int colId)
        {
            //Removing while runtime voxels are built aint supported since the overhead of removing it from everything is just too much
            if (voxRuntimeIsValid == true)
            {
                Debug.LogError("Unable to destroy voxelObject while runtime voxels are built, call ClearRuntimeVoxelSystem() first");
                return;
            }

            savedVoxData.RemoveVoxObject(colId);

        }

        public void ClearEditorVoxelSystem()
        {
            ClearRuntimeVoxelSystem();
            transToColIds.Clear();
        }

        #endregion VoxelObjectManagement






        #region VoxelWorldManagement

        [Header("Voxel Grid Scale")]
        [SerializeField] private Vector3 worldScaleAxis = Vector3.one;

        [Space()]
        [Header("Debug")]
        [SerializeField] private SerializableDictionary<Transform, VoxTransform.VoxTransformSavable> transToColIds = new();
        private HashSet<Transform> newVoxelTrans = new();
        private HashSet<int> newVoxelObj = new();
        [System.NonSerialized] public VoxWorld voxWorld = new();

        private bool ValidateVoxelSystem()
        {
            voxEditorIsValid = false;

            //Get saved data
            if (savedVoxData == null)
            {
                savedVoxData = VoxHelpFunc.TryGetVoxelSaveAsset();
                if (savedVoxData == null) return false;
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

            voxEditorIsValid = true;
            return true;
        }

        public void SetupVoxelSystem()
        {
            if (voxRuntimeIsValid == true) ClearRuntimeVoxelSystem();

            //Allocate ComputeVoxelObjects_work
            transColIds.Clear();
            voxObjectVoxs.Clear();

            cvo_job = new()
            {
                voxTranssToCompute = new(Allocator.Persistent),
                voxTranss = new(16, Allocator.Persistent),
                colIdToVoxObject = new(16, Allocator.Persistent),
                voxsCount = new NativeArray<byte>(voxWorld.vCountXYZ, Allocator.Persistent),
                voxsType = new NativeArray<byte>(voxWorld.vCountXYZ, Allocator.Persistent),
                voxsTypeOld = new NativeArray<byte>(voxWorld.vCountXYZ, Allocator.Persistent),
                voxWorld = new(voxWorld, Allocator.Persistent)
            };

            //Allocate GetTransformData_work
            voxelObjTranss = new(16);

            gtd_job = new()
            {
                transOldLocValue = new(16, Allocator.Persistent),
                voxTranssLToWPrev = new(16, Allocator.Persistent),
                voxTranssToCompute = cvo_job.voxTranssToCompute.AsParallelWriter()
            };

            //Add existing voxel transforms to the voxelSystem
            foreach (var transCol in transToColIds)
            {
                transToColIds[transCol.Key].transIndex = AddTransformToVoxelSystem(transCol.Key, transCol.Value);
            }

            //Add existing voxel objects to the voxelSystem
            foreach (var colObj in savedVoxData.colIdToVoxObjectSave)
            {
                savedVoxData.colIdToVoxObjectSave[colObj.Key].objIndex = AddObjectToVoxelSystem(colObj.Key, colObj.Value);
            }

            voxRuntimeIsValid = true;
        }

        public void ClearRuntimeVoxelSystem()
        {
            if (voxRuntimeIsValid == false) return;
            ComputeVoxels_end();
            voxRuntimeIsValid = false;

            //Dispose voxel transforms
            int transCount = transColIds.Count;
            for (int i = 0; i < transCount; i++)
            {
                if (transColIds[i].IsCreated == true) transColIds[i].Dispose();
            }

            newVoxelTrans.Clear();
            transColIds.Clear();

            //Dispose voxel objects
            int vObjCount = voxObjectVoxs.Count;
            for (int i = 0; i < vObjCount; i++)
            {
                if (voxObjectVoxs[i].IsCreated == true) voxObjectVoxs[i].Dispose();
            }

            newVoxelObj.Clear();
            voxObjectVoxs.Clear();

            //Dispose GetTransformData_work
            if (voxelObjTranss.isCreated == true) voxelObjTranss.Dispose();
            if (gtd_job.transOldLocValue.IsCreated == true) gtd_job.transOldLocValue.Dispose();
            if (gtd_job.voxTranssLToWPrev.IsCreated == true) gtd_job.voxTranssLToWPrev.Dispose();

            //Dispose ComputeVoxelObjects_work
            if (cvo_job.voxTranss.IsCreated == true) cvo_job.voxTranss.Dispose();
            if (cvo_job.voxTranssToCompute.IsCreated == true) cvo_job.voxTranssToCompute.Dispose();
            if (cvo_job.colIdToVoxObject.IsCreated == true) cvo_job.colIdToVoxObject.Dispose();
            if (cvo_job.voxsCount.IsCreated == true) cvo_job.voxsCount.Dispose();
            if (cvo_job.voxsType.IsCreated == true) cvo_job.voxsType.Dispose();
            if (cvo_job.voxsTypeOld.IsCreated == true) cvo_job.voxsTypeOld.Dispose();
            if (cvo_job.voxWorld.IsCreated == true) cvo_job.voxWorld.Dispose();
        }

        /// <summary>
        /// Adds the given voxel transform to the voxelSystem and returns the transIndex it was added to
        /// (Unexpected behaviour may occure if trans is already added to the voxelSystem)
        /// </summary>
        private unsafe int AddTransformToVoxelSystem(Transform trans, VoxTransform.VoxTransformSavable voxTrans)
        {
            if (trans == null) return -1;

            NativeArray<int> colIds = voxTrans.colIds.ToNativeArray(Allocator.Persistent);
            transColIds.Add(colIds);
            voxelObjTranss.Add(trans);
            gtd_job.transOldLocValue.Add(0.0f);
            gtd_job.voxTranssLToWPrev.Add(Matrix4x4.zero);

            cvo_job.voxTranss.Add(new()
            {
                colIds_ptr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(colIds),
                colIds_lenght = colIds.Length,
            });

            return transColIds.Count - 1;
        }

        /// <summary>
        /// Adds the given voxel object to the voxelSystem returns the objIndex it was added to
        /// (Unexpected behaviour may occure if colId is already added to the voxelSystem)
        /// </summary>
        private unsafe int AddObjectToVoxelSystem(int colId, VoxObject.VoxObjectSaveable voxObj)
        {
            var voxs = voxObj.voxs.ToNativeArray(Allocator.Persistent);
            voxObjectVoxs.Add(voxs);

            cvo_job.colIdToVoxObject.Add(colId, new()
            {
                isAppliedToWorld = false,
                minL = voxObj.minL,
                vCountYZ = voxObj.vCountYZ,
                vCountZ = voxObj.vCountZ,
                voxType = voxObj.voxType,
                xDirL = voxObj.xDirL,
                yDirL = voxObj.yDirL,
                zDirL = voxObj.zDirL,
                voxs_lenght = voxs.Length,
                voxs_ptr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(voxs)
            });

            return voxObjectVoxs.Count - 1;
        }

        /// <summary>
        /// The colIds the transform at X uses
        /// </summary>
        private List<NativeArray<int>> transColIds = new();
        private List<NativeArray<int>> voxObjectVoxs = new();
        private TransformAccessArray voxelObjTranss;

        #endregion VoxelWorldManagement





        #region ComputeVoxels

        private void Update()
        {
            ComputeVoxels_start();
        }

        private void LateUpdate()
        {
            ComputeVoxels_end();
        }

        private void ComputeVoxels_start()
        {
            if (gtdCvo_jobIsActive == true || voxRuntimeIsValid == false) return;

            //Add new transforms and objects to voxel system
            foreach (var newTrans in newVoxelTrans)
            {
                var voxT = transToColIds[newTrans];
                voxT.transIndex = AddTransformToVoxelSystem(newTrans, voxT);
            }

            newVoxelTrans.Clear();

            foreach (var newObj in newVoxelObj)
            {
                var voxO = savedVoxData.colIdToVoxObjectSave[newObj];
                voxO.objIndex = AddObjectToVoxelSystem(newObj, voxO);
            }

            newVoxelObj.Clear();

            //Run the job
            gtd_handle = gtd_job.Schedule(voxelObjTranss);
            cvo_handle = cvo_job.Schedule(gtd_handle);
            gtdCvo_jobIsActive = true;
        }

        private void ComputeVoxels_end()
        {
            if (gtdCvo_jobIsActive == false) return;

            //Complete the job
            gtdCvo_jobIsActive = false;
            gtd_handle.Complete();
            cvo_handle.Complete();
        }

        private ComputeVoxelWorld_work cvo_job;
        private GetTransformData_work gtd_job;
        private JobHandle cvo_handle;
        private JobHandle gtd_handle;
        private bool gtdCvo_jobIsActive = false;

        [BurstCompile]
        private struct GetTransformData_work : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction] public NativeList<Matrix4x4> voxTranssLToWPrev;
            [NativeDisableParallelForRestriction] public NativeList<float> transOldLocValue;
            public NativeQueue<VoxTransform.ToCompute>.ParallelWriter voxTranssToCompute;

            public unsafe void Execute(int index, TransformAccess transform)
            {
                //Get transform location value
                float newLocValue = transform.worldToLocalMatrix.GetHashCode();

                if (newLocValue - transOldLocValue[index] != 0.0f)
                {
                    //Transform has moved
                    transOldLocValue[index] = newLocValue;

                    //Get voxel transform and colIds
                    Matrix4x4 newLToW = transform.localToWorldMatrix;
                    voxTranssToCompute.Enqueue(new()
                    {
                        nowLToW = newLToW,
                        prevLToW = voxTranssLToWPrev[index],
                        transIndex = index,
                    });

                    voxTranssLToWPrev[index] = newLToW;
                }
            }
        }

        [BurstCompile]
        private struct ComputeVoxelWorld_work : IJob
        {
            public NativeList<VoxTransform> voxTranss;
            public NativeHashMap<int, VoxObject> colIdToVoxObject;
            public NativeQueue<VoxTransform.ToCompute> voxTranssToCompute;
            public NativeReference<VoxWorld> voxWorld;

            /// <summary>
            /// The number of voxels at this global voxel
            /// </summary>
            public NativeArray<byte> voxsCount;

            /// <summary>
            /// The type this global voxel is, always air if voxsCount[X] is 0
            /// </summary>
            public NativeArray<byte> voxsType;

            /// <summary>
            /// The type this global voxel had, used internally
            /// </summary>
            public NativeArray<byte> voxsTypeOld;

            public unsafe void Execute()
            {
                var voxW = voxWorld.Value;

                while (voxTranssToCompute.TryDequeue(out var toCompute) == true)
                {
                    var voxT = voxTranss[toCompute.transIndex];

                    NativeArray<int> colIds = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(
                                       voxT.colIds_ptr, voxT.colIds_lenght, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref colIds, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

                    foreach (int colId in colIds)
                    {
                        var voxO = colIdToVoxObject[colId];
                        VoxHelpBurst.ApplyVoxObjectToWorldVox(ref voxW, ref voxsCount, ref voxsType, ref voxsTypeOld,
                            ref voxO, ref toCompute.prevLToW, ref toCompute.nowLToW);
                    }
                }
            }
        }

        #endregion ComputeVoxels






        #region Editor

#if UNITY_EDITOR
        private Vector3 oldWorldScaleAxis = Vector3.zero;

        private void OnDrawGizmos()
        {
            if (Application.isPlaying == false && (voxEditorIsValid == false || oldWorldScaleAxis != worldScaleAxis))
            {
                ValidateVoxelSystem();
                oldWorldScaleAxis = worldScaleAxis;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (voxEditorIsValid == false) return;

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

#if UNITY_EDITOR
    //########################Custom Editor######################################
    [CustomEditor(typeof(VoxGlobalHandler))]
    public class YourScriptEditor : Editor
    {
        private static readonly string[] hiddenFields = new string[]
        {
                "m_Script", "transToColIds", "worldScaleAxis"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            VoxGlobalHandler yourScript = (VoxGlobalHandler)target;

            EditorGUILayout.Space();

            DrawPropertiesExcluding(serializedObject, hiddenFields);
            if (Application.isPlaying == false) EditorGUILayout.PropertyField(serializedObject.FindProperty("worldScaleAxis"), true);
            else EditorGUILayout.HelpBox("Properties cannot be edited at runtime", MessageType.Info);

            GUI.enabled = false;
            if (Application.isPlaying == true)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("worldScaleAxis"), true);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("transToColIds"), true);
            GUI.enabled = true;

            //Apply changes
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}


