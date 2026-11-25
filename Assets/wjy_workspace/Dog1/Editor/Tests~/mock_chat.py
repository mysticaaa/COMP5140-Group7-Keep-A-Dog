"""Return controlled chat completions on loopback for Dog1 conversation QA."""

import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class ChatFixture(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        return

    def do_POST(self):
        mode = self.path.strip("/")
        if mode == "unavailable":
            self.send_error(503)
            return
        if mode not in {"allowed", "disallowed", "invite"}:
            self.send_error(404)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size <= 65536 or self.headers.get("Authorization"):
                raise ValueError("Unexpected request length or credentials")
            request = json.loads(self.rfile.read(size))
            context = json.loads(request["messages"][-1]["content"])
            if mode == "invite":
                if "还是安静" not in context["playerSentence"] or "quiet" not in context["candidates"]:
                    raise ValueError("Unexpected invitation context")
                choice = {"activity": "quiet", "reply": "我想和你安静待一会儿。"}
            else:
                if context["playerSentence"] != "看看我":
                    raise ValueError("Unexpected scenario text")
                candidate = next(
                    entry for entry in context["observation"]["candidates"]
                    if entry["id"] == "air_sniff" and entry["tags"]
                )
                choice = {
                    "actionId": candidate["id"],
                    "targetId": candidate["targetId"] if mode == "allowed" else "outside-test-registry",
                    "reply": "我停下来闻一闻。",
                }
        except (ValueError, TypeError, KeyError, IndexError, StopIteration):
            self.send_error(400)
            return

        body = json.dumps({"choices": [{"message": {"content": json.dumps(choice)}}]}).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 0), ChatFixture) as server:
        Path(sys.argv[1]).write_text(str(server.server_port), encoding="utf-8")
        server.serve_forever()
