using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zombVoxels;

public class VoxParent : MonoBehaviour
{
    [Header("Voxel Settings")]
    public bool buildOnStart = false;
    public byte voxelType = 0;
    public Collider voxelColliderOverwrite = null;

    [Space]
    [Header("Collider Settings")]
    public bool includeInative = false;
    public bool includeTriggers = false;
    public bool ignoreSelf = false;
    public bool ignoreChildren = false;
    public bool affectSelf = true;
    public bool affectChildren = true;

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

    private void Start()
    {
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
            voxGlobal = GameObject.FindAnyObjectByType<VoxGlobalHandler>(FindObjectsInactive.Exclude);

            if (voxGlobal == null)
            {
                Debug.LogError("There is no active VoxGlobalHandler in the current scene");
                return false;
            }
        }

        return true;
    }
}
