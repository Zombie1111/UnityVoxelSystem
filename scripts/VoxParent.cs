//https://github.com/Zombie1111/UnityVoxelSystem
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zombVoxels;

[DefaultExecutionOrder(200)]
public class VoxParent : MonoBehaviour
{
    [Header("Voxel Settings")]
    [Tooltip("If true the voxelObject will not be baked in editor")] public bool buildOnStart = true;
    [Tooltip("The type all voxels that are from this parent has, can be used to categorize voxels and more," +
        " see solidTypeStart in VoxGlobalSettings.cs")] public byte voxelType = VoxGlobalSettings.defualtType;
    [Tooltip("If assigned, all affected colliders will use this collider as voxelObject")] public Collider voxelColliderOverwrite = null;

    [Space]
    [Header("Collider Settings")]
    [Tooltip("Should the voxParent settings affect this transform?")] public bool affectSelf = true;
    [Tooltip("Should the voxParent settings affect children transforms?")] public bool affectChildren = true;
    [Tooltip("If false, inactive colliders wont be voxelized")] public bool includeInative = false;
    [Tooltip("If false, colliders marked as trigger wont be voxelized")] public bool includeTriggers = false;
    [Tooltip("If true, colliders on this transform wont be voxelized")] public bool ignoreSelf = false;
    [Tooltip("If true, colliders on children wont be voxelized")] public bool ignoreChildren = false;

    [Space]
    [Header("Debug")]
    public List<VoxCollider> voxCols = new();

#if UNITY_EDITOR
    //########################Custom Editor######################################
    [CustomEditor(typeof(VoxParent))]
    public class YourScriptEditor : Editor
    {
        private static readonly string[] hiddenFields = new string[]
        {
                "m_Script", "voxCols"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            VoxParent yourScript = (VoxParent)target;

            EditorGUILayout.Space();

            DrawPropertiesExcluding(serializedObject, hiddenFields);
            GUI.enabled = false;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("voxCols"), true);
            GUI.enabled = true;

            //Apply changes
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif

    private VoxGlobalHandler voxGlobal;
    private bool hasBeenSetup = false;

    private void OnEnable()
    {
        //Setup voxeltrans
        if (hasBeenSetup == true) goto SkipTransSetup;

        //If build on start, get colliders and build voxels
        if (buildOnStart == true)
        {
            UpdateVoxParent();
            return;
        }

        //If build on scene, make sure voxels are built
        if (VerifyGlobalHandler() == false) return;

        foreach (var vCol in voxCols)
        {
            voxGlobal.CreateVoxObjectFromCollider(vCol.col, vCol.colId, vCol.colType);
        }

        hasBeenSetup = true;
        return;//New voxTrans are always enabled by defualt

        //Make sure voxTrans is enabled
        SkipTransSetup:;
        //voxGlobal.SetVoxTransActiveStatus(transform, true);
        SetVoxParentActiveStatus(true);
    }

    private void OnDisable()
    {
        //Make sure voxTrans is disabled
        //voxGlobal.SetVoxTransActiveStatus(transform, false);
        SetVoxParentActiveStatus(false);
    }

    private void OnDestroy()
    {
        SetVoxParentActiveStatus(false);
        //voxGlobal.SetVoxTransActiveStatus(transform, false);
    }

    /// <summary>
    /// Gets all colliders to build voxels for and builds the voxels
    /// </summary>
    public void UpdateVoxParent()
    {
        if (VerifyGlobalHandler() == false) return; 

        Dictionary<int, VoxBuilder.ColsType> unusedDic = null;
        VoxBuilder.GetAllColliderIdsInChildren(gameObject, ref unusedDic);

        foreach (var vCol in voxCols)
        {
            voxGlobal.CreateVoxObjectFromCollider(vCol.col, vCol.colId, vCol.colType);
        }
    }

    /// <summary>
    /// Returns true if a valid global handler exist and is assigned to voxGlobal
    /// </summary>
    private bool VerifyGlobalHandler()
    {
        if (voxGlobal == null)
        {
            voxGlobal = VoxGlobalHandler.TryGetValidGlobalHandler();

            if (voxGlobal == null) return false;
        }

        return true;
    }

    /// <summary>
    /// Enable or Disable all voxel transforms that has this parent
    /// </summary>
    public void SetVoxParentActiveStatus(bool toActive)
    {
        foreach (var vCol in voxCols)
        {
            voxGlobal.SetVoxTransActiveStatus(vCol.col.transform, toActive);
        }
    }
}
