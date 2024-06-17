using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
namespace zombVoxels
{
    public class VoxSavedData : ScriptableObject
    {
        public SerializableDictionary<int, VoxObject.VoxObjectSaveable> colIdToVoxObjectSave = new();

        /// <summary>
        /// Adds the given vox object to the global voxelObject dictorary
        /// </summary>
        internal void AddVoxObject(int colId, VoxObject.VoxObjectSaveable voxObj)
        {
            if (colIdToVoxObjectSave.TryAdd(colId, voxObj) == false) return;

#if UNITY_EDITOR
            if (Application.isPlaying == false) EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Removes the given vox object from the global voxelObject dictorary
        /// </summary>
        internal void RemoveVoxObject(int colId)
        {
            if (colIdToVoxObjectSave.Remove(colId) == false) return;

#if UNITY_EDITOR
            if (Application.isPlaying == false) EditorUtility.SetDirty(this);
#endif
        }

        internal void ClearVoxelObjects()
        {
            colIdToVoxObjectSave.Clear();

#if UNITY_EDITOR
            if (Application.isPlaying == false) EditorUtility.SetDirty(this);
#endif
        }
    }
}
