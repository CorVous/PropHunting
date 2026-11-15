using System.Collections.Generic;
using UnityEngine;

// The flying foam dart is only a picture (05 section 5.2): the shot itself is decided at once by a cast.
public class DartFx : MonoBehaviour
{
    Vector3 from, to;
    float t, duration;
    bool leaveMark;
    Vector3 markNormal;
    static readonly List<GameObject> marks = new List<GameObject>();
    static Material markMat;
    static Mesh markMesh;
    static int dartModel = -1;

    public static void Spawn(Vector3 from, Vector3 to, bool mark, Vector3 normal)
    {
        if (dartModel < 0) dartModel = ModelLibrary.Find("bullet-foam");
        var go = new GameObject("Dart");
        go.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.mesh[dartModel];
        go.AddComponent<MeshRenderer>().sharedMaterial = ModelLibrary.lit;
        go.transform.localScale = Vector3.one * 2.2f;
        var fx = go.AddComponent<DartFx>();
        fx.from = from; fx.to = to; fx.leaveMark = mark; fx.markNormal = normal;
        fx.duration = Mathf.Max(0.03f, Vector3.Distance(from, to) / Balance.DartSpeed);
        go.transform.position = from;
        // the model's long axis is Y
        if ((to - from).sqrMagnitude > 1e-6f) go.transform.rotation = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
    }

    void Update()
    {
        t += Time.deltaTime / duration;
        transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(t));
        if (t < 1f) return;
        if (leaveMark) Mark(to, markNormal);
        Destroy(gameObject);
    }

    // a small orange mark where a dart struck architecture; the oldest marks are reused
    static void Mark(Vector3 at, Vector3 normal)
    {
        if (markMat == null)
        {
            markMat = new Material(Shader.Find("PropHunt/Unlit"));
            markMat.SetColor("_Color", new Color(0.95f, 0.45f, 0.12f, 0.9f));
            var mb = new MeshBuf();
            mb.Box(new Vector3(-0.025f, -0.025f, -0.004f), new Vector3(0.025f, 0.025f, 0.004f), new Color32(255, 255, 255, 255));
            markMesh = mb.ToMesh("DartMark");
        }
        GameObject go;
        if (marks.Count >= 40) { go = marks[0]; marks.RemoveAt(0); if (go == null) go = null; }
        else go = null;
        if (go == null)
        {
            go = new GameObject("DartMark");
            go.AddComponent<MeshFilter>().sharedMesh = markMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = markMat;
        }
        go.transform.position = at + normal * 0.006f;
        go.transform.rotation = normal.sqrMagnitude > 0.5f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        marks.Add(go);
    }

    public static void ClearMarks()
    {
        for (int i = 0; i < marks.Count; i++) if (marks[i] != null) Destroy(marks[i]);
        marks.Clear();
    }
}
