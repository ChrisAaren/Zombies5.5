"""One-command runner: creates work/<character>/vNNN, runs stages 1-4 and check.py, publishes on a pass.

  python Tools/CharacterPipeline/run.py <character> <source.fbx|.blend|.obj> [--config file.json] [--no-publish] [--stage N] [--unity]
  python Tools/CharacterPipeline/run.py <character> --unity-only      # only the Unity check (stage 6) on the published FBX

--stage N: run only stages N.. in the latest existing version folder (re-run after hand edits); default runs all in a new folder.
Plain Python 3 (it only launches Blender subprocesses). Env BLENDER overrides the Blender path.
"""
import os, sys, re, json, shutil, subprocess, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
BLENDER = os.environ.get("BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")

UNITY_CLI = os.environ.get("UNITY_CLI", r"C:\Users\<user>\AppData\Local\Unity\bin\unity.exe")
UNITY_EDITOR = os.environ.get("UNITY_EDITOR", r"D:\Unity\6000.6.9f1\Editor\Unity.exe")


def unity_check(name):
    """Stage 6. Open Editor on this project -> `unity command script eval` (CharacterUnityCheck.Run);
    otherwise Unity batch mode (-executeMethod CharacterUnityCheck.RunBatch). Returns 0 on pass."""
    proj = os.path.normcase(os.path.normpath(ROOT))
    open_editor = False
    try:
        r = subprocess.run([UNITY_CLI, "status", "--format", "json"], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60)
        for inst in json.loads(r.stdout).get("data", {}).get("instances", []):
            if os.path.normcase(os.path.normpath(inst.get("projectPath", ""))) == proj and inst.get("state") == "ready":
                open_editor = True
    except Exception as e:
        print("unity status failed (%s); assuming no open Editor" % e)
    if open_editor:
        print("unity check: open Editor found, using script eval")
        code = 'return CharacterUnityCheck.Run("%s");' % name
        r = subprocess.run([UNITY_CLI, "command", "script", "eval", "--", "--code", code, "--timeout", "300000", "--format", "json"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=ROOT)
        print(r.stdout[-2000:], r.stderr[-1000:])
        try:
            out = json.loads(r.stdout)
            if isinstance(out, dict):
                out = out.get("data", {}).get("result", out)
            summ = json.loads(out) if isinstance(out, str) else out
            return 0 if summ["pass"] else 1
        except Exception as e:
            print("could not parse the eval result:", e)
            return 1
    log = os.path.join(HERE, "work", name, "unity_batch.log")
    print("unity check: no open Editor, batch mode (log: %s)" % log)
    cmd = [UNITY_EDITOR, "-batchmode", "-projectPath", ROOT, "-executeMethod", "CharacterUnityCheck.RunBatch", "-character", name, "-logFile", log]
    r = subprocess.run(cmd)
    print("unity batch exit code:", r.returncode, "(0 = all checks pass)")
    return r.returncode


def main():
    a = sys.argv[1:]
    if len(a) >= 2 and a[1] == "--unity-only":
        return unity_check(a[0])
    if len(a) < 2:
        print(__doc__)
        return 2
    name, source = a[0], os.path.abspath(a[1])
    cfg = os.path.join(HERE, "pipeline_config.json")
    publish = "--no-publish" not in a
    if "--config" in a:
        cfg = os.path.abspath(a[a.index("--config") + 1])
    start = int(a[a.index("--stage") + 1]) if "--stage" in a else 1
    wdir = os.path.join(HERE, "work", name)
    os.makedirs(wdir, exist_ok=True)
    vers = sorted(int(m.group(1)) for d in os.listdir(wdir) for m in [re.fullmatch(r"v(\d+)", d)] if m)
    if start == 1:
        n = (vers[-1] if vers else 0) + 1
        vdir = os.path.join(wdir, "v%03d" % n)
        os.makedirs(vdir)
        shutil.copy2(cfg, os.path.join(vdir, "config_used.json"))
    else:
        vdir = os.path.join(wdir, "v%03d" % vers[-1])
        n = vers[-1]
        if "--config" in a:
            shutil.copy2(cfg, os.path.join(vdir, "config_used.json"))
    prev = os.path.join(wdir, "v%03d" % (n - 1)) if n > 1 else ""
    print("version folder:", vdir)
    stages = [
        (1, "cleanup.py", ["--character", name, "--source", source, "--out", vdir]),
        (2, "joints.py", ["--out", vdir]),
        (3, "skeleton.py", ["--out", vdir]),
        (4, "skin.py", ["--character", name, "--out", vdir]),
        (5, "check.py", ["--character", name, "--out", vdir] + (["--prev", prev] if prev else [])),
    ]
    for num, script, args in stages:
        if num < start:
            continue
        t0 = time.time()
        cmd = [BLENDER, "-b", "--factory-startup", "--python", os.path.join(HERE, script), "--"] + args
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        with open(os.path.join(vdir, "log_stage%d.txt" % num), "w", encoding="utf-8") as f:
            f.write(r.stdout + "\n" + r.stderr)
        ok = ("STAGE%d DONE" % num) in r.stdout
        print("stage %d %-12s %s (%.0fs)" % (num, script, "ok" if ok else "FAILED", time.time() - t0))
        if not ok:
            print(r.stdout[-1500:], r.stderr[-1500:])
            return 1
    rep = json.load(open(os.path.join(vdir, "report.json")))
    s = rep["summary"]
    print("RESULT %s %s: %d/%d checks pass (score %.4f)" % (name, os.path.basename(vdir), s["checks_passed"], s["checks_total"], s["score"]))
    for c in rep["checks"]:
        if not c["pass"]:
            print("  FAIL %-52s %s %s %s" % (c["id"], c["value"], c["op"], c["threshold"]))
    print("report:", os.path.join(vdir, "report.json"))
    print("sheet :", os.path.join(vdir, "sheet.png"))
    if rep["overall_pass"] and publish:
        dst = os.path.join(ROOT, "Assets", "Art", "Characters", name)
        os.makedirs(dst, exist_ok=True)
        for f in os.listdir(os.path.join(vdir, "export")):
            shutil.copy2(os.path.join(vdir, "export", f), os.path.join(dst, f))
        print("published to", dst)
    rc = 0 if rep["overall_pass"] else 3
    if "--unity" in a:
        urc = unity_check(name)
        rc = rc or (4 if urc else 0)
    return rc


if __name__ == "__main__":
    sys.exit(main())
