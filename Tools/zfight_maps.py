"""Z-fighting between the things on built maps (Cor): plays N random maps in the Unity editor (through MCP for
Unity, Play mode), runs ZFight.ScanScene() on each and adds up what fights, by the pair of things involved.

    python Tools/zfight_maps.py <path to umcp.py> [N=30] [out=zfight_maps.txt]

The editor must be open on the project with Main.unity and not in Play mode. About 20 s a map."""
import json, sys, time, collections
umcp, n, out = sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 30, sys.argv[3] if len(sys.argv) > 3 else 'zfight_maps.txt'
sys.argv = ['x']
exec(open(umcp).read().split('a = sys.argv[1:]')[0])


def cs(code):
    r = call("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": code, "safety_checks": False}})
    try: return json.loads(r["result"]["content"][0]["text"])["data"]["result"]
    except Exception as e: return "ERR " + str(r)[:600]


GAME = 'var game = UnityEngine.GameObject.Find("World/Game").GetComponent<VRC.Udon.UdonBehaviour>();'
BUILDER = 'VRC.Udon.UdonBehaviour b = null; foreach (var u in UnityEngine.GameObject.Find("Map").GetComponents<VRC.Udon.UdonBehaviour>()) if (u.GetProgramVariable("moodTime") != null) b = u;'
START = '''var game = UnityEngine.GameObject.Find("World/Game").GetComponent<VRC.Udon.UdonBehaviour>();
game.SetProgramVariable("minPlayers", 1);
var lp = VRC.SDKBase.Networking.LocalPlayer;
var zone = UnityEngine.GameObject.Find("World/Lobby/Zone").transform.position;
lp.TeleportTo(zone + UnityEngine.Vector3.down * 1.4f, UnityEngine.Quaternion.identity);
return "ok";'''
if cs('return UnityEditor.EditorApplication.isPlaying ? "p" : "s";') != "p":
    call("tools/call", {"name": "manage_editor", "arguments": {"action": "play"}}); time.sleep(25)
cs(START); time.sleep(2)
total = collections.Counter(); maps = collections.Counter(); where = {}
log = open(out, 'w')
for k in range(n):
    cs(GAME + 'game.SetProgramVariable("debugTheme", %d); game.SendCustomEvent("DebugHider"); return "ok";' % (k % 3))
    t0 = time.time()
    while time.time() - t0 < 150:
        time.sleep(3)
        st = cs(BUILDER + 'return b == null ? "nob" : (b.GetProgramVariable("built") + " " + b.GetProgramVariable("busy"));')
        if st.startswith("True") and time.time() - t0 > 8: break
    code = cs(GAME + 'return (string)game.GetProgramVariable("seedCode");')
    res = cs('return ZFight.ScanScene();')
    lines = [l for l in res.split("\n")[1:] if l.strip()]
    log.write("== %s  %s\n" % (code, res.split("\n")[0]))
    for l in lines:
        p = [x.strip() for x in l.split("|")]
        if len(p) < 4: continue
        key = p[0] + " | " + p[1]
        total[key] += float(p[2]); maps[key] += 1; where.setdefault(key, code + " at " + p[3])
        log.write("   " + l + "\n")
    log.flush()
    print(k, code, len(lines), "pairs", flush=True)
    cs('foreach (var t in UnityEngine.Object.FindObjectsOfType<UnityEngine.Transform>()) if (t.name.StartsWith("Button END ROUND")) { t.GetComponent<UnityEngine.UI.Button>().onClick.Invoke(); return "pressed"; } return "not found";')
    time.sleep(4)
call("tools/call", {"name": "manage_editor", "arguments": {"action": "stop"}})
log.write("\n==== totals over %d maps: maps, cm2, pair, first seen\n" % n)
for key, c in sorted(maps.items(), key=lambda kv: -kv[1]):
    log.write("%4d %9.0f  %s   (%s)\n" % (c, total[key], key, where[key]))
log.close()
print("done", len(maps), "kinds of pair")
