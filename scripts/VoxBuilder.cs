//https://github.com/Zombie1111/UnityVoxelSystem
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
        //Test later, use collider.raycast and send raycast towards collider center, if no collision we are inside collider

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
                if (voxGlobal.ValidateVoxelSystem() == false) return false;

                float colCount = colIdToCol.Count;
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
                else
#endif
                    voxGlobal.SetupVoxelSystem();
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

        public class ColsType
        {
            public HashSet<Collider> cols;
            public byte type;
        }

        /// <summary>
        /// Returns a dictorary, the keys are all colliderIds, the values are all colliders that uses that colId (Returns null if invalid scene)
        /// </summary>
        private static Dictionary<int, ColsType> GetAllColliderIdsInScene(Scene scene, out VoxGlobalHandler voxGlobal)
        {
            voxGlobal = VerifyScene(scene, "Cannot build voxel objects for " + scene.name, false);
            if (voxGlobal == null) return null;

            Dictionary<int, ColsType> colIdToCols = new();

            foreach (GameObject rootObj in scene.GetRootGameObjects())
            {
                GetAllColliderIdsInChildren(rootObj, ref colIdToCols);
            }

            return colIdToCols;
        }

        /// <summary>
        /// If colIdToCols aint null, adds all colliderIds that exists in any children of rootObj to it.
        /// Always updates voxCols for any VoxParent found
        /// </summary>
        public static void GetAllColliderIdsInChildren(GameObject rootObj, ref Dictionary<int, ColsType> colIdToCols)
        {
            //Clear colliders from voxParents
            foreach (VoxParent voxParent in rootObj.GetComponentsInChildren<VoxParent>(true))
            {
                voxParent.voxCols.Clear();
            }

            //Get all colliders
            bool isEditMode = !Application.isPlaying;

            foreach (Collider colLoop in rootObj.GetComponentsInChildren<Collider>(true))
            {
                //Get if colldier should be included
                Collider col = colLoop;
                bool usedParent = false;
                var voxParent = col.GetComponentInParent<VoxParent>(true);

                if (voxParent != null)
                {
                    //We have voxParent, use its settings to know if colldier is included or not
                    bool isSelf = col.transform == voxParent.transform;
                    bool colIsActive = col.enabled == true && col.gameObject.activeInHierarchy == true;

                    if ((isSelf == true && voxParent.affectSelf == false)
                        || (isSelf == false && voxParent.affectChildren == false)) goto SkipVoxParent;

                    if (voxParent.buildOnStart == true && (isEditMode == true || colIsActive == false)) continue;

                    if ((isSelf == true && voxParent.ignoreSelf == true)
                        || (isSelf == false && voxParent.ignoreChildren == true)) continue;

                    if ((col.isTrigger == true && voxParent.includeTriggers == false)
                        || (colIsActive == false && voxParent.includeInative == false)) continue;

                    usedParent = true;
                }

            SkipVoxParent:;

                if (usedParent == false)
                {
                    //No voxParent, use standard include settings
                    if (col.isTrigger == true || col.enabled == false || col.gameObject.activeInHierarchy == false) continue;
                }
                else if (voxParent.voxelColliderOverwrite != null)
                {
                    //If parent has collider overwrite use it
                    col = voxParent.voxelColliderOverwrite;
                }

                //Get collider id
                byte voxType = usedParent == true ? voxParent.voxelType : VoxGlobalSettings.defualtType;
                int colId = col.GetColId(voxType);

                //Notify voxParent that it has this collider
                if (usedParent == true)
                {
                    //if (voxParent.buildOnStart == true) continue;//Since its computed on start it should count as not included in scene
                    voxParent.voxCols.Add(new() { col = col, colId = colId, colType = voxType });
                }

                //Add collider and colId to the returned dictorary
                if (colIdToCols == null) continue;

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

        /// <summary>
        /// Returns null if the scene is invalid, returns the scene VoxGlobalHandler if valid scene
        /// </summary>
        private static VoxGlobalHandler VerifyScene(Scene scene, string errorMessageDo, bool ignoreIfNotInBuild = false)
        {
            //Verify scene
            if (scene == null || scene.IsValid() == false)
            {
                Debug.LogError(errorMessageDo + " because the scene is invalid");
                return null;
            }

            if (scene.isLoaded == false)
            {
                Debug.LogError(errorMessageDo + " because the scene is not loaded");
                return null;
            }

            if (scene.isSubScene == true)
            {
                Debug.LogError(errorMessageDo + " because its a subScene");
                return null;
            }

#if UNITY_EDITOR
            if (ignoreIfNotInBuild == false
                && (scene.buildIndex == SceneManager.sceneCountInBuildSettings || scene.buildIndex < 0))
            {
                Debug.LogError(errorMessageDo + " because the scene is not included in the build");
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
                Debug.LogError(errorMessageDo + " because there is more than one VoxGlobalHandlers in the scene");
                return null;
            }

            if (voxGlobals.Count == 0 || voxGlobals[0].isActiveAndEnabled == false)
            {
                Debug.LogError(errorMessageDo + " because there is no active VoxGlobalHandler in the scene");
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
            if (AskEditorIfCanSave(true) == false) return;
            BuildScene(SceneManager.GetActiveScene());
#if UNITY_EDITOR
            if (Application.isPlaying == false) EditorSceneManager.SaveOpenScenes();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Builds voxelObjects and voxelSystem for all colliders in all scenes included in build (Editor only)
        /// </summary>
        [MenuItem("Tools/Voxel System/Build All Scenes")]
        public static void BuildAllScenes()
        {
            if (Application.isPlaying == true)
            {
                Debug.LogError("Can only BuildAllScenes in edit mode");
                return;
            }

            var ogScene = SceneManager.GetActiveScene();
            if (VerifyScene(ogScene, "Cannot build voxel objects while " + ogScene.name + "is loaded", false) == null) return;
            if (AskEditorIfCanSave(true) == false) return;

            int ogSceneI = ogScene.buildIndex;

            foreach (var sceneI in GetAllScenes())
            {
                EditorSceneManager.SaveOpenScenes();
                EditorSceneManager.OpenScene(SceneUtility.GetScenePathByBuildIndex(sceneI));
                BuildScene(SceneManager.GetSceneByBuildIndex(sceneI));
            }

            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(SceneUtility.GetScenePathByBuildIndex(ogSceneI));
        }
#endif

        /// <summary>
        /// Clears the voxelSystem for the active scene
        /// </summary>
        [MenuItem("Tools/Voxel System/Clear Active Scene")]
        public static void ClearActiveScene()
        {
            if (AskEditorIfCanSave(true) == false) return;
            ClearVoxelSystemForScene(SceneManager.GetActiveScene());
#if UNITY_EDITOR
            if (Application.isPlaying == false) EditorSceneManager.SaveOpenScenes();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Clears all built voxelObjects and clears the voxelSystem for all scenes included in build (Editor only)
        /// </summary>
        [MenuItem("Tools/Voxel System/Clear Voxel Cache")]
        public static void ClearWholeVoxelSystem()
        {
            if (Application.isPlaying == true)
            {
                Debug.LogError("Can only ClearWholeVoxelSystem in edit mode");
                return;
            }

            VoxSavedData savedData = VoxHelpFunc.TryGetVoxelSaveAsset();
            if (savedData == null) return;

            var ogScene = SceneManager.GetActiveScene();
            if (VerifyScene(ogScene, "Cannot clear voxelSystems while " + ogScene.name + "is loaded", false) == null) return;
            if (AskEditorIfCanSave(true) == false) return;

            int ogSceneI = ogScene.buildIndex;

            savedData.ClearVoxelObjects();

            foreach (var sceneI in GetAllScenes())
            {
                EditorSceneManager.SaveOpenScenes();
                EditorSceneManager.OpenScene(SceneUtility.GetScenePathByBuildIndex(sceneI));
                ClearVoxelSystemForScene(SceneManager.GetSceneByBuildIndex(sceneI));
            }

            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(SceneUtility.GetScenePathByBuildIndex(ogSceneI));
        }
#endif

        /// <summary>
        /// Clears the voxelSystem for the given scene
        /// </summary>
        public static void ClearVoxelSystemForScene(Scene scene)
        {
            var voxGlobal = VerifyScene(scene, "Cannot clear voxelSystem for " + scene.name, true);
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
            else
#endif
                voxGlobal.SetupVoxelSystem();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Returns all scene build indexes (Editor only)
        /// </summary>
        private static int[] GetAllScenes()
        {
            if (Application.isPlaying == false)
            {
                int sceneCountE = SceneManager.sceneCountInBuildSettings;
                int[] scenesE = new int[sceneCountE];

                for (int i = 0; i < sceneCountE; i++)
                {
                    scenesE[i] = i;
                }

                if (scenesE.Length == 0)
                {
                    Debug.LogError("There are no scenes included in the build");
                }

                return scenesE;
            }

            return new int[0];
        }
#endif

        /// <summary>
        /// Returns false if the user dont wanna save stuff, always true at runtime
        /// </summary>
        private static bool AskEditorIfCanSave(bool willClear)
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false && EditorSceneManager.GetActiveScene().isDirty == true
    && EditorUtility.DisplayDialog("", willClear == true ? "All open scenes must be saved before clearing voxelSystem!"
    : "All open scenes must be saved before building voxelSystem!", willClear == true ? "Save and clear" : "Save and build", "Cancel") == false)
            {
                return false;
            }
#endif

            return true;
        }

#if UNITY_EDITOR
        [MenuItem("Tools/Voxel System/Toggle Draw Editor Voxels")]
        private static void ToggleAllDrawEditorVoxels()
        {
            if (Application.isPlaying == false)
            {
                Debug.LogError("Can only toggle at runtime");
                return;
            }

            bool didToggleAny = false;

            foreach (var vHandler in GameObject.FindObjectsByType<VoxGlobalHandler>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                vHandler.debugDoUpdateVisualVoxels = !vHandler.debugDoUpdateVisualVoxels;
                didToggleAny = true;
            }

            if (didToggleAny == true) return;

            Debug.LogError("Found no active VoxGlobalHandler in the current scene");
        }
#endif
    }
}

