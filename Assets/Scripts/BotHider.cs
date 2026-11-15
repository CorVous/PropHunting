using System.Collections.Generic;
using UnityEngine;

// A bot hider: a prop that looks exactly like a generated prop of its type (same mesh, scale, material and
// collider set), picks a plausible place, stays still, gives forced taunts, sometimes relocates, and runs
// when it is hit. Caught at 0 health: out for the round (G6).
public class BotHider : MonoBehaviour
{
    public int def, disguiseClass;
    public int health, maxHealth;
    public bool alive = true;
    public float survived;              // seconds in the hunt, set when caught or at the end
    public bool caught;
    public string label = "";
    public string spotNote = "";
    public float nextTaunt;
    public bool moving;
    public Vector3 lastSeenPos;
    public PropInstance support;        // the furniture this hider stands on or in, if any

    List<int> path;
    int pathIndex;
    float speed;
    float flinchUntil;
    Vector3 flinchBase;
    float yawTarget;
    bool hasYawTarget;
    Vector3 finalPos;
    bool hasFinal;

    public void Init(int defIndex, Vector3 pos, float yaw, string name, string note)
    {
        def = defIndex; label = name; spotNote = note;
        disguiseClass = Mathf.Clamp(GenData.defDisguise[def] - 1, 0, 2);
        maxHealth = Balance.HiderHealth[disguiseClass]; health = maxHealth;
        float sc = GenData.defScale[def] / 1000f;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        transform.localScale = new Vector3(sc, sc, sc);
        gameObject.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.mesh[GenData.defModel[def]];
        gameObject.AddComponent<MeshRenderer>().sharedMaterial = ModelLibrary.lit;
        PropColliders.Build(gameObject, def);
        gameObject.layer = Layers.HiderSolid;   // as solid as the real prop, whatever its size (G26)
        lastSeenPos = pos;
    }

    public Vector3 Centre { get { return transform.position + Vector3.up * (GenData.defSy[def] / 2000f); } }

    // walk the cells of a path, then take the final pose
    public void Relocate(List<int> cells, Vector3 endPos, float endYaw, bool run, string note)
    {
        if (cells == null || cells.Count < 2) return;
        path = cells; pathIndex = 1; moving = true;
        speed = Balance.HiderRun[disguiseClass] * (run ? 1f : Balance.DesktopWalkFactor);
        finalPos = endPos; hasFinal = true; yawTarget = endYaw; hasYawTarget = true;
        support = null;
        spotNote = note;
        // a moving disguise has no blocking collider (05 section 3.7)
        gameObject.layer = Layers.Hider;
    }

    void Update()
    {
        if (!alive) return;
        if (Time.time < flinchUntil)
        {
            // a 0.2 s shake seen by everyone (05 section 3.9)
            float a = Mathf.Sin(Time.time * 90f) * 0.03f;
            transform.position = flinchBase + new Vector3(a, 0f, -a);
            return;
        }
        if (!moving)
        {
            // the furniture it stood on was knocked away: it falls and stands where it lands, without tumbling (05 section 3.9)
            if (support != null && support.displaced && transform.position.y > 0.001f)
                transform.position = new Vector3(transform.position.x, Mathf.Max(0f, transform.position.y - 4f * Time.deltaTime), transform.position.z);
            return;
        }
        var map = Game.I.map;
        Vector3 target = pathIndex < path.Count ? map.CellCentre(path[pathIndex]) : finalPos;
        if (pathIndex >= path.Count - 1 && hasFinal) target = finalPos;
        Vector3 p = transform.position;
        // props stay level; a prop on a table top comes down as it sets off
        float y = Mathf.MoveTowards(p.y, target.y, 3f * Time.deltaTime);
        Vector3 flat = Vector3.MoveTowards(new Vector3(p.x, 0f, p.z), new Vector3(target.x, 0f, target.z), speed * Time.deltaTime);
        Vector3 dir = new Vector3(target.x - p.x, 0f, target.z - p.z);
        if (dir.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 360f * Time.deltaTime);
        transform.position = new Vector3(flat.x, y, flat.z);
        if (new Vector2(flat.x - target.x, flat.z - target.z).sqrMagnitude < 0.0009f)
        {
            pathIndex++;
            if (pathIndex >= path.Count)
            {
                moving = false;
                transform.position = finalPos;
                if (hasYawTarget) transform.rotation = Quaternion.Euler(0f, yawTarget, 0f);
                gameObject.layer = Layers.HiderSolid;
            }
        }
    }

    // a dart hit: 25 damage, a flinch, a 0.3 m slide along the dart; true if this hit caught the hider
    public bool TakeHit(int damage, Vector3 dir)
    {
        if (!alive) return false;
        health -= damage;
        Vector3 slide = new Vector3(dir.x, 0f, dir.z).normalized * Balance.HitSlide;
        Vector3 to = transform.position + slide;
        int cell = Game.I.map.CellAt(to);
        if (cell >= 0 && Game.I.map.gen.walk[cell] == 1 && !moving) transform.position = to;
        flinchBase = transform.position;
        flinchUntil = Time.time + 0.2f;
        if (health <= 0)
        {
            alive = false; caught = true;
            gameObject.SetActive(false);
            return true;
        }
        return false;
    }
}
