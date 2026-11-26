"""Return controlled chat completions on loopback for Dog1 conversation QA."""

import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


COMMAND_REPLIES = {
    "command-single": ("坐下", {"status": "ready", "kind": "command", "action": "sit"}),
    "command-sequence": (
        "按我的提示做一组动作",
        {"status": "ready", "kind": "sequence", "steps": ["sit", "paw", "bow"]},
    ),
    "command-invalid": (
        "按我的提示做一组新动作",
        {"status": "ready", "kind": "sequence", "steps": ["sit", "teleport", "paw"]},
    ),
    "command-state-change": (
        "换一组动作演示一下",
        {"status": "ready", "kind": "sequence", "steps": ["sit", "pounce", "paw"]},
    ),
    "command-blocked": (
        "轻扑一下",
        {"status": "blocked", "kind": "chat", "reply": "我前面有障碍，先等等。"},
    ),
}


def command_reply(mode, context):
    utterance, reply = COMMAND_REPLIES[mode]
    state = context["state"]
    booleans = ("dogReady", "ownerAvailable", "ballAvailable", "lollipopAvailable", "pounceClear")
    if (
        context["utterance"] != utterance
        or not state["location"]
        or any(type(state[field]) is not bool for field in booleans)
        or any(not isinstance(state[field], (int, float)) for field in ("energy", "hydration"))
        or not state["dogReady"]
        or not state["ownerAvailable"]
    ):
        raise ValueError("Unexpected action state or utterance")
    if mode in {"command-state-change", "command-blocked"} and state["pounceClear"] != (mode == "command-state-change"):
        raise ValueError("Unexpected pounce clearance")
    if state.get("actionCatalog"):
        if mode == "command-single":
            return {"decision": "act", "reply": "好，我先坐下。", "actions": [{"id": "sit", "target": "none"}]}
        if mode == "command-blocked":
            return {"decision": "decline", "reply": "我前面有障碍，先等等。", "reason": "blocked", "actions": []}
        steps = reply["steps"]
        line = "我听见你了。" if mode == "command-invalid" else "好，我按顺序试试。"
        return {"decision": "act", "reply": line,
                "actions": [{"id": step, "target": "none"} for step in steps]}
    return {"target": "none", "action": "none", "reply": "", **reply}


class ChatFixture(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        return

    def do_POST(self):
        mode = self.path.strip("/")
        if mode == "unavailable":
            self.send_error(503)
            return
        if mode not in {"allowed", "disallowed", "invite"} and mode not in COMMAND_REPLIES:
            self.send_error(404)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size <= 65536 or self.headers.get("Authorization"):
                raise ValueError("Unexpected request length or credentials")
            request = json.loads(self.rfile.read(size))
            context = json.loads(request["messages"][-1]["content"])
            if mode in COMMAND_REPLIES:
                if request["response_format"]["type"] != "json_object":
                    raise ValueError("Command request did not ask for JSON")
                choice = command_reply(mode, context)
            elif mode == "invite":
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
