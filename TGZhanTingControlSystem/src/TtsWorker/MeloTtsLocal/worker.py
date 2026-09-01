#!/usr/bin/env python3
"""Loopback-only MeloTTS Chinese worker for TG Exhibition Control Server."""

from __future__ import annotations

import argparse
import hashlib
import io
import ipaddress
import json
import os
import re
import sys
import threading
import time
import traceback
import uuid
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

PROVIDER_ID = "melo-local"
VOICE_ID = "zh-standard"
WORKER_VERSION = "1.0.0"
MELOTTS_VERSION = "0.1.2"
MELOTTS_COMMIT = "b633f243412169b999526e19eb6fcac0974b5d30"
MODEL_REVISION = "082ca057e44f1e52ec47e1622a30286019e8a3ef"
BERT_REVISION = "7cbf9a625e29989f6b9c6c2fa68234c304f7e38f"
EXPECTED_HASHES = {
    "acoustic/config.json": "d58b5acdab89ad2bbd65325affab309ae3cb964834b02f9a60587474e81c8bb9",
    "acoustic/checkpoint.pth": "a74e9eadffff065c75eb6dfa040efa72cad23e72cfea70d39190bc174fb97093",
    "bert/config.json": "fba5d4b0a351a43f6ccb7a6587301fd9f6876ca36aae62af762af67c8f18db1c",
    "bert/pytorch_model.bin": "2fec0e2a13cde5fa386fa00ba3e1bfea14b5d8fd8760f37f051799812a320e8d",
    "bert/vocab.txt": "87b44292b452f6c05afa49b2e488e7eedf79ea4f4c39db6f2f4b37764228ef3f",
}

CAPABILITIES = {
    "maxTextLength": 5000,
    "minRate": 0.75,
    "maxRate": 1.25,
    "minPitch": 0.0,
    "maxPitch": 0.0,
    "supportedMediaTypes": ["audio/wav"],
    "supportsRate": True,
    "supportsPitch": False,
    "supportsVolume": False,
    "defaultSampleRateHz": 44100,
    "defaultChannels": 1,
    "fixedSampleRateHz": 44100,
    "fixedChannels": 1,
}


class WorkerFailure(Exception):
    def __init__(self, code: str, message: str, transient: bool = False):
        super().__init__(message)
        self.code = code
        self.message = message
        self.transient = transient


class MeloEngine:
    def __init__(self, melotts_source: Path, acoustic_model: Path, bert_model: Path, nltk_data: Path):
        self._melotts_source = melotts_source.resolve()
        self._acoustic_model = acoustic_model.resolve()
        self._bert_model = bert_model.resolve()
        self._nltk_data = nltk_data.resolve()
        self._model: Any | None = None
        self._speaker_id: int | None = None
        self._sample_rate = 44100
        self._load_seconds = 0.0
        self._error_code: str | None = "worker_starting"
        self._error_message: str | None = "MeloTTS 本地模型正在加载。"
        self._synthesis_gate = threading.Lock()
        self._requests_gate = threading.Lock()
        self._request_cancellations: dict[str, threading.Event] = {}

    @property
    def available(self) -> bool:
        return self._model is not None and self._speaker_id is not None and self._error_code is None

    def initialize(self) -> None:
        started = time.perf_counter()
        try:
            self._validate_bundle()
            os.environ["HF_HUB_OFFLINE"] = "1"
            os.environ["TRANSFORMERS_OFFLINE"] = "1"
            os.environ["HF_DATASETS_OFFLINE"] = "1"
            os.environ["MELOTTS_CONFIG_PATH"] = str(self._acoustic_model / "config.json")
            os.environ["MELOTTS_CHECKPOINT_PATH"] = str(self._acoustic_model / "checkpoint.pth")
            os.environ["MELOTTS_BERT_PATH"] = str(self._bert_model)
            os.environ["NLTK_DATA"] = str(self._nltk_data)
            sys.path.insert(0, str(self._melotts_source))

            import torch
            torch.set_num_threads(max(1, min(6, os.cpu_count() or 1)))
            try:
                torch.set_num_interop_threads(1)
            except RuntimeError:
                pass
            from melo.api import TTS

            model = TTS(language="ZH", device="cpu")
            speakers = dict(model.hps.data.spk2id)
            if "ZH" not in speakers or len(speakers) != 1:
                raise WorkerFailure("voice_catalog_mismatch", "MeloTTS 中文模型音色目录与冻结版本不一致。")
            sample_rate = int(model.hps.data.sampling_rate)
            if sample_rate != 44100:
                raise WorkerFailure("sample_rate_mismatch", "MeloTTS 中文模型不是预期的 44.1 kHz 模型。")
            self._model = model
            self._speaker_id = int(speakers["ZH"])
            self._sample_rate = sample_rate
            self._load_seconds = time.perf_counter() - started
            self._error_code = None
            self._error_message = None
            print(json.dumps({"event": "worker_ready", "loadSeconds": self._load_seconds,
                              "sampleRateHz": self._sample_rate}, ensure_ascii=False), flush=True)
        except WorkerFailure as failure:
            self._set_unavailable(failure.code, failure.message)
        except Exception:
            self._set_unavailable("model_load_failed", "MeloTTS 本地模型加载失败。")
            traceback.print_exc(file=sys.stderr)

    def health(self) -> dict[str, Any]:
        return {
            "available": self.available,
            "message": self._error_message,
            "errorCode": self._error_code,
            "providerId": PROVIDER_ID,
            "workerVersion": WORKER_VERSION,
            "meloTtsVersion": MELOTTS_VERSION,
            "meloTtsCommit": MELOTTS_COMMIT,
            "modelRevision": MODEL_REVISION,
            "bertRevision": BERT_REVISION,
            "device": "cpu",
            "loadSeconds": self._load_seconds,
        }

    def voices(self) -> dict[str, Any]:
        voices = ([{"voiceId": VOICE_ID, "displayName": "中文标准讲解", "language": "zh-CN"}]
                  if self.available else [])
        return {"voices": voices, "capabilities": CAPABILITIES}

    def cancel(self, request_id: str) -> bool:
        with self._requests_gate:
            cancellation = self._request_cancellations.get(request_id)
        if cancellation is None:
            return False
        cancellation.set()
        return True

    def synthesize(self, payload: dict[str, Any]) -> tuple[bytes, str, dict[str, Any]]:
        if not self.available:
            raise WorkerFailure(self._error_code or "worker_unavailable",
                                self._error_message or "MeloTTS 本地语音服务不可用。", True)
        request_id = require_string(payload, "requestId", 128)
        text = normalize_text(require_string(payload, "text", CAPABILITIES["maxTextLength"]))
        voice = require_string(payload, "voice", 128)
        language = require_string(payload, "language", 32)
        rate = require_number(payload, "rate")
        pitch = require_number(payload, "pitch")
        volume = require_number(payload, "volume")
        output_media_type = require_string(payload, "outputMediaType", 64).lower()
        sample_rate = require_integer(payload, "sampleRateHz")
        channels = require_integer(payload, "channels")
        if not text:
            raise WorkerFailure("empty_text", "讲解词不能为空。")
        if voice != VOICE_ID:
            raise WorkerFailure("voice_not_found", "所选 MeloTTS 中文音色不存在。")
        if language.lower() not in ("zh-cn", "zh", "zh-hans"):
            raise WorkerFailure("language_not_supported", "MeloTTS 本地 Provider 仅支持中文普通话及中英混读。")
        if not CAPABILITIES["minRate"] <= rate <= CAPABILITIES["maxRate"]:
            raise WorkerFailure("rate_not_supported", "语速超出 MeloTTS 本地 Provider 支持范围。")
        if pitch != 0:
            raise WorkerFailure("pitch_not_supported", "MeloTTS 中文模型不支持音调参数。")
        if volume != 1:
            raise WorkerFailure("volume_not_supported", "MeloTTS 中文模型不支持合成音量参数。")
        if output_media_type not in ("audio/wav", "audio/x-wav"):
            raise WorkerFailure("media_type_not_supported", "MeloTTS 本地 Provider 仅输出 PCM WAV。")
        if sample_rate not in (0, self._sample_rate):
            raise WorkerFailure("sample_rate_not_supported", "MeloTTS 中文模型固定输出 44.1 kHz。")
        if channels not in (0, 1):
            raise WorkerFailure("channels_not_supported", "MeloTTS 中文模型固定输出单声道。")

        cancellation = threading.Event()
        with self._requests_gate:
            if request_id in self._request_cancellations:
                raise WorkerFailure("duplicate_request_id", "语音生成请求标识重复。")
            self._request_cancellations[request_id] = cancellation
        try:
            started = time.perf_counter()
            chunks = split_long_text(text)
            with self._synthesis_gate:
                audio = self._synthesize_chunks(chunks, rate, cancellation)
            if cancellation.is_set():
                raise WorkerFailure("cancelled", "语音生成已取消。")
            import soundfile
            output = io.BytesIO()
            soundfile.write(output, audio, self._sample_rate, format="WAV", subtype="PCM_16")
            wav = output.getvalue()
            elapsed = time.perf_counter() - started
            duration = len(audio) / float(self._sample_rate)
            metadata = {"chunkCount": len(chunks), "synthesisSeconds": elapsed,
                        "durationSeconds": duration, "rtf": elapsed / duration if duration else 0.0}
            return wav, request_id, metadata
        finally:
            with self._requests_gate:
                self._request_cancellations.pop(request_id, None)

    def _synthesize_chunks(self, chunks: list[str], rate: float, cancellation: threading.Event):
        import numpy
        segments = []
        for chunk in chunks:
            if cancellation.is_set():
                raise WorkerFailure("cancelled", "语音生成已取消。")
            segment = self._model.tts_to_file(chunk, self._speaker_id, output_path=None,
                                              speed=rate, quiet=True)
            segment = numpy.asarray(segment, dtype=numpy.float32).reshape(-1)
            if segment.size == 0:
                raise WorkerFailure("empty_audio", "MeloTTS 未生成有效音频。", True)
            fade_samples = min(int(self._sample_rate * 0.005), segment.size // 4)
            if fade_samples > 1:
                segment[:fade_samples] *= numpy.linspace(0.0, 1.0, fade_samples, dtype=numpy.float32)
                segment[-fade_samples:] *= numpy.linspace(1.0, 0.0, fade_samples, dtype=numpy.float32)
            segments.append(segment)
        return numpy.concatenate(segments)

    def _validate_bundle(self) -> None:
        required = {
            "acoustic/config.json": self._acoustic_model / "config.json",
            "acoustic/checkpoint.pth": self._acoustic_model / "checkpoint.pth",
            "bert/config.json": self._bert_model / "config.json",
            "bert/pytorch_model.bin": self._bert_model / "pytorch_model.bin",
            "bert/vocab.txt": self._bert_model / "vocab.txt",
        }
        if not (self._melotts_source / "melo" / "api.py").is_file():
            raise WorkerFailure("melotts_source_missing", "MeloTTS v0.1.2 程序包缺失。")
        if not self._nltk_data.is_dir():
            raise WorkerFailure("nltk_data_missing", "MeloTTS 英文混读词典缺失。")
        for identity, path in required.items():
            if not path.is_file():
                raise WorkerFailure("model_file_missing", f"MeloTTS 模型文件缺失：{identity}")
            if sha256_file(path) != EXPECTED_HASHES[identity]:
                raise WorkerFailure("model_hash_mismatch", f"MeloTTS 模型文件校验失败：{identity}")

    def _set_unavailable(self, code: str, message: str) -> None:
        self._model = None
        self._speaker_id = None
        self._error_code = code
        self._error_message = message
        print(json.dumps({"event": "worker_unavailable", "code": code, "message": message},
                         ensure_ascii=False), file=sys.stderr, flush=True)


def normalize_text(text: str) -> str:
    text = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", "", text)
    return re.sub(r"[ \t\r\f\v]+", " ", text).strip()


def split_long_text(text: str, max_chars: int = 220) -> list[str]:
    """Split at Chinese sentence punctuation while keeping alphanumeric tokens intact."""
    sentences = [part.strip() for part in re.findall(r".*?(?:[。！？!?；;\n]+|$)", text, re.S) if part.strip()]
    chunks: list[str] = []
    current = ""
    for sentence in sentences:
        for piece in split_oversized_piece(sentence, max_chars):
            candidate = current + piece
            if current and len(candidate) > max_chars:
                chunks.append(current.strip())
                current = piece
            else:
                current = candidate
    if current.strip():
        chunks.append(current.strip())
    return chunks or [text]


def split_oversized_piece(text: str, max_chars: int) -> list[str]:
    if len(text) <= max_chars:
        return [text]
    natural = [part for part in re.findall(r".*?(?:[，,、：:]|$)", text, re.S) if part]
    pieces: list[str] = []
    for part in natural:
        if len(part) <= max_chars:
            pieces.append(part)
            continue
        tokens = re.findall(r"[A-Za-z]+(?:[.\-/][A-Za-z0-9]+)*|\d+(?:\.\d+)*|.", part, re.S)
        packed = ""
        for token in tokens:
            if packed and len(packed) + len(token) > max_chars:
                pieces.append(packed)
                packed = token
            else:
                packed += token
        if packed:
            pieces.append(packed)
    return pieces


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require_string(payload: dict[str, Any], key: str, maximum: int) -> str:
    value = payload.get(key)
    if not isinstance(value, str) or not value.strip() or len(value) > maximum:
        raise WorkerFailure("invalid_input", f"字段 {key} 无效。")
    return value.strip()


def require_number(payload: dict[str, Any], key: str) -> float:
    value = payload.get(key)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise WorkerFailure("invalid_input", f"字段 {key} 无效。")
    return float(value)


def require_integer(payload: dict[str, Any], key: str) -> int:
    value = payload.get(key)
    if isinstance(value, bool) or not isinstance(value, int):
        raise WorkerFailure("invalid_input", f"字段 {key} 无效。")
    return value


class WorkerHttpServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, address: tuple[str, int], engine: MeloEngine):
        super().__init__(address, WorkerRequestHandler)
        self.engine = engine

    def handle_error(self, request, client_address) -> None:
        exception = sys.exc_info()[1]
        if isinstance(exception, (BrokenPipeError, ConnectionResetError)):
            return
        super().handle_error(request, client_address)


class WorkerRequestHandler(BaseHTTPRequestHandler):
    server: WorkerHttpServer
    protocol_version = "HTTP/1.1"

    def do_GET(self) -> None:
        if not self._is_loopback_client():
            self._json(HTTPStatus.FORBIDDEN, error("forbidden", "Worker 仅允许本机访问。"))
        elif self.path == "/health":
            self._json(HTTPStatus.OK, self.server.engine.health())
        elif self.path == "/voices":
            self._json(HTTPStatus.OK, self.server.engine.voices())
        else:
            self._json(HTTPStatus.NOT_FOUND, error("not_found", "接口不存在。"))

    def do_POST(self) -> None:
        if not self._is_loopback_client():
            self._json(HTTPStatus.FORBIDDEN, error("forbidden", "Worker 仅允许本机访问。"))
            return
        cancel_match = re.fullmatch(r"/requests/([A-Za-z0-9_.-]{1,128})/cancel", self.path)
        if cancel_match:
            cancelled = self.server.engine.cancel(cancel_match.group(1))
            self._json(HTTPStatus.OK, {"cancelled": cancelled})
            return
        if self.path != "/synthesize":
            self._json(HTTPStatus.NOT_FOUND, error("not_found", "接口不存在。"))
            return
        try:
            payload = self._read_json()
            wav, request_id, metadata = self.server.engine.synthesize(payload)
            self.send_response(HTTPStatus.OK)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(wav)))
            self.send_header("X-TG-TTS-Request-Id", request_id)
            self.send_header("X-TG-TTS-Duration", f"{metadata['durationSeconds']:.6f}")
            self.send_header("X-TG-TTS-Synthesis-Seconds", f"{metadata['synthesisSeconds']:.6f}")
            self.send_header("Connection", "close")
            self.end_headers()
            self.close_connection = True
            self.wfile.write(wav)
        except WorkerFailure as failure:
            status = HTTPStatus.SERVICE_UNAVAILABLE if failure.transient else HTTPStatus.BAD_REQUEST
            self._json(status, error(failure.code, failure.message, failure.transient))
        except (BrokenPipeError, ConnectionResetError):
            pass
        except Exception:
            traceback.print_exc(file=sys.stderr)
            self._json(HTTPStatus.INTERNAL_SERVER_ERROR,
                       error("worker_internal", "MeloTTS Worker 处理失败。", True))

    def log_message(self, format_string: str, *args: Any) -> None:
        print(json.dumps({"event": "http", "client": self.client_address[0],
                          "message": format_string % args}, ensure_ascii=False), flush=True)

    def _read_json(self) -> dict[str, Any]:
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError as exception:
            raise WorkerFailure("invalid_json", "请求长度无效。") from exception
        if length <= 0 or length > 1024 * 1024:
            raise WorkerFailure("invalid_json", "请求正文为空或过大。")
        try:
            value = json.loads(self.rfile.read(length).decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exception:
            raise WorkerFailure("invalid_json", "请求不是有效 JSON。") from exception
        if not isinstance(value, dict):
            raise WorkerFailure("invalid_json", "请求必须是 JSON 对象。")
        return value

    def _json(self, status: HTTPStatus, payload: dict[str, Any]) -> None:
        data = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        try:
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Connection", "close")
            self.end_headers()
            self.close_connection = True
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def _is_loopback_client(self) -> bool:
        try:
            return ipaddress.ip_address(self.client_address[0]).is_loopback
        except ValueError:
            return False


def error(code: str, message: str, transient: bool = False) -> dict[str, Any]:
    return {"error": {"code": code, "message": message, "transient": transient}}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="TG MeloTTS loopback worker")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5091)
    parser.add_argument("--melotts-source", required=True)
    parser.add_argument("--acoustic-model", required=True)
    parser.add_argument("--bert-model", required=True)
    parser.add_argument("--nltk-data", required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        if not ipaddress.ip_address(args.host).is_loopback:
            raise ValueError("Worker host must be loopback.")
        if not 1 <= args.port <= 65535:
            raise ValueError("Worker port is invalid.")
    except ValueError as exception:
        print(str(exception), file=sys.stderr)
        return 2

    engine = MeloEngine(Path(args.melotts_source), Path(args.acoustic_model),
                        Path(args.bert_model), Path(args.nltk_data))
    loader = threading.Thread(target=engine.initialize, name="melotts-model-loader", daemon=True)
    loader.start()
    server = WorkerHttpServer((args.host, args.port), engine)
    print(json.dumps({"event": "worker_listening", "host": args.host, "port": args.port,
                      "providerId": PROVIDER_ID}, ensure_ascii=False), flush=True)
    try:
        server.serve_forever(poll_interval=0.25)
    except KeyboardInterrupt:
        pass
    finally:
        server.shutdown()
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
