using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

// Before a build or upload from the VRChat SDK panel (Cor: a pre-upload check): a scene made by "Build test world
// (pilot)" carries a PHTestPilot, which teleports everyone and starts rounds by itself. Such a scene is refused, with
// a dialog saying how to fix it (PropHunt > Build world scene). The test world's own export (TestWorldBuilder.Export)
// sets Exporting and is let through.
public class PrePublishCheck : IVRCSDKBuildRequestedCallback
{
    public static bool Exporting;   // set by TestWorldBuilder while it exports the pilot world on purpose
    public static bool Quiet;       // tests: no dialog

    public int callbackOrder => 0;

    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        if (requestedBuildType != VRCSDKRequestedBuildType.Scene || Exporting) return true;
        var pilots = Object.FindObjectsOfType<PHTestPilot>(true);
        if (pilots.Length == 0) return true;
        string msg = "This scene contains the test pilot (" + pilots[0].gameObject.name + "), which teleports players and starts rounds by itself.\n\n"
            + "Rebuild the scene with PropHunt > Build world scene, then build or upload again.";
        Debug.LogError("[PropHunt] Build refused: " + msg);
        if (!Application.isBatchMode && !Quiet) EditorUtility.DisplayDialog("PropHunt: test pilot in the scene", msg, "OK");
        return false;
    }
}
