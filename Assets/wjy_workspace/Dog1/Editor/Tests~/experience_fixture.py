"""Credential-free, loopback-only responses for the player-flow regression."""
import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def model_memory(request):
    instructions = request["messages"][0]["content"]
    return json.loads(instructions.split("\nDog memory: ", 1)[1])


def choose(context, action_id, target="none"):
    entry = next(item for item in context["state"]["actionCatalog"]
                 if item["id"] == action_id and item["target"] == target)
    if not entry["available"]:
        raise ValueError("Requested experience action is unavailable: " + entry["reason"])
    return {"id": action_id, "target": target}


def answer(mode, request):
    context = json.loads(request["messages"][-1]["content"])
    if mode == "memory":
        memory = model_memory(request)
        recent = memory.get("recentEvents", [])
        if len(recent) > 8 or any(not isinstance(note, str) or len(note) > 100 for note in recent):
            raise ValueError("Recent memory is unbounded")
        sentence = context["utterance"]
        if "暗号是什么" in sentence:
            if not any("暗号是小云朵" in note for note in recent):
                raise ValueError("The preceding player utterance was not sent to the model")
            if not any("记住小云朵" in note for note in recent):
                raise ValueError("The preceding model reply was not sent to the model")
            reply = "你刚才说的暗号是小云朵。"
        elif "暗号是小云朵" in sentence:
            reply = "好呀，我记住小云朵了。"
        else:
            reply = "我听见你了。"
        return dict(reply=reply, decision="reply", actions=[])
    if mode in ("teach-free", "sad-recall"):
        if mode == "sad-recall":
            memory = model_memory(request)
            if memory.get("sadnessStrength", 0) <= 0 or memory.get("sadnessTarget") != "lollipop":
                raise ValueError("Saved conditional memory did not reach the semantic recall request")
        return dict(reply="我会试着记住你的约定。" if mode == "teach-free" else "我记得你的约定，去拿毛绒玩具陪你。",
                    decision="act", actions=[choose(context, "fetch_lollipop")],
                    learningCondition="owner_sad" if mode == "teach-free" else "none")
    action_id, target = {
        "rest": ("rest", "none"),
        "crouch": ("crouch", "none"),
        "house": ("rest", "house"),
        "cushion": ("rest", "cushion"),
        "invite": ("invite", "none"),
        "fetch": ("fetch_toy", "none"),
        "sit": ("sit", "none"),
        "headset-sit": ("sit", "none"),
    }[mode]
    return dict(reply="我按你的意思试试看。", decision="act",
                actions=[choose(context, action_id, target)])


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        return

    def do_POST(self):
        try:
            if self.headers.get("Authorization"):
                raise ValueError("Experience fixtures must not receive credentials")
            size = int(self.headers.get("Content-Length", 0))
            if not 0 < size < 65536:
                raise ValueError("Unexpected request size")
            request = json.loads(self.rfile.read(size))
            response = answer(self.path.strip("/"), request)
            body = json.dumps({"choices": [{"message": {"content": json.dumps(response, ensure_ascii=False)}}]},
                              ensure_ascii=False).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except (ValueError, KeyError, TypeError, IndexError, StopIteration) as error:
            print("Experience fixture rejected request: " + str(error), file=sys.stderr, flush=True)
            self.send_error(400, "Experience fixture rejected request")


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
        Path(sys.argv[1]).write_text(str(server.server_port), encoding="utf-8")
        server.serve_forever()
