# -*- coding: utf-8 -*-
import os, socket, subprocess, threading, time
REPO = r"D:\输入法"; PORT = 18957
CAND = ["140.82.113.3","140.82.112.3","140.82.114.3","20.205.243.165","140.82.121.3"]
env = dict(os.environ); env["GIT_TERMINAL_PROMPT"]="0"
def pump(a,b):
    try:
        while True:
            d=a.recv(65536)
            if not d: break
            b.sendall(d)
    except Exception: pass
    finally:
        try: b.shutdown(socket.SHUT_WR)
        except Exception: pass
def handle(cli,t):
    try:
        cli.settimeout(20); buf=b""
        while b"\r\n\r\n" not in buf:
            d=cli.recv(4096)
            if not d: return
            buf+=d
        if not buf.split(b"\r\n")[0].upper().startswith(b"CONNECT"):
            cli.sendall(b"HTTP/1.1 405\r\n\r\n"); return
        srv=socket.create_connection((t,443),timeout=15)
        cli.sendall(b"HTTP/1.1 200 Connection established\r\n\r\n")
        cli.settimeout(None); srv.settimeout(None)
        x=threading.Thread(target=pump,args=(cli,srv)); y=threading.Thread(target=pump,args=(srv,cli))
        x.daemon=y.daemon=True; x.start(); y.start(); x.join(40); y.join(40); srv.close()
    except Exception: pass
    finally:
        try: cli.close()
        except Exception: pass
r = subprocess.run(["git","push"], cwd=REPO, capture_output=True, text=True, env=env, timeout=60)
if r.returncode == 0: print("OK-direct")
else:
    for ip in CAND:
        try:
            s=socket.create_connection((ip,443),timeout=4); s.close()
        except Exception: continue
        stop=threading.Event()
        def serve():
            s=socket.socket(); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1)
            s.bind(("127.0.0.1",PORT)); s.listen(16); s.settimeout(1.0)
            while not stop.is_set():
                try: cli,_=s.accept()
                except socket.timeout: continue
                except Exception: break
                threading.Thread(target=handle,args=(cli,ip),daemon=True).start()
            s.close()
        threading.Thread(target=serve,daemon=True).start(); time.sleep(0.4)
        r = subprocess.run(["git","-c","http.proxy=http://127.0.0.1:%d"%PORT,"push"],
                           cwd=REPO, capture_output=True, text=True, env=env, timeout=90)
        stop.set()
        if r.returncode == 0: print("OK-proxy-" + ip); break
        print("fail-" + ip)
