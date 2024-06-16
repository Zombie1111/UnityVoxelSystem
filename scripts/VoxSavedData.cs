using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using zombVoxels;

public class VoxSavedData : ScriptableObject
{
    public List<int> voxIds = new();
    public List<VoxObject.VoxObjectSaveable> voxObjects = new();

#if UNITY_EDITOR
    /// <summary>
    /// Add vox object that should be saved on disk (Editor only)
    /// </summary>
    public void AddVoxObject(int voxId, VoxObject voxObj)
    {
        

        voxIds.Add(voxId);
        voxObjects.Add(voxObj.ToVoxObjectSaveable());
        EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// Remove vox object that is saved on disk (Editor only)
    /// </summary>
    public void RemoveVoxObject(int voxId)
    {
        int voxIndex = voxIds.FindIndex(id => id == voxId);
        if (voxIndex < 0) return;

        voxIds.RemoveAtSwapBack(voxIndex);
        voxObjects.RemoveAtSwapBack(voxIndex);
        EditorUtility.SetDirty(this);
    }
#endif
}
