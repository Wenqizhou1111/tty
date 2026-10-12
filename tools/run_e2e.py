# -*- coding: utf-8 -*-
u"""跑端到端打字测试。PowerShell 启动子进程会卡死，所以用 Python 起。"""
import os, subprocess, time, shutil, sys
BASE = r"D:\输入法"; OUT = os.path.join(BASE, "out"); DATA = os.path.join(BASE, ".e2e")
if os.path.exists(DATA): shutil.rmtree(DATA, ignore_errors=True)
os.makedirs(DATA, exist_ok=True)
env = dict(os.environ)
env["MYIME_DATA_DIR"]=DATA; env["MYIME_ACCEPT_INJECTED"]="1"; env["MYIME_LOG_LEVEL"]="info"
core = subprocess.Popen([os.path.join(OUT,"MyIme.Core.exe")], cwd=OUT, env=env)
time.sleep(4)
tt = subprocess.Popen([os.path.join(OUT,"TypeTest.exe")], cwd=OUT, env=env,
                      stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
try: out = tt.communicate(timeout=90)[0].decode("utf-8","ignore")
except Exception:
    tt.kill(); out = tt.communicate()[0].decode("utf-8","ignore")
if core.poll() is None: core.kill()
shutil.rmtree(DATA, ignore_errors=True)
print("TypeTest exit:", tt.returncode)
keep = [l for l in out.split("\n") if ("[OK]" in l or "[FAIL]" in l or "全部通过" in l or "失败" in l)]
print("\n".join(keep))
