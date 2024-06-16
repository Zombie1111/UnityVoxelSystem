using UnityEngine.SceneManagement;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using System.ComponentModel;

namespace zombVoxels
{
    public class VoxBuilder : MonoBehaviour
    {
        private static bool BuildVoxelObjectsForScene(Scene scene)
        {
            var colIdToCol = GetAllColliderIdsInScene(scene);
            if (colIdToCol == null) return false;

            return true;
        }

        /// <summary>
        /// Returns a dictorary, the keys are all colliderIds, the values are all colliders that uses that colId (Returns null if invalid scene)
        /// </summary>
        private static Dictionary<int, HashSet<Collider>> GetAllColliderIdsInScene(Scene scene)
        {
            if (VerifyScene(scene) == false) return null;

            Dictionary<int, HashSet<Collider>> colIdToCols = new();

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
                    int colId = col.GetColId(usedParent == true ? voxParent.voxelType : 0);

                    //Notify voxParent that it has this collider
                    if (usedParent == true)
                    {
                        voxParent.voxCols.Add(new() { col = col, colId = colId });
                        if (voxParent.buildOnStart == true) continue;//Since its computed on start it should count as not included in scene
                    }

                    //Add collider and colId to the returned dictorary
                    if (colIdToCols.TryGetValue(colId, out HashSet<Collider> cols) == false)
                    {
                        cols = new();
                    }

                    cols.Add(col);
                    colIdToCols[colId] = cols;
                }
            }

            return colIdToCols;
        }

        private static bool VerifyScene(Scene scene)
        {
            //Verify scene
            if (scene.isSubScene == true)
            {
                Debug.LogError("Cannot build voxel objects for " + scene.name + " because its a subScene");
                return false;
            }


            if (scene.buildIndex == EditorBuildSettings.scenes.Length || scene.buildIndex < 0)
            {
                Debug.LogError("Cannot build voxel objects for " + scene.name + " because the scene is not included in the build");
                return false;
            }

            return true;
        }

        public static void BuildLoadedScenes()
        {
            BuildVoxelObjectsForScene(EditorSceneManager.GetActiveScene());
        }

        public static void BuildAllScenes()
        {
            int sceneCount = EditorBuildSettings.scenes.Length;
            for (int i = 0; i < sceneCount; i++)
            {
                BuildVoxelObjectsForScene(EditorSceneManager.GetSceneByBuildIndex(i));
            }
        }
    }
}

