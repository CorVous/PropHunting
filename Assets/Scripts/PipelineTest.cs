using UnityEngine;

// Temporary: proves the headless WebGL pipeline works before the real game is added.
public class PipelineTest : MonoBehaviour
{
    Transform cube;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("PipelineTest");
        go.AddComponent<PipelineTest>();
    }

    void Start()
    {
        var camGo = new GameObject("Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        camGo.transform.position = new Vector3(0, 1.5f, -4);
        camGo.AddComponent<AudioListener>();
        var lightGo = new GameObject("Sun");
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(50, 30, 0);
        cube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        cube.position = new Vector3(0, 1.5f, 0);
        var sh = Shader.Find("PropHunt/Lit");
        if (sh != null) cube.GetComponent<Renderer>().material = new Material(sh);
    }

    void Update()
    {
        cube.Rotate(0, 40 * Time.deltaTime, 0);
    }

    void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 600, 30), "Prop Hunt MVP: build pipeline test. t=" + Time.time.ToString("0.0"));
    }
}
