"""Local, credential-free HTTP failures for Unity authentication regression."""
import json
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        return

    def do_POST(self):
        size = int(self.headers.get("Content-Length", 0))
        if self.headers.get("Authorization") or not 0 < size < 65536:
            self.send_error(400, "Fixture requires no credentials")
            return
        try:
            request = json.loads(self.rfile.read(size))
            context = json.loads(request["messages"][-1]["content"])
            if not context["state"]["actionCatalog"]:
                raise ValueError("Missing controller state")
        except (ValueError, KeyError, IndexError, TypeError):
            self.send_error(400, "Invalid controller request")
            return

        route = self.path.strip("/")
        if route.startswith("http-"):
            status = int(route[5:])
            body = b'{"error":{"message":"MUST_NOT_LEAK_RESPONSE_BODY"}}'
        elif route == "timeout":
            time.sleep(14)
            status, body = 200, b'{}'
        else:
            status = 200
            answer = dict(reply="我在这里陪着你。", decision="reply", actions=[])
            if route == "act":
                answer = dict(reply="好，我就在这里休息。", decision="act",
                              actions=[dict(id="rest", target="none")])
            body = json.dumps({"choices": [{"message": {"content": json.dumps(answer, ensure_ascii=False)}}]},
                              ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
        Path(sys.argv[1]).write_text(str(server.server_port), encoding="utf-8")
        server.serve_forever()
