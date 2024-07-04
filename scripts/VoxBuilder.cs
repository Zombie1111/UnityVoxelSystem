using UnityEngine.SceneManagement;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using UnityEditor.SceneManagement;

namespace zombVoxels
{
    public static class VoxBuilder
    {
        Test later, use collider.raycast and send raycast towards collider center, if no collision we are inside collider

        /// <summary>
        /// Builds all voxelObjects and voxelSystem for all colliders in the given scene, returns true if succesfull
        /// </summary>
        public static bool BuildScene(Scene scene)
        {
            try
            {
                return Execute();
            }
            catch (Exception ex)
            {
                //Log the error
                Debug.LogError("Exception: " + ex.Message);
                Debug.LogError("StackTrace: " + ex.StackTrace);

                //Display an error message to the user
#if UNITY_EDITOR
                if (Application.isPlaying == false)
                    EditorUtility.DisplayDialog("Error", "An unexpected error occured while building voxels for scene " + scene.name, "OK");
#endif
                return false;
            }
            finally
            {
#if UNITY_EDITOR
                //Always clear the progressbar
                EditorUtility.ClearProgressBar();
#endif
            }

            bool Execute()
            {
                string sceneName = scene.name;
                if (UpdateProgressBar("Building voxelObjects: " + sceneName, "Getting scene objects", 0.0f) == false) return false;
                var colIdToCol = GetAllColliderIdsInScene(scene, out VoxGlobalHandler voxGlobal);
                if (colIdToCol == null) return false;

                voxGlobal.ClearEditorVoxelSystem();
                int colCount = colIdToCol.Count;
                int colProgress = 0;

                foreach (var colIdCol in colIdToCol)
                {
                    colProgress++;
                    if (UpdateProgressBar("Building voxelObjects: " + sceneName, "Building objects", colProgress / colCount) == false) return false;

                    int colId = colIdCol.Key;
                    byte voxType = colIdCol.Value.type;

                    foreach (var col in colIdCol.Value.cols)
                    {
                        voxGlobal.CreateVoxObjectFromCollider(col, colId, voxType);
                    }
                }

#if UNITY_EDITOR
                if (Application.isPlaying == false) EditorSceneManager.MarkSceneDirty(scene);
#endif

                return true;
            }
        }

        /// <summary>
        /// If editor, displays a progressbar, returns false if user wanna cancel (Dont forget to clear it)
        /// </summary>
        private static bool UpdateProgressBar(string title, string message, float progress)
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false)
            {
                if (EditorUtility.DisplayCancelableProgressBar(title, message, progress) == true)
                {
                    Debug.Log("Canceled " + title);
                    return false;
                }
            }

            return true;
#endif

#pragma warning disable CS0162 // Unreachable code detected
            return true;
#pragma warning restore CS0162 // Unreachable code detected
        }

        private class ColsType
        {
            public HashSet<Collider> cols;
            public byte type;
        }

        /// <summary>
        /// Returns a dictorary, the keys are all colliderIds, the values are all colliders that uses that colId (Returns null if invalid scene)
        /// </summary>
        private static Dictionary<int, ColsType> GetAllColliderIdsInScene(Scene scene, out VoxGlobalHandler voxGlobal)
        {
            voxGlobal = VerifyScene(scene, "build voxel objects", false);
            if (voxGlobal == null) return null;

            Dictionary<int, ColsType> colIdToCols = new();

            foreach (GameObject rootObj in scene.GetRootGameObjects())
            {
                //Clear colliders from voxParents
                foreach (VoxParent voxParent in rootObj.GetComponentsInChildren<VoxParent>(true))
                {
                    voxParent.voxCols.Clear();
                }

                //Get all colliders
                foreach (Collider col in rootObj.GetComponentsInChildren<Collider>(true))
                {
                    //Get if colldier should be included
                    bool usedParent = false;
                    var voxParent = col.GetComponentInParent<VoxParent>();

                    if (voxParent != null)
                    {
                        //We have voxParent, use its settings to know if colldier is included or not
                        bool isSelf = col.transform == voxParent.transform;

                        if ((isSelf == true && voxParent.affectSelf == false)
                            || (isSelf == false && voxParent.affectChildren == false)) goto SkipVoxParent;

                        if ((isSelf == true && voxParent.ignoreSelf == true)
                            || (isSelf == false && voxParent.ignoreChildren == true)) continue;

                        if ((col.isTrigger == true && voxParent.includeTriggers == false)
                            || ((col.enabled == false || col.gameObject.activeInHierarchy == false) && voxParent.includeInative == false)) continue;

                        usedParent = true;
                    }

                    SkipVoxParent:;

                    if (usedParent == false)
                    {
                        //No voxParent, use standard include settings
                        if (col.isTrigger == true || col.enabled == false || col.gameObject.activeInHierarchy == false) continue;
                    }

                    //Get collider id
                    byte voxType = usedParent == true ? voxParent.voxelType : (byte)0;
                    int colId = col.GetColId(voxType);

                    //Notify voxParent that it has this collider
                    if (usedParent == true)
                    {
                        voxParent.voxCols.Add(new() { col = col, colId = colId, colType = voxType });
                        if (voxParent.buildOnStart == true) continue;//Since its computed on start it should count as not included in scene
                    }

                    //Add collider and colId to the returned dictorary
                    if (colIdToCols.TryGetValue(colId, out ColsType colsType) == false)
                    {
                        colsType = new()
                        {
                            cols = new(),
                            type = voxType
                        };
                    }

                    colsType.cols.Add(col);
                    colIdToCols[colId] = colsType;
                }
            }

            return colIdToCols;
        }

        /// <summary>
        /// Returns null if the scene is invalid, returns the scene VoxGlobalHandler if valid scene
        /// </summary>
        private static VoxGlobalHandler VerifyScene(Scene scene, string errorMessageDo, bool ignoreIfNotInBuild = false)
        {
            //Verify scene
            if (scene.isSubScene == true)
            {
                Debug.LogError("Cannot " + errorMessageDo + " for " + scene.name + " because its a subScene");
                return null;
            }

#if UNITY_EDITOR
            if (ignoreIfNotInBuild == false
                && (scene.buildIndex == SceneManager.sceneCountInBuildSettings || scene.buildIndex < 0))
            {
                Debug.LogError("Cannot " + errorMessageDo + " for " + scene.name + " because the scene is not included in the build");
                return null;
            }
#endif

            //Verify VoxGlobalHandler
            List<VoxGlobalHandler> voxGlobals = new();
            foreach (GameObject rootObj in scene.GetRootGameObjects())
            {
                voxGlobals.AddRange(rootObj.GetComponentsInChildren<VoxGlobalHandler>(true));
            }

            if (voxGlobals.Count > 1)
            {
                Debug.LogError("Cannot " + errorMessageDo + " for " + scene.name + " because there is more than one VoxGlobalHandlers in the scene");
                return null;
            }

            if (voxGlobals.Count == 0 || voxGlobals[0].isActiveAndEnabled == false)
            {
                Debug.LogError("Cannot " + errorMessageDo + " for " + scene.name + " because there is no active VoxGlobalHandler in the scene");
                return null;
            }

            return voxGlobals[0];
        }

        /// <summary>
        /// Builds voxelObjects and voxelSystem for all colliders in the activeScene
        /// </summary>
        [MenuItem("Tools/Voxel System/Build Active Scene")]
        public static void BuildActiveScene()
        {
            BuildScene(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// Builds voxelObjects and voxelSystem for all colliders in all scenes found
        /// (In editMode its scenes included in build, at runtime its all loaded scenes)
        /// </summary>
        [MenuItem("Tools/Voxel System/Build All Scenes")]
        public static void BuildAllScenes()
        {
            foreach (var scene in GetAllScenes())
            {
                BuildScene(scene);
            }
        }

        /// <summary>
        /// Clears the voxelSystem for the active scene
        /// </summary>
        [MenuItem("Tools/Voxel System/Clear Active Scene")]
        public static void ClearActiveScene()
        {
            ClearVoxelSystemForScene(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// Clears all built voxelObjects and clears the voxelSystem for all scenes found
        /// (In editMode its scenes included in build, at runtime its all loaded scenes)
        /// </summary>
        [MenuItem("Tools/Voxel System/Clear Voxel Cache")]
        public static void ClearWholeVoxelSystem()
        {
            VoxSavedData savedData = VoxHelpFunc.TryGetVoxelSaveAsset();
            if (savedData == null) return;

            savedData.ClearVoxelObjects();

            foreach (var scene in GetAllScenes())
            {
                ClearVoxelSystemForScene(scene);
            }
        }

        /// <summary>
        /// Clears the voxelSystem for the given scene
        /// </summary>
        public static void ClearVoxelSystemForScene(Scene scene)
        {
            var voxGlobal = VerifyScene(scene, "clear voxelSystem", true);
            if (voxGlobal == null) return;

            voxGlobal.ClearEditorVoxelSystem();

            foreach (GameObject rootObj in scene.GetRootGameObjects())
            {
                //Clear colliders from voxParents
                foreach (VoxParent voxParent in rootObj.GetComponentsInChildren<VoxParent>(true))
                {
                    voxParent.voxCols.Clear();
                }
            }

#if UNITY_EDITOR
            if (Application.isPlaying == false) EditorSceneManager.MarkSceneDirty(scene);
#endif
        }

        /// <summary>
        /// Returns all scenes that are included in the build
        /// </summary>
        private static Scene[] GetAllScenes()
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false)
            {
                int sceneCountE = SceneManager.sceneCountInBuildSettings;
                Scene[] scenesE = new Scene[sceneCountE];

                for (int i = 0; i < sceneCountE; i++)
                {
                    scenesE[i] = SceneManager.GetSceneByBuildIndex(i);
                }

                if (scenesE.Length == 0)
                {
                    Debug.LogError("There are no scenes included in the build");
                }

                return scenesE;
            }
#endif

            int sceneCount = SceneManager.loadedSceneCount;
            Scene[] scenes = new Scene[sceneCount];

            for (int i = 0; i < sceneCount; i++)
            {
                scenes[i] = SceneManager.GetSceneAt(i);
            }

            if (scenes.Length == 0)
            {
                Debug.LogError("There are no loaded scenes");
            }

            return scenes;
        }
    }
}

