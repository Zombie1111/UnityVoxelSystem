using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
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
        /// </summary>
        public void CreateVoxObjectFromCollider(Collider col, int colId, byte objVoxType = VoxGlobalSettings.solidStart + 1)
        {
            //Add colId to transform
            Transform trans = col.transform;
            bool isNewT;
            if (transToColIds.TryGetValue(trans, out var voxT) == false)
            {
                voxT = new()
                {
                    colIds = new(),
                    transIndex = -1//We set it later when we actually add the trans
                };

                transToColIds.Add(trans, voxT);
                isNewT = true;
            }
            else
            {
                isNewT = false;
            }

            if (voxT.colIds.Contains(colId) == false) voxT.colIds.Add(colId);
            else if (isNewT == false)
            {
                SetVoxTransActiveStatus(trans, true);
                return;
            }

#if UNITY_EDITOR
            if (Application.isPlaying == true)
#endif
                newVoxelTrans.Add(trans);

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

        /// <summary>
        /// Enable or disable a voxelTransform
        /// </summary>
        public void SetVoxTransActiveStatus(Transform trans, bool toActive)
        {
            transToSetVoxActiveStatus[trans] = toActive;
        }

        /// <summary>
        /// Cleares voxelTransforms and RuntimeVoxelSystem (Works in build)
        /// </summary>
        public void ClearEditorVoxelSystem()
        {
            ClearRuntimeVoxelSystem();
            transToColIds.Clear();
        }

        #endregion VoxelObjectManagement






        #region VoxelWorldManagement

        [Header("Configuration")]
        [SerializeField] private Vector3 worldScaleAxis = Vector3.one;
        [SerializeField][Range(1, 10)] private int framesBetweenVoxelUpdate = 2;
#if UNITY_EDITOR
        [SerializeField] private bool drawEditorGizmo = true;
#endif

        [Space()]
        [Header("Debug")]
        [SerializeField] private SerializableDictionary<Transform, VoxTransform.VoxTransformSavable> transToColIds = new();
        private HashSet<Transform> newVoxelTrans = new();
        private HashSet<int> newVoxelObj = new();
        [System.NonSerialized] public VoxWorld voxWorld = new();

        /// <summary>
        /// Returns true if the voxelSystem is valid
        /// </summary>
        /// <returns></returns>
        public bool ValidateVoxelSystem()
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

            int vZCount = (int)Math.Round((worldScaleAxis.z / totScale) * VoxGlobalSettings.voxelAxisCount);
            int vXCount = (int)Math.Round((worldScaleAxis.x / totScale) * VoxGlobalSettings.voxelAxisCount);
            int vYCount = (int)Math.Round((worldScaleAxis.y / totScale) * VoxGlobalSettings.voxelAxisCount);

            voxWorld.vCountXYZ = vXCount * vYCount * vZCount;
#if UNITY_EDITOR
            voxWorld.vCountX = vXCount;
            voxWorld.vCountY = vYCount;
#endif
            voxWorld.vCountZ = vZCount;
            voxWorld.vCountYZ = vYCount * vZCount;

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
                AddObjectToVoxelSystem(colObj.Key, colObj.Value);
            }

            voxRuntimeIsValid = true;
        }

        /// <summary>
        /// Disposes the jobs and the world voxel grid
        /// </summary>
        public void ClearRuntimeVoxelSystem()
        {
            if (voxRuntimeIsValid == false) return;
            ComputeVoxels_end(false);
            voxRuntimeIsValid = false;

            //Make sure no globalReadAccess
            if (globalHasReadAccess == true)
            {
                globalHasReadAccess = false;
                OnGlobalReadAccessStop?.Invoke();
            }

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
            if (voxTrans.transIndex >= 0)
            {
                //Update already added transform
                //Unapply transform from voxel system
                var voxT = cvo_job.voxTranss[voxTrans.transIndex];

                if (transColIds[voxTrans.transIndex].IsCreated == true)
                {
                    foreach (int colId in transColIds[voxTrans.transIndex])
                    {
                        var voxO = cvo_job.colIdToVoxObject[colId];
                        var lToWPrev = gtd_job.voxTranssLToWPrev[voxTrans.transIndex];

                        VoxHelpBurst.ApplyVoxObjectToWorldVox(ref voxWorld, ref cvo_job.voxsCount, ref cvo_job.voxsType, ref cvo_job.voxsTypeOld,
                            ref voxO, ref lToWPrev, ref lToWPrev, ref voxT, true);//We must unapply it before doing changing any data, should be fast enough to not cause a problem?
                    }

                    transColIds[voxTrans.transIndex].Dispose();
                }

                //Update transform data
                transColIds[voxTrans.transIndex] = colIds;
                gtd_job.transOldLocValue[voxTrans.transIndex] = 0.00002f;

                voxT.colIds_ptr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(colIds);
                voxT.colIds_lenght = colIds.Length;
                cvo_job.voxTranss[voxTrans.transIndex] = voxT;
            }
            else
            {
                //Add new tranform to voxel system
                transColIds.Add(colIds);
                voxelObjTranss.Add(trans);
                gtd_job.transOldLocValue.Add(0.0f);
                gtd_job.voxTranssLToWPrev.Add(Matrix4x4.zero);
                cvo_job.voxTranss.Add(new()
                {
                    colIds_ptr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(colIds),
                    colIds_lenght = colIds.Length,
                    isAppliedToWorld = false,
                    isActive = true
                });
            }

            return transColIds.Count - 1;
        }

        /// <summary>
        /// Adds the given voxel object to the voxelSystem returns the objIndex it was added to
        /// </summary>
        private unsafe int AddObjectToVoxelSystem(int colId, VoxObject.VoxObjectSaveable voxObj)
        {
            if (cvo_job.colIdToVoxObject.ContainsKey(colId) == true) return -1;

            var voxs = voxObj.voxs.ToNativeArray(Allocator.Persistent);
            voxObjectVoxs.Add(voxs);

            cvo_job.colIdToVoxObject.Add(colId, new()
            {
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

        /// <summary>
        /// Returns a valid VoxGlobalHandler, returns null if no valid VoxGlobalHandler is found
        /// </summary>
        /// <returns></returns>
        public static VoxGlobalHandler TryGetValidGlobalHandler()
        {
            var vHandler = GameObject.FindAnyObjectByType<VoxGlobalHandler>(FindObjectsInactive.Exclude);

            if (vHandler == null)
            {
                Debug.LogError("There is no active VoxGlobalHandler in the current scene");
                return null;
            }

            return vHandler;
        }

        #endregion VoxelWorldManagement





        #region ComputeVoxels

        public delegate void Event_OnGlobalReadAccessStart();
        public event Event_OnGlobalReadAccessStart OnGlobalReadAccessStart;
        public delegate void Event_OnGlobalReadAccessStop();
        public event Event_OnGlobalReadAccessStop OnGlobalReadAccessStop;
        private bool wannaUpdateVoxels = true;
        private int framesSinceUpdatedVoxels = 69420;

        private void LateUpdate()
        {
            if (wannaUpdateVoxels == true)
            {
                framesSinceUpdatedVoxels = 0;
                wannaUpdateVoxels = false;
                ComputeVoxels_start();
                return;
            }

            framesSinceUpdatedVoxels++;
            if (framesSinceUpdatedVoxels >= framesBetweenVoxelUpdate) wannaUpdateVoxels = true;
            ComputeVoxels_end(true);
        }

        private Dictionary<Transform, bool> transToSetVoxActiveStatus = new();

        private void ComputeVoxels_start()
        {
            if (gtdCvo_jobIsActive == true || voxRuntimeIsValid == false) return;

            //Global read access stops here
            //Internal read+write access starts here
            if (globalHasReadAccess == true)
            {
                globalHasReadAccess = false;
                OnGlobalReadAccessStop?.Invoke();
            }

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
                //voxO.objIndex = AddObjectToVoxelSystem(newObj, voxO);
                AddObjectToVoxelSystem(newObj, voxO);
            }

            newVoxelObj.Clear();

            //Set transforms active status
            foreach (var transToSet in transToSetVoxActiveStatus)
            {
                if (transToColIds.TryGetValue(transToSet.Key, out var voxTS) == false || voxTS.transIndex < 0) continue;
                var voxT = cvo_job.voxTranss[voxTS.transIndex];

                if (voxT.isActive == transToSet.Value) continue;

                voxT.isActive = transToSet.Value;
                if (voxT.isActive == true)
                {
                    gtd_job.transOldLocValue[voxTS.transIndex] = 0.0001f;
                    voxelObjTranss[voxTS.transIndex] = transToSet.Key;
                }
                else
                {
                    gtd_job.transOldLocValue[voxTS.transIndex] = 0.0003f;
                }

                if (transToSet.Key == null)
                {
                    foreach (int colId in transColIds[voxTS.transIndex])
                    {
                        var voxO = cvo_job.colIdToVoxObject[colId];
                        var lToWPrev = gtd_job.voxTranssLToWPrev[voxTS.transIndex];

                        VoxHelpBurst.ApplyVoxObjectToWorldVox(ref voxWorld, ref cvo_job.voxsCount, ref cvo_job.voxsType, ref cvo_job.voxsTypeOld,
                            ref voxO, ref lToWPrev, ref lToWPrev, ref voxT, true);//We must unapply manually since transJob dont get called on deleted trans
                    }
                }

                cvo_job.voxTranss[voxTS.transIndex] = voxT;
            }

            transToSetVoxActiveStatus.Clear();

            //Run the job
            gtd_handle = gtd_job.Schedule(voxelObjTranss);
            cvo_handle = cvo_job.Schedule(gtd_handle);
            gtdCvo_jobIsActive = true;
        }

        private bool globalHasReadAccess = true;

        private void ComputeVoxels_end(bool giveGlobalReadAccess = true)
        {
            if (gtdCvo_jobIsActive == false) return;
            
            //Complete the job
            gtdCvo_jobIsActive = false;
            gtd_handle.Complete();
            cvo_handle.Complete();

            //Internal read+write access stops here
            //Global read access starts here
            if (giveGlobalReadAccess == false || globalHasReadAccess == true) return;

            globalHasReadAccess = true;
            OnGlobalReadAccessStart?.Invoke();
        }

        [System.NonSerialized] public ComputeVoxelWorld_work cvo_job;
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
        public struct ComputeVoxelWorld_work : IJob
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
                            ref voxO, ref toCompute.prevLToW, ref toCompute.nowLToW, ref voxT, false);
                    }

                    voxTranss[toCompute.transIndex] = voxT;
                }
            }
        }

        #endregion ComputeVoxels






        #region Editor

#if UNITY_EDITOR
        private Vector3 oldWorldScaleAxis = Vector3.zero;
        [System.NonSerialized] public bool debugDoUpdateVisualVoxels = false;
        public Transform debugTrams;
        public Transform debugTramsB;

        private void OnDrawGizmos()
        {
            if (Application.isPlaying == false && (voxEditorIsValid == false || oldWorldScaleAxis != worldScaleAxis))
            {
                ValidateVoxelSystem();
                oldWorldScaleAxis = worldScaleAxis;
            }

            if (voxEditorIsValid == false || drawEditorGizmo == false) return;

            //Draw world voxel bounds
            Vector3 size = new Vector3(voxWorld.vCountX, voxWorld.vCountY, voxWorld.vCountZ) * VoxGlobalSettings.voxelSizeWorld;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(size * 0.5f, size + (Vector3.one * 0.1f));
            Gizmos.color = Color.red;
            Gizmos.DrawCube(size * 0.5f, size);

            //Draw debug voxels
            if (debugDoUpdateVisualVoxels == true && globalHasReadAccess == true)
            {
                //debugDoUpdateVisualVoxels = false;
                Debug_updateVisualVoxels();
            }

            if (debugDoUpdateVisualVoxels == true && Application.isPlaying == true)
            {
                Vector3 voxSize = Vector3.one * VoxGlobalSettings.voxelSizeWorld;

                foreach (var draw in voxsToDraw)
                {
                    Gizmos.color = VoxGlobalSettings.diffColors64[draw.colorI];
                    Gizmos.DrawCube(draw.pos, voxSize);
                }
            }

            //Debug stuff
            //if (Application.isPlaying == true)
            //{
            //    Vector3 pos = debugTrams.position;
            //    int vI = 0;
            //    VoxHelpBurst.PosToWVoxIndex(ref pos, ref vI, ref voxWorld);
            //    VoxHelpBurst.WVoxIndexToPos(ref vI, ref pos, ref voxWorld);
            //    Gizmos.DrawCube(pos, Vector3.one * VoxGlobalSettings.voxelSizeWorld);
            //
            //    byte result = 0;
            //    Debug_toggleTimer();
            //    for (int i = 0; i < 10000; i++)
            //    {
            //        result = IsVoxelValidClimb(vI, 6);
            //    }
            //    Debug_toggleTimer();
            //    Debug.Log(result);
            //
            //    Vector3 posB = debugTramsB.position;
            //    int vIB = 0;
            //    VoxHelpBurst.PosToWVoxIndex(ref posB, ref vIB, ref voxWorld);
            //    VoxHelpBurst.WVoxIndexToPos(ref vIB, ref posB, ref voxWorld);
            //    Gizmos.DrawCube(posB, Vector3.one * VoxGlobalSettings.voxelSizeWorld);
            //
            //    Gizmos.DrawLine(pos, posB);
            //    ushort voxDis = 0;
            //    VoxHelpBurst.GetVoxelCountBetweenWVoxIndexs(vI, vIB, ref voxDis, ref voxWorld);
            //    Debug.Log("Actual dis: " + Vector3.Distance(pos, posB) + " vox dis: " + (voxDis * VoxGlobalSettings.voxelSizeWorld));
            //}
        }

        private List<VoxHelpBurst.CustomVoxelData> voxsToDraw = new();

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

        private void Debug_updateVisualVoxels()
        {
            voxsToDraw.Clear();
            if (Application.isPlaying == false)
            {
                Debug.LogError("Cannot update visual voxels in editmode");
                return;
            }

            float maxDis = VoxGlobalSettings.voxelSizeWorld * 50.0f;
            var view = SceneView.currentDrawingSceneView;
            if (view == null) return;
            Vector3 sceneCam = view.camera.ViewportToWorldPoint(view.cameraViewport.center);

            NativeList<VoxHelpBurst.CustomVoxelData> solidVoxsI = new(1080, Allocator.Temp);
            VoxHelpBurst.GetAllVoxelDataWithinRadius(ref sceneCam, maxDis, ref cvo_job.voxsType, ref voxWorld, ref solidVoxsI);

            foreach (var vData in solidVoxsI)
            {
                voxsToDraw.Add(vData);
            }

            solidVoxsI.Dispose();
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
            else
            {
                if (GUILayout.Button("Toggle Visual Voxels") == true)
                {
                    yourScript.debugDoUpdateVisualVoxels = !yourScript.debugDoUpdateVisualVoxels;
                }

                EditorGUILayout.HelpBox("Properties cannot be edited at runtime", MessageType.Info);
            }

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


