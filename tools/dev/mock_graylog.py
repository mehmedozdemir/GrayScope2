"""Minimal Graylog 5.x API mock for exercising GrayScope 2 locally (no real servers touched)."""
import csv, io, json, random, re, sys, uuid
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

STREAMS = [{"id": "s-app", "title": "Uygulama"}, {"id": "s-gw", "title": "API Gateway"}, {"id": "s-sys", "title": "Sistem"}]
FIELDS = ["timestamp", "source", "message", "NetworkId", "StatusCode", "Path", "DurationMs", "level"]
SOURCES = ["web-01", "web-02", "api-01"]
PATHS = ["/api/balance", "/api/topup", "/api/card", "/health"]
STORE = {}


def make_message(network_id, now):
    mid = uuid.uuid4().hex[:24]
    status = random.choice([200, 200, 200, 404, 500, 502])
    msg = {
        "_id": mid,
        "timestamp": (now - timedelta(seconds=random.randint(0, 3600))).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z",
        "source": random.choice(SOURCES),
        "NetworkId": str(network_id),
        "StatusCode": str(status),
        "Path": random.choice(PATHS),
        "DurationMs": str(random.randint(5, 2500)),
        "level": "3" if status >= 500 else "6",
        "message": f"HTTP {status} {random.choice(PATHS)}" + (' {"error":"upstream timeout","retry":true}' if status >= 500 else ""),
        "gl2_source_index": "graylog_42",
    }
    STORE[mid] = msg
    return msg


class Handler(BaseHTTPRequestHandler):
    def _send(self, code, body, ctype="application/json"):
        data = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _auth_ok(self):
        return self.headers.get("Authorization", "").startswith("Basic ")

    def do_GET(self):
        if not self._auth_ok():
            return self._send(401, "")
        path = self.path.split("?")[0]
        if path == "/api/system":
            return self._send(200, json.dumps({"version": "5.1.2+mock", "cluster_id": "mock", "hostname": "localhost"}))
        if path == "/api/streams":
            return self._send(200, json.dumps({"total": len(STREAMS), "streams": STREAMS}))
        if path == "/api/system/fields":
            return self._send(200, json.dumps({"fields": FIELDS + ["gl2_source_index", "_id"]}))
        m = re.match(r"/api/messages/([^/]+)/([^/]+)$", path)
        if m:
            msg = STORE.get(m.group(2))
            if not msg:
                return self._send(404, json.dumps({"message": "not found"}))
            full = dict(msg, full_message=msg["message"] + "\nstack: at Foo.Bar()", gl2_remote_ip="10.0.0.5")
            return self._send(200, json.dumps({"index": m.group(1), "message": full}))
        return self._send(404, json.dumps({"message": "no route"}))

    def do_POST(self):
        if not self._auth_ok():
            return self._send(401, "")
        if self.path != "/api/search/messages":
            return self._send(404, "")
        body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
        query = body.get("query", "")
        if "((" in query:
            return self._send(400, json.dumps({"type": "ApiError", "message": f"Cannot parse '{query}'"}))
        ids = re.findall(r"NetworkId:(\d+)", query) or ["34"]
        size = min(body.get("size") or 300, 300)
        now = datetime.now(timezone.utc)
        rows = [make_message(random.choice(ids), now) for _ in range(random.randint(size // 2, size))]
        if "StatusCode:500" in query:
            rows = [r for r in rows if r["StatusCode"] == "500"]
        fields = body.get("fields") or ["timestamp", "source", "message"]
        out = io.StringIO()
        w = csv.writer(out)
        w.writerow([f"field: {f}" for f in fields])
        for r in rows:
            w.writerow(["-" if f == "gl2_source_index" else r.get(f, "") for f in fields])
        return self._send(200, out.getvalue(), "text/csv")

    def log_message(self, fmt, *args):
        sys.stderr.write("mock: " + (fmt % args) + "\n")


if __name__ == "__main__":
    ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1]) if len(sys.argv) > 1 else 9123), Handler).serve_forever()
