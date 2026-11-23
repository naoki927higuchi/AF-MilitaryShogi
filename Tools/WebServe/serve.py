# -*- coding: utf-8 -*-
"""Web（WebGL）ビルドを、このPCのブラウザで確かめるための配信。外部には公開しない（127.0.0.1 だけ）。
  python -I Tools/WebServe/serve.py [port] [build] [prefix]   既定のポートは 8769
・bin/Web-<最新の版>/ を配信する。[build] に bin の下のフォルダー名（例：Web-1.6.0）を渡すと、そのビルドを配信する。
・Unity の Gzip 圧縮ファイル（.gz）に Content-Encoding を付ける。
・同じビルドを /a/ と /b/ の下でも配信する（ページのURLが変わっても保存が残るかを確かめる用。
  itch.io は更新のたびにURLのパスが変わる）。
・--lan を付けると、同じLAN内の他の端末（スマートフォンなど）からも開ける（0.0.0.0 で待ち受ける）。
  確かめ終わったら止める。付けなければこのPCからしか開けない。
"""
import datetime, glob, http.server, os, socketserver, sys

LAN = '--lan' in sys.argv
sys.argv = [x for x in sys.argv if x != '--lan']
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))

def latest_build():
    def key(p):
        try: return tuple(int(x) for x in os.path.basename(p)[4:].split('.'))
        except ValueError: return (0,)
    dirs = [d for d in glob.glob(os.path.join(ROOT, 'bin', 'Web-*')) if os.path.isfile(os.path.join(d, 'index.html'))]
    if not dirs: sys.exit('bin/Web-* がありません。先に Build-Web.ps1 でビルドしてください。')
    return max(dirs, key=key)

def chosen_build():
    if len(sys.argv) > 2:
        d = os.path.join(ROOT, 'bin', sys.argv[2])
        if not os.path.isfile(os.path.join(d, 'index.html')): sys.exit(d + ' に index.html がありません。')
        return os.path.normpath(d)
    return latest_build()

BUILD = chosen_build()
TYPES = {'.js': 'application/javascript', '.wasm': 'application/wasm', '.data': 'application/octet-stream',
         '.json': 'application/json', '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.png': 'image/png', '.ico': 'image/x-icon'}

class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def log_message(self, fmt, *args):
        sys.stderr.write('%s %s\n' % (datetime.datetime.now().strftime('%H:%M:%S'), fmt % args))

    def send_bytes(self, code, body, ctype, extra=None):
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        for k, v in (extra or {}).items(): self.send_header(k, v)
        self.end_headers()
        if self.command != 'HEAD': self.wfile.write(body)

    def do_GET(self):
        path = self.path.split('?', 1)[0]
        for prefix in ('/a/', '/b/'):
            if path.startswith(prefix): path = path[len(prefix) - 1:]
        if path == '/': path = '/index.html'
        full = os.path.normpath(os.path.join(BUILD, path.lstrip('/')))
        if not full.startswith(BUILD) or not os.path.isfile(full):
            return self.send_bytes(404, b'not found', 'text/plain')
        extra = {}
        name = full
        if name.endswith('.gz'): extra['Content-Encoding'] = 'gzip'; name = name[:-3]
        elif name.endswith('.br'): extra['Content-Encoding'] = 'br'; name = name[:-3]
        ctype = TYPES.get(os.path.splitext(name)[1].lower(), 'application/octet-stream')
        with open(full, 'rb') as fh: body = fh.read()
        self.send_bytes(200, body, ctype, extra)

    do_HEAD = do_GET

class Server(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True

if __name__ == '__main__':
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8769
    print('build: ' + BUILD)
    print('http://localhost:%d/' % port)
    sys.stdout.flush()
    if LAN:
        import socket
        try:
            probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); probe.connect(('192.0.2.1', 9)); print('LAN: http://%s:%d/' % (probe.getsockname()[0], port)); probe.close()
        except OSError: pass
        sys.stdout.flush()
    Server(('0.0.0.0' if LAN else '127.0.0.1', port), Handler).serve_forever()
