"""Credential-free loopback fixtures for Dog1 controller regression."""
import json
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def action(catalog, name, target="none"):
    entry = next(item for item in catalog if item["id"] == name and item["target"] == target)
    if not entry["available"]:
        raise ValueError(f"Expected {name}/{target} to be available: {entry['reason']}")
    return {"id": name, "target": target}


def response(mode, context):
    state = context["state"]
    catalog = state["actionCatalog"]
    if len(catalog) < 20:
        raise ValueError("Controller did not receive the complete action catalogue")
    if not any(entry["id"] == "body_shake" for entry in catalog):
        raise ValueError("Micro actions missing from catalogue")
    if mode == "controller-rest":
        return dict(reply="我就在这里歇一会儿。", decision="act", actions=[action(catalog, "rest")])
    if mode == "controller-queue-delayed":
        time.sleep(0.8)
        return dict(reply="收到延迟的休息回应。", decision="act", actions=[action(catalog, "rest")])
    if mode == "controller-fuzzy":
        return dict(reply="我抬起鼻子闻一闻。", decision="act", actions=[action(catalog, "air_sniff")])
    if mode == "controller-mixed":
        return dict(reply="先闻闻，再歇下来。", decision="act",
                    actions=[action(catalog, "air_sniff"), action(catalog, "rest")])
    if mode == "controller-invalid":
        return dict(reply="我听见你了。", decision="act", actions=[action(catalog, "rest"),
                    {"id": "teleport", "target": "none"}])
    if mode in ("controller-hungry", "controller-tired"):
        effort = next(item for item in catalog if item["id"] == "fetch_toy" and item["target"] == "none")
        if effort["available"] or not effort["reason"]:
            raise ValueError("Needs did not block fetch with a reason")
        action(catalog, "rest")
        line = "我肚子饿了，吃点东西再玩。" if mode.endswith("hungry") else "我累了，想先歇一下。"
        return dict(reply=line, decision="decline", reason=effort["reason"], actions=[])
    if mode == "controller-stale":
        return dict(reply="我先闻一下。", decision="act",
                    actions=[action(catalog, "air_sniff"), action(catalog, "pounce")])
    if mode == "controller-unsupported":
        return dict(reply="这个碗我现在叼不了。", decision="reply", reason="unsupported object", actions=[])
    raise ValueError("Unknown controller test mode")


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        return

    def do_POST(self):
        if self.path.strip("/") == "unavailable":
            self.send_error(503)
            return
        try:
            if self.headers.get("Authorization"):
                raise ValueError("Fixture must never receive credentials")
            size = int(self.headers.get("Content-Length", 0))
            if not 0 < size < 65536:
                raise ValueError("Unexpected request size")
            request = json.loads(self.rfile.read(size))
            context = json.loads(request["messages"][-1]["content"])
            answer = response(self.path.strip("/"), context)
        except (ValueError, TypeError, KeyError, IndexError, StopIteration) as error:
            print(f"Fixture rejected {self.path}: {error}", file=sys.stderr, flush=True)
            self.send_error(400, str(error))
            return
        body = json.dumps({"choices": [{"message": {"content": json.dumps(answer, ensure_ascii=False)}}]},
                          ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
        Path(sys.argv[1]).write_text(str(server.server_port), encoding="utf-8")
        server.serve_forever()
