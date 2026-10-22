#!/usr/bin/env python3
"""Local MCP bridge from Codex to OpenCode's headless HTTP server.

The bridge is intentionally stdlib-only. It starts one isolated OpenCode server
per job, creates one session per job, and keeps that session for corrections.
It speaks newline-delimited JSON-RPC on stdin/stdout for Codex MCP.
"""

from __future__ import annotations

import json
import os
import base64
import secrets
import shutil
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass, field
from typing import Any


DEFAULT_MODEL_LABEL = "DeepSeek V4.1 Flash Free"
DEFAULT_REASONING = "high"
DEFAULT_OPENCODE_COMMAND = os.environ.get("OPENCODE_COMMAND") or shutil.which("opencode") or shutil.which("opencode.cmd") or os.path.join(os.environ.get("APPDATA", ""), "npm", "opencode.cmd") or "opencode"
DEFAULT_START_TIMEOUT_SECONDS = 30
DEFAULT_POLL_SECONDS = 2
MODEL_IDS = {
    "glm 5.3 flash free": ("opencode", "glm-5.3-flash-free"),
    "glm-5.3-flash free": ("opencode", "glm-5.3-flash-free"),
    "big pickle": ("opencode", "big-pickle"),
    "big-pickle": ("opencode", "big-pickle"),
    "deepseek v4.1 flash free": ("opencode", "deepseek-v4.1-flash-free"),
    "deepseek-v4.1-flash-free": ("opencode", "deepseek-v4.1-flash-free"),
}


def _json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def _free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def _request(base_url: str, method: str, path: str, body: Any | None = None, password: str | None = None) -> Any:
    data = None if body is None else _json(body).encode("utf-8")
    headers = {"Content-Type": "application/json", "Accept": "application/json"}
    if password:
        token = base64.b64encode(f"opencode:{password}".encode("utf-8")).decode("ascii")
        headers["Authorization"] = f"Basic {token}"
    request = urllib.request.Request(
        f"{base_url}{path}",
        data=data,
        method=method,
        headers=headers,
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read()
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"OpenCode HTTP {error.code} for {method} {path}: {detail}") from error
    if not raw:
        return None
    return json.loads(raw.decode("utf-8"))


def _text_from_parts(parts: Any) -> str:
    if not isinstance(parts, list):
        return ""
    chunks: list[str] = []
    for part in parts:
        if isinstance(part, dict) and part.get("type") == "text":
            text = part.get("text")
            if isinstance(text, str):
                chunks.append(text)
    return "\n".join(chunks).strip()


@dataclass
class Job:
    job_id: str
    workspace_directory: str
    phase: str
    model_label: str
    reasoning: str
    port: int
    base_url: str
    process: subprocess.Popen[Any]
    attach_process: subprocess.Popen[Any] | None
    session_id: str
    password: str
    started_at: float = field(default_factory=time.time)
    prompt_started_at: float = field(default_factory=time.time)
    state: str = "running"
    last_report: str = ""
    last_message_count: int = 0
    prompt_message_floor: int = 0
    lock: threading.Lock = field(default_factory=threading.Lock)


JOBS: dict[str, Job] = {}
JOBS_LOCK = threading.Lock()


def _start_server(workspace: str) -> tuple[subprocess.Popen[Any], int, str, str]:
    command = [
        DEFAULT_OPENCODE_COMMAND,
        "serve",
        "--hostname",
        "127.0.0.1",
        "--port",
        str(_free_port()),
    ]
    port = int(command[-1])
    password = secrets.token_urlsafe(32)
    env = os.environ.copy()
    env["OPENCODE_SERVER_PASSWORD"] = password
    process = subprocess.Popen(
        command,
        cwd=workspace,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        env=env,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    base_url = f"http://127.0.0.1:{port}"
    deadline = time.monotonic() + DEFAULT_START_TIMEOUT_SECONDS
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(
                f"OpenCode server exited with code {process.returncode}. "
                "Verify that OpenCode is installed and its model/provider is configured."
            )
        try:
            health = _request(base_url, "GET", "/api/info", password=password)
            if isinstance(health, dict):
                return process, port, base_url, password
        except (OSError, urllib.error.URLError, TimeoutError, json.JSONDecodeError) as error:
            last_error = error
        time.sleep(0.25)
    process.terminate()
    detail = f" Last error: {last_error}" if last_error else ""
    raise RuntimeError(f"Timed out waiting for OpenCode server.{detail}")


def _prompt_text(job: Job, task: str, allowed_files: list[str], base_revision: str) -> str:
    files = "\n".join(f"- {item}" for item in allowed_files) or "- No file manifest supplied"
    return f"""You are an OpenCode implementation worker for the Sanad API repository.

Phase: {job.phase}
Requested model label: {job.model_label}
Requested reasoning: {job.reasoning}
Pinned base revision: {base_revision or 'not supplied'}

Task:
{task}

Owned files (do not edit outside this manifest):
{files}

Execution rules:
- Read and follow the repository AGENTS.md and the supplied phase brief.
- Do not commit, push, merge, deploy, reset databases, or change external systems.
- Stop on ambiguity or an out-of-manifest dependency and report it.
- At the end, return a concise report with: status, changed files, implementation summary,
  validation commands/results, assumptions, blockers, and unfulfilled acceptance checks.
- The first line of the final report must be exactly `FINAL_REPORT_STATUS: COMPLETE` or
  `FINAL_REPORT_STATUS: BLOCKED`. Never use either marker in interim progress messages.
"""


def _open_visible_session(job: Job) -> tuple[bool, str]:
    """Open a read-only-by-convention TUI view attached to this job's server."""
    if os.name != "nt":
        return False, "Automatic visible terminal attachment is currently supported on Windows only."

    env = os.environ.copy()
    env["OPENCODE_SERVER_USERNAME"] = "opencode"
    env["OPENCODE_SERVER_PASSWORD"] = job.password
    command = [DEFAULT_OPENCODE_COMMAND, "attach", job.base_url, "--session", job.session_id]
    creationflags = getattr(subprocess, "CREATE_NEW_CONSOLE", 0)
    try:
        if DEFAULT_OPENCODE_COMMAND.lower().endswith((".cmd", ".bat")):
            command_line = subprocess.list2cmdline(command)
            job.attach_process = subprocess.Popen(
                [os.environ.get("COMSPEC", "cmd.exe"), "/d", "/k", command_line],
                cwd=job.workspace_directory,
                env=env,
                creationflags=creationflags,
            )
        else:
            job.attach_process = subprocess.Popen(
                command,
                cwd=job.workspace_directory,
                env=env,
                creationflags=creationflags,
            )
        return True, "An OpenCode terminal is attached to this session for observation."
    except OSError as error:
        return False, f"Could not open the visible OpenCode terminal: {error}"


def _create_job(arguments: dict[str, Any]) -> dict[str, Any]:
    workspace = os.path.abspath(str(arguments.get("workspace_directory", os.getcwd())))
    if not os.path.isdir(workspace):
        raise ValueError(f"Workspace directory does not exist: {workspace}")
    phase = str(arguments.get("phase", "unnamed-phase"))
    task = str(arguments.get("task", ""))
    if not task.strip():
        raise ValueError("task is required")
    allowed_files = [str(item) for item in arguments.get("allowed_files", [])]
    model_label = str(arguments.get("model_label", DEFAULT_MODEL_LABEL))
    reasoning = str(arguments.get("reasoning", DEFAULT_REASONING))
    base_revision = str(arguments.get("base_revision", ""))

    process, port, base_url, password = _start_server(workspace)
    try:
        title = str(arguments.get("title", f"Sanad {phase}"))
        session_body: dict[str, Any] = {"title": title, "location": {"directory": workspace}}
        provider_id = arguments.get("provider_id")
        model_id = arguments.get("model_id")
        if not provider_id or not model_id:
            provider_id, model_id = MODEL_IDS.get(model_label.strip().lower(), (None, None))
        if provider_id and model_id:
            session_body["model"] = {
                "providerID": str(provider_id),
                "id": str(model_id),
                "variant": reasoning,
            }
        session = _request(base_url, "POST", "/api/session", session_body, password=password)
        session_data = session.get("data") if isinstance(session, dict) else None
        session_id = str(session_data.get("id")) if isinstance(session_data, dict) else ""
        if not session_id:
            raise RuntimeError("OpenCode did not return a session id")
        job = Job(
            job_id=f"oc-{uuid.uuid4().hex[:12]}",
            workspace_directory=workspace,
            phase=phase,
            model_label=model_label,
            reasoning=reasoning,
            port=port,
            base_url=base_url,
            process=process,
            attach_process=None,
            session_id=session_id,
            password=password,
        )
        with JOBS_LOCK:
            JOBS[job.job_id] = job
        _send_prompt(job, _prompt_text(job, task, allowed_files, base_revision))
        visible, visibility_message = _open_visible_session(job)
        return {
            "job_id": job.job_id,
            "session_id": session_id,
            "state": "running",
            "workspace_directory": workspace,
            "phase": phase,
            "model_label": model_label,
            "reasoning": reasoning,
            "visible_terminal_opened": visible,
            "message": f"OpenCode is running. {visibility_message} Use opencode_wait_task or opencode_get_report; do not start duplicate work.",
        }
    except Exception:
        process.terminate()
        raise


def _find_job(job_id: str) -> Job:
    with JOBS_LOCK:
        job = JOBS.get(job_id)
    if not job:
        raise ValueError(f"Unknown job_id: {job_id}")
    return job


def _load_messages(job: Job) -> list[Any]:
    messages_payload = _request(job.base_url, "GET", f"/api/session/{job.session_id}/message", password=job.password)
    messages = messages_payload.get("data") if isinstance(messages_payload, dict) else messages_payload
    return messages if isinstance(messages, list) else []


def _load_latest_report(job: Job) -> tuple[str, str]:
    messages = _load_messages(job)
    for item in reversed(messages[job.prompt_message_floor:]):
        if not isinstance(item, dict):
            continue
        info = item.get("info") or item
        if info.get("role") == "assistant" or info.get("type") == "assistant":
            text = _text_from_parts(item.get("parts") or item.get("content"))
            lines = [line.strip() for line in text.splitlines() if line.strip()]
            if lines and lines[0] in {"FINAL_REPORT_STATUS: COMPLETE", "FINAL_REPORT_STATUS: BLOCKED"}:
                return lines[0].rsplit(" ", 1)[-1].lower(), text
    return "", ""


def _send_prompt(job: Job, text: str) -> None:
    # Snapshot before sending this prompt so earlier reports in the same session
    # cannot satisfy the new request.
    job.prompt_message_floor = len(_load_messages(job))

    def run() -> None:
        try:
            _request(
                job.base_url,
                "POST",
                f"/api/session/{job.session_id}/prompt",
                {"id": None, "text": text, "delivery": "steer", "resume": True},
                password=job.password,
            )
            deadline = time.monotonic() + 86400
            status, report = "", ""
            while time.monotonic() < deadline:
                status, report = _load_latest_report(job)
                if status:
                    break
                if job.process.poll() is not None:
                    raise RuntimeError("OpenCode exited before returning a report")
                time.sleep(DEFAULT_POLL_SECONDS)
            if not status:
                with job.lock:
                    job.state = "unknown"
                    job.last_report = "No terminal report marker arrived within 24 hours. The same OpenCode session may still be active; verify it before any new dispatch."
                return
            with job.lock:
                job.last_report = report
                job.state = "blocked" if status == "blocked" else "completed"
        except Exception as error:
            with job.lock:
                job.state = "failed" if job.process.poll() is not None else "unknown"
                job.last_report = str(error)
    threading.Thread(target=run, name=f"opencode-{job.job_id}", daemon=True).start()


def _refresh(job: Job) -> dict[str, Any]:
    with job.lock:
        return {
            "job_id": job.job_id,
            "session_id": job.session_id,
            "state": job.state,
            "phase": job.phase,
            "report": job.last_report,
            "server_alive": job.process.poll() is None,
            "visible_terminal_alive": job.attach_process is not None and job.attach_process.poll() is None,
        }


def _wait_job(arguments: dict[str, Any]) -> dict[str, Any]:
    job = _find_job(str(arguments.get("job_id", "")))
    timeout = max(1, min(int(arguments.get("timeout_seconds", 3600)), 86400))
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = _refresh(job)
        if result["state"] in {"completed", "blocked", "failed", "cancelled"}:
            return result
        time.sleep(DEFAULT_POLL_SECONDS)
    result = _refresh(job)
    result["timed_out"] = True
    result["message"] = "Still running; keep the same job_id and wait again. No duplicate task was started."
    return result


def _follow_up(arguments: dict[str, Any]) -> dict[str, Any]:
    job = _find_job(str(arguments.get("job_id", "")))
    correction = str(arguments.get("correction", ""))
    if not correction.strip():
        raise ValueError("correction is required")
    with job.lock:
        job.state = "running"
        job.prompt_started_at = time.time()
        job.last_report = ""
        job.prompt_message_floor = len(_load_messages(job))
        _send_prompt(job, f"Mastermind correction for the same phase:\n\n{correction}")
    return {"job_id": job.job_id, "session_id": job.session_id, "state": "running", "message": "Correction sent to the same OpenCode session."}


def _cancel(arguments: dict[str, Any]) -> dict[str, Any]:
    job = _find_job(str(arguments.get("job_id", "")))
    with job.lock:
        try:
            _request(job.base_url, "POST", f"/api/session/{job.session_id}/interrupt", {"resume": False}, password=job.password)
        finally:
            job.process.terminate()
            job.state = "cancelled"
    return {"job_id": job.job_id, "session_id": job.session_id, "state": "cancelled"}


TOOLS = [
    {
        "name": "opencode_start_task",
        "description": "Start one bounded OpenCode worker task and return a resumable job_id. Supported labels: GLM-5.3-Flash Free, Big Pickle, DeepSeek V4.1 Flash Free. The call returns immediately.",
        "inputSchema": {"type": "object", "properties": {
            "workspace_directory": {"type": "string"}, "phase": {"type": "string"}, "task": {"type": "string"},
            "allowed_files": {"type": "array", "items": {"type": "string"}}, "base_revision": {"type": "string"},
            "title": {"type": "string"}, "model_label": {"type": "string"}, "reasoning": {"type": "string"},
            "provider_id": {"type": "string"}, "model_id": {"type": "string"},
        }, "required": ["workspace_directory", "phase", "task"]},
    },
    {"name": "opencode_wait_task", "description": "Wait without duplicate work until an OpenCode job reports completion or failure.", "inputSchema": {"type": "object", "properties": {"job_id": {"type": "string"}, "timeout_seconds": {"type": "integer"}}, "required": ["job_id"]}},
    {"name": "opencode_get_report", "description": "Get current status and the latest OpenCode report for a job.", "inputSchema": {"type": "object", "properties": {"job_id": {"type": "string"}}, "required": ["job_id"]}},
    {"name": "opencode_follow_up", "description": "Send a bounded correction to the same OpenCode session and keep its job identity.", "inputSchema": {"type": "object", "properties": {"job_id": {"type": "string"}, "correction": {"type": "string"}}, "required": ["job_id", "correction"]}},
    {"name": "opencode_cancel_task", "description": "Abort one OpenCode job and its server.", "inputSchema": {"type": "object", "properties": {"job_id": {"type": "string"}}, "required": ["job_id"]}},
]


def _dispatch(name: str, arguments: dict[str, Any]) -> Any:
    if name == "opencode_start_task":
        return _create_job(arguments)
    if name == "opencode_wait_task":
        return _wait_job(arguments)
    if name == "opencode_get_report":
        return _refresh(_find_job(str(arguments.get("job_id", ""))))
    if name == "opencode_follow_up":
        return _follow_up(arguments)
    if name == "opencode_cancel_task":
        return _cancel(arguments)
    raise ValueError(f"Unknown tool: {name}")


def _handle(request: dict[str, Any]) -> dict[str, Any] | None:
    request_id = request.get("id")
    method = request.get("method")
    if method == "notifications/initialized":
        return None
    if method == "initialize":
        return {"jsonrpc": "2.0", "id": request_id, "result": {"protocolVersion": "2024-11-05", "capabilities": {"tools": {}}, "serverInfo": {"name": "sanad-opencode-bridge", "version": "0.1.0"}}}
    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": request_id, "result": {"tools": TOOLS}}
    if method == "tools/call":
        try:
            params = request.get("params") or {}
            result = _dispatch(str(params.get("name")), params.get("arguments") or {})
            return {"jsonrpc": "2.0", "id": request_id, "result": {"content": [{"type": "text", "text": _json(result)}], "isError": False}}
        except Exception as error:
            return {"jsonrpc": "2.0", "id": request_id, "result": {"content": [{"type": "text", "text": str(error)}], "isError": True}}
    if request_id is not None:
        return {"jsonrpc": "2.0", "id": request_id, "error": {"code": -32601, "message": f"Method not found: {method}"}}
    return None


def main() -> None:
    for line in sys.stdin:
        if not line.strip():
            continue
        try:
            response = _handle(json.loads(line))
            if response is not None:
                sys.stdout.write(_json(response) + "\n")
                sys.stdout.flush()
        except Exception as error:
            sys.stdout.write(_json({"jsonrpc": "2.0", "id": None, "error": {"code": -32000, "message": str(error)}}) + "\n")
            sys.stdout.flush()


if __name__ == "__main__":
    main()
