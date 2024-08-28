//https://github.com/Zombie1111/UnityVoxelSystem
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

#if UNITY_EDITOR
    [CustomEditor(typeof(VoxSavedData))]
    public class FractureSaveAssetEditor : Editor
    {
        private bool showFloatVariable = false;

        public override void OnInspectorGUI()
        {
            // Show the button to toggle the float variable
            if (GUILayout.Button("Show Voxel Cache Data (MAY FREEZE UNITY!)"))
            {
                showFloatVariable = !showFloatVariable;
            }

            if (showFloatVariable)
            {
                // Show the variables
                serializedObject.Update(); // Ensure serialized object is up to date

                DrawPropertiesExcluding(serializedObject, "m_Script");
            }

            // Apply modifications to the asset
            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
