"""Credential-free loopback controller fixture for the integrated Dog1 scene.

Routes select explicit catalogue entries; this fixture does not emulate semantic AI.
Use action/<id>/<target> or plan/<id>:<target>,<id>:<target> for controlled plans.
"""
import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit


def option(context, action_id, target="none"):
    catalogue = context["state"]["actionCatalog"]
    entry = next(item for item in catalogue if item["id"] == action_id and item["target"] == target)
    if not entry["available"]:
        raise ValueError("Integration action is unavailable: " + str(entry.get("reason", "")))
    return {"id": action_id, "target": target}


def answer(path, request):
    context = json.loads(request["messages"][-1]["content"])
    route = unquote(urlsplit(path).path).strip("/")
    if route == "reply":
        return dict(reply="我听见你了，先在这里陪你。", decision="reply", actions=[])
    if route == "invalid":
        return dict(reply="这次保持原位。", decision="act", actions=[dict(id="not_registered", target="none")])
    if route in ("praise", "insincere"):
        return dict(reply="听见你的表扬啦。" if route == "praise" else "我继续听你说。",
                    decision="reply", actions=[], training=dict(mode="praise", sincere=route == "praise"))
    if route.startswith("practice/"):
        skill = route.split("/", 1)[1]
        if skill not in ("sit", "crouch", "paw", "spin", "come", "fetch_toy"):
            raise ValueError("Unsupported practice skill")
        return dict(reply="我试着练习这个动作。", decision="act", actions=[option(context, skill)],
                    training=dict(mode="practice", alternative=option(context, "refuse"),
                                  failureReply="这次我还没跟上，歪头看看你，再练一次吧。"))
    if route.startswith("action/"):
        parts = route.split("/")
        if len(parts) != 3:
            raise ValueError("Expected action/<id>/<target>")
        actions = [option(context, parts[1], parts[2])]
    elif route.startswith("plan/"):
        plan = route[len("plan/"):].split(",")
        if not 1 <= len(plan) <= 3:
            raise ValueError("Expected one to three plan steps")
        actions = []
        for step in plan:
            fields = step.split(":")
            if len(fields) != 2:
                raise ValueError("Expected id:target")
            actions.append(option(context, fields[0], fields[1]))
    elif route == "teach-plush":
        return dict(reply="我练习把毛绒玩具叼回来，做对了再夸我。", decision="act",
                    actions=[option(context, "fetch_lollipop")], learningCondition="owner_sad")
    else:
        raise ValueError("Unknown integration fixture route")
    return dict(reply="我按你的意思试试看。", decision="act", actions=actions)


class Handler(BaseHTTPRequestHandler):
    count_lock = threading.Lock()
    counts = {}

    def log_message(self, *_args):
        return

    def do_GET(self):
        if self.path != "/stats":
            self.send_error(404)
            return
        with self.count_lock:
            self.send_json(dict(requests=dict(self.counts)))

    def do_POST(self):
        try:
            if self.headers.get("Authorization"):
                raise ValueError("Integration fixtures must never receive personal credentials")
            size = int(self.headers.get("Content-Length", 0))
            if not 0 < size < 65536:
                raise ValueError("Unexpected request size")
            request = json.loads(self.rfile.read(size))
            response = answer(self.path, request)
            with self.count_lock:
                self.counts[self.path] = self.counts.get(self.path, 0) + 1
            self.send_json({"choices": [{"message": {"content": json.dumps(response, ensure_ascii=False)}}]})
        except (ValueError, KeyError, TypeError, IndexError, StopIteration) as error:
            print("Integration fixture rejected request: " + str(error), file=sys.stderr, flush=True)
            self.send_error(400, "Integration fixture rejected request")

    def send_json(self, value):
        body = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
        Path(sys.argv[1]).write_text(str(server.server_port), encoding="utf-8")
        server.serve_forever()
