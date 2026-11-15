using UnityEngine;

// The collider set of a prop definition (05 N16), simplified to boxes derived from the model's bounds and
// its flat surfaces. Every prop has one, small props and items in furniture slots included (G26): one box
// for most, several for tables, seats, shelves and hollow furniture. Only grass tufts (ground cover) have none. Generated props and disguises use the same set, so a dart that passes under a hider's
// table misses it exactly as it would miss a real one. All sizes are in model units: the object's uniform
// scale turns them into metres.
public static class PropColliders
{
    static BoxCollider Add(GameObject go, Vector3 mn, Vector3 mx)
    {
        var c = go.AddComponent<BoxCollider>();
        c.center = (mn + mx) * 0.5f;
        c.size = new Vector3(Mathf.Max(0.001f, mx.x - mn.x), Mathf.Max(0.001f, mx.y - mn.y), Mathf.Max(0.001f, mx.z - mn.z));
        return c;
    }

    public static int LayerFor(int def)
    {
        int fl = GenData.defFlags[def];
        if ((fl & GenData.FL_SCENERY) != 0) return Layers.Arch;
        return (fl & GenData.FL_SOLID) != 0 ? Layers.Solid : Layers.Prop;
    }

    // Adds the def's colliders to go (whose local scale is the def's scale). Door parts are separate objects.
    public static void Build(GameObject go, int def)
    {
        int model = GenData.defModel[def];
        int kind = GenData.defCollider[def];
        int fl = GenData.defFlags[def];
        float sc = GenData.defScale[def] / 1000f;
        Bounds b = ModelLibrary.bounds[model];
        Vector3 mn = b.min, mx = b.max;
        float top = GenData.defTopY[def] / 1000f / sc;     // main top surface, model units
        float cm = 0.01f / sc;                               // one centimetre in model units
        if ((fl & GenData.FL_DECOR) != 0) return;
        switch (kind)
        {
            case GenData.C_TABLE:
            {
                float slab = Mathf.Max(0.1f * (mx.y - mn.y), 5f * cm);
                float bottom = Mathf.Max(mn.y, top - slab);
                Add(go, new Vector3(mn.x, bottom, mn.z), new Vector3(mx.x, Mathf.Max(top, mx.y), mx.z));
                float leg = Mathf.Max(0.09f * Mathf.Min(mx.x - mn.x, mx.z - mn.z), 5f * cm);
                float inset = 2f * cm;
                for (int i = 0; i < 4; i++)
                {
                    float x0 = (i & 1) == 0 ? mn.x + inset : mx.x - inset - leg;
                    float z0 = (i & 2) == 0 ? mn.z + inset : mx.z - inset - leg;
                    Add(go, new Vector3(x0, mn.y, z0), new Vector3(x0 + leg, bottom, z0 + leg));
                }
                break;
            }
            case GenData.C_SEAT:
            {
                float seat = Mathf.Clamp(top, mn.y + 5f * cm, mx.y);
                Add(go, mn, new Vector3(mx.x, seat, mx.z));
                Add(go, new Vector3(mn.x, seat, mn.z), new Vector3(mx.x, mx.y, mn.z + 0.25f * (mx.z - mn.z)));
                break;
            }
            case GenData.C_HOLLOW:
            {
                float t = Mathf.Max(4f * cm, 0.04f * (mx.x - mn.x));
                Add(go, mn, new Vector3(mn.x + t, mx.y, mx.z));                                   // left
                Add(go, new Vector3(mx.x - t, mn.y, mn.z), mx);                                   // right
                Add(go, mn, new Vector3(mx.x, mx.y, mn.z + t));                                   // back
                Add(go, new Vector3(mn.x, mx.y - t, mn.z), mx);                                   // top
                Add(go, mn, new Vector3(mx.x, mn.y + Mathf.Max(t, InsideFloor(model)), mx.z));    // bottom
                break;
            }
            case GenData.C_SHELF:
            {
                bool generated = (fl & GenData.FL_GENERATED) != 0;
                int a = GenModels.SurfStart[model], e = GenModels.SurfStart[model + 1];
                int maxArea = 0;
                for (int i = a; i < e; i++) if (GenModels.Surf[i * 6 + 5] > maxArea) maxArea = GenModels.Surf[i * 6 + 5];
                for (int i = a; i < e; i++)
                {
                    if (GenModels.Surf[i * 6 + 5] * 2 < maxArea) continue;
                    float y = GenModels.Surf[i * 6] / 1000f;
                    Add(go, new Vector3(mn.x, y - 3f * cm, mn.z), new Vector3(mx.x, y, mx.z));
                }
                if (!generated)
                {
                    float t = 3f * cm;
                    Add(go, mn, new Vector3(mn.x + t, mx.y, mx.z));
                    Add(go, new Vector3(mx.x - t, mn.y, mn.z), mx);
                    Add(go, mn, new Vector3(mx.x, mx.y, mn.z + t));
                }
                break;
            }
            case GenData.C_TRUNK:
            {
                float r = 0.22f / sc;
                Add(go, new Vector3(-r, mn.y, -r), new Vector3(r, mx.y, r));
                break;
            }
            case GenData.C_TENT:
            {
                float t = 6f * cm;
                float h = mx.y - mn.y;
                Add(go, mn, new Vector3(mx.x, mn.y + 0.75f * h, mn.z + t));
                Add(go, mn, new Vector3(mn.x + t, mn.y + 0.5f * h, mx.z));
                Add(go, new Vector3(mx.x - t, mn.y, mn.z), new Vector3(mx.x, mn.y + 0.5f * h, mx.z));
                break;
            }
            case GenData.C_NONE:
                // rugs: a slab 3 cm high whatever the model's thickness, so every body's step height takes it
                Add(go, mn, new Vector3(mx.x, mn.y + 3f * cm, mx.z));
                break;
            default:
                Add(go, mn, new Vector3(mx.x, Mathf.Max(mx.y, mn.y + 2f * cm), mx.z));
                break;
        }
    }

    // height of the lowest inside surface of a hollow model (the floor a hider stands on), model units
    static float InsideFloor(int model)
    {
        int a = GenModels.SurfStart[model], e = GenModels.SurfStart[model + 1];
        float best = 0f;
        float h = GenModels.SizeMm[model * 3 + 1] / 1000f;
        for (int i = a; i < e; i++)
        {
            float y = GenModels.Surf[i * 6] / 1000f;
            if (y > 0.01f && y < 0.3f * h && y > best) best = y;
        }
        return best;
    }

    // the hollow of an openable, in the prop's local space
    public static Bounds InsideBounds(int def)
    {
        int model = GenData.defModel[def];
        Bounds b = ModelLibrary.bounds[model];
        float t = 0.05f * b.size.x;
        var r = new Bounds();
        r.SetMinMax(new Vector3(b.min.x + t, b.min.y, b.min.z + t), new Vector3(b.max.x - t, b.max.y - t, b.max.z - t));
        return r;
    }
}
