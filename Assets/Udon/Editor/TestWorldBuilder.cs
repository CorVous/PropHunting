using System;
using System.IO;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Editor.Builder;
using VRC.Editor;
using VRC.SDKBase.Editor;

// Local multi-client tests (as PropHunt Station Lab did): the world scene with a PHTestPilot that plays every
// client by itself, exported as a .vrcw for Tools/Launch-Clients.ps1. The result path goes to
// PropHunt_last_build.txt ("OK" and the path, or "FAILED" and the error). Build world scene again afterwards for
// a normal world (it drops the pilot).
public static class TestWorldBuilder
{
    const string ResultFile = "PropHunt_last_build.txt";

    [MenuItem("PropHunt/Build test world (pilot) and export")]
    public static void BuildTest()
    {
        try
        {
            File.WriteAllText(ResultFile, "RUNNING");
            WorldSceneBuilder.Build();
            var game = UnityEngine.Object.FindObjectOfType<PHGame>(true);
            var go = new GameObject("TestPilot");
            go.transform.SetParent(game.transform.parent, false);
            var pilot = go.AddUdonSharpComponent<PHTestPilot>();
            pilot.game = game; pilot.local = game.local;
            UdonSharpEditorUtility.CopyProxyToUdon(pilot);
            EditorSceneManager.SaveScene(game.gameObject.scene);
            string path = Export();
            File.WriteAllText(ResultFile, "OK\n" + path);
            Debug.Log("[prophunt] exported the test world: " + path);
        }
        catch (Exception e)
        {
            File.WriteAllText(ResultFile, "FAILED\n" + e);
            Debug.LogError("[prophunt] test build failed: " + e);
        }
    }

    // the SDK's own local export (Station Lab's StationLabBuilder.Export)
    public static string Export()
    {
        if (!UpdateLayers.AreLayersSetup()) UpdateLayers.SetupEditorLayers();
        if (!UpdateLayers.IsCollisionLayerMatrixSetup()) UpdateLayers.SetupCollisionLayerMatrix();
        EnvConfig.ConfigurePlayerSettings();
        EditorPrefs.SetBool("VRC.SDKBase_StripAllShaders", false);
        EditorPrefs.DeleteKey("lastVRCPath");
        VRC_SdkBuilder.shouldBuildUnityPackage = false;
        VRC_SdkBuilder.PreBuildBehaviourPackaging();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var setBuilder = typeof(VRC_SdkBuilder).GetMethod("SetCurrentBuilder", flags);
        if (setBuilder == null) throw new Exception("VRC_SdkBuilder.SetCurrentBuilder not found");
        setBuilder.MakeGenericMethod(typeof(ISDKWorldBuilder)).Invoke(null, null);
        var clear = typeof(VRC_SdkBuilder).GetMethod("ClearCallbacks", flags);
        if (clear != null) clear.Invoke(null, null);
        if (!VRC_SdkBuilder.RunExportSceneResource()) throw new Exception("RunExportSceneResource returned false");
        string path = UnityEngine.Networking.UnityWebRequest.UnEscapeURL(EditorPrefs.GetString("lastVRCPath"));
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new Exception("No .vrcw produced (lastVRCPath='" + path + "')");
        return path;
    }
}
