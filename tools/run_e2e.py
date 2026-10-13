# -*- coding: utf-8 -*-
u"""端到端打字测试。用 Python 起子进程 —— PowerShell 起子进程会卡死。"""
import os, subprocess, time, shutil, io, sys
BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(BASE, "out"); DATA = os.path.join(BASE, ".e2e")
if os.path.exists(DATA): shutil.rmtree(DATA, ignore_errors=True)
os.makedirs(DATA, exist_ok=True)
env = dict(os.environ)
env["MYIME_DATA_DIR"] = DATA
env["MYIME_ACCEPT_INJECTED"] = "1"
env["MYIME_LOG_LEVEL"] = "info"
core = subprocess.Popen([os.path.join(OUT, "MyIme.Core.exe")], cwd=OUT, env=env)
time.sleep(4)
tt = subprocess.Popen([os.path.join(OUT, "TypeTest.exe")], cwd=OUT, env=env,
                      stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
try:
    res = tt.communicate(timeout=150)[0].decode("utf-8", "ignore")
except Exception:
    tt.kill(); res = tt.communicate()[0].decode("utf-8", "ignore")
if core.poll() is None: core.kill()
time.sleep(0.8)
lines = [l for l in res.split("\n") if "[OK]" in l or "[FAIL]" in l]
ok = sum(1 for l in lines if "[OK]" in l)
bad = sum(1 for l in lines if "[FAIL]" in l)
name = sys.argv[1] if len(sys.argv) > 1 else "_e2e_out.txt"
io.open(os.path.join(BASE, name), "w", encoding="utf-8").write(
    u"PASS=%d FAIL=%d\n" % (ok, bad) + u"\n".join(lines))
print("PASS=%d FAIL=%d" % (ok, bad))
shutil.rmtree(DATA, ignore_errors=True)
