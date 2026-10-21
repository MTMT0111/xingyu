"""Extract bounded video evidence locally; the caller owns upload cleanup and timeout."""
import base64
import io
import json
import logging
import math
import os
from pathlib import Path
import sys
import warnings

MAX_BYTES = 512 * 1024 * 1024
SEGMENT_SECONDS = 180
SAMPLE_RATE = 16000


class MediaError(Exception):
    def __init__(self, code, message):
        self.code = code
        self.message = message


def fail(code, message):
    raise MediaError(code, message)


def video_frames(path, av):
    try:
        container = av.open(str(path))
    except Exception:
        fail("INVALID_VIDEO", "无法打开视频，请上传完整的 MP4、MOV 或 WebM 文件。")
    with container:
        if not container.streams.video:
            fail("NO_VIDEO_TRACK", "文件没有可读取的视频画面。")
        stream = container.streams.video[0]
        if stream.width < 1 or stream.height < 1 or stream.width * stream.height > 3840 * 2160:
            fail("VIDEO_RESOLUTION_LIMIT", "请上传分辨率不超过 4K 的视频。")
        durations = []
        if container.duration is not None:
            durations.append(float(container.duration / av.time_base))
        if stream.duration is not None and stream.time_base:
            durations.append(float(stream.duration * stream.time_base))
        if not durations or any(not math.isfinite(value) or value <= 0 for value in durations):
            fail("UNKNOWN_DURATION", "无法确认视频时长，请重新导出为 MP4 后重试。")
        duration = max(durations)
        origin = stream.start_time or 0
        frames = []
        frame_bytes = 0
        count = 6 * math.ceil(duration / SEGMENT_SECONDS)
        for index in range(count):
            part = index // 6
            start = part * SEGMENT_SECONDS
            target = start + min(SEGMENT_SECONDS, duration - start) * (index % 6) / 6
            container.seek(origin + int(target / stream.time_base), stream=stream, backward=True)
            candidate = None
            for decoded_count, frame in enumerate(container.decode(stream)):
                if decoded_count > 3600:
                    fail("VIDEO_DECODE_LIMIT", "视频编码过于复杂，请重新导出后重试。")
                if frame.width * frame.height > 3840 * 2160:
                    fail("VIDEO_RESOLUTION_LIMIT", "请上传分辨率不超过 4K 的视频。")
                if frame.pts is None:
                    continue
                timestamp = float((frame.pts - origin) * stream.time_base)
                candidate = (frame, max(0, timestamp))
                if timestamp >= target - 0.05:
                    break
            if candidate is None:
                fail("VIDEO_FRAME_MISSING", "部分画面无法解码，请重新导出视频后重试。")
            frame, timestamp = candidate
            picture = frame.to_image()
            picture.thumbnail((768, 768))
            encoded = io.BytesIO()
            picture.convert("RGB").save(encoded, format="JPEG", quality=78)
            frame_bytes += len(encoded.getvalue())
            if frame_bytes > 24 * 1024 * 1024:
                fail("EVIDENCE_CAPACITY", "完整画面证据超过本机容量，未截断后生成。")
            frames.append({"time": round(timestamp, 3), "dataUrl": "data:image/jpeg;base64," + base64.b64encode(encoded.getvalue()).decode("ascii")})
        return round(duration, 3), frames


def audio_evidence(path, model_path, av, np, duration):
    parts = [dict(start=i, end=min(i + SEGMENT_SECONDS, duration), transcript="")
             for i in range(0, math.ceil(duration), SEGMENT_SECONDS)]
    try:
        with av.open(str(path)) as container:
            if not container.streams.audio:
                return parts, "silent", ["NO_AUDIO_TRACK"]
            if not model_path.is_dir() or not (model_path / "model.bin").is_file():
                fail("ASR_MODEL_MISSING", "本地语音模型未就绪。")
            from faster_whisper import WhisperModel
            model = WhisperModel(str(model_path), device="cpu", compute_type="int8", cpu_threads=min(4, os.cpu_count() or 1), num_workers=1, local_files_only=True)
            resampler = av.AudioResampler(format="fltp", layout="mono", rate=SAMPLE_RATE)
            capacity = SEGMENT_SECONDS * SAMPLE_RATE
            buffer = np.empty(capacity, dtype=np.float32)
            used, index = 0, 0
            def transcribe(waveform, position):
                if not np.isfinite(waveform).all():
                    fail("INVALID_AUDIO_SAMPLES", "音轨样本损坏。")
                if position >= len(parts):
                    fail("AUDIO_DURATION_MISMATCH", "音轨时长与视频不符，未截断后生成。")
                if waveform.size and float(np.max(np.abs(waveform))) >= 0.0001:
                    segments, _ = model.transcribe(waveform, language="zh", beam_size=3, vad_filter=True, condition_on_previous_text=False)
                    parts[position]["transcript"] = "".join(segment.text.strip() for segment in segments if segment.no_speech_prob < 0.8)
            for frame in container.decode(container.streams.audio[0]):
                for converted in resampler.resample(frame):
                    samples = converted.to_ndarray().reshape(-1)
                    offset = 0
                    while offset < samples.size:
                        size = min(capacity - used, samples.size - offset)
                        buffer[used:used + size] = samples[offset:offset + size]
                        used += size
                        offset += size
                        if used == capacity:
                            transcribe(buffer, index)
                            index += 1
                            used = 0
            for converted in resampler.resample(None):
                samples = converted.to_ndarray().reshape(-1)
                if used + samples.size > capacity:
                    transcribe(buffer[:used], index)
                    index += 1
                    used = 0
                buffer[used:used + samples.size] = samples
                used += samples.size
            if used:
                transcribe(buffer[:used], index)
        return parts, "transcribed" if any(part["transcript"] for part in parts) else "silent", []
    except MediaError:
        raise
    except Exception:
        fail("AUDIO_DECODE_FAILED", "部分视频语音未处理成功，未用前半段冒充完整内容。")


def main():
    if len(sys.argv) != 4 or sys.argv[2] != "--model":
        fail("INVALID_ARGUMENTS", "视频处理参数不完整。")
    path = Path(sys.argv[1])
    try:
        size = path.stat().st_size
        if not path.is_file():
            fail("INPUT_UNAVAILABLE", "无法读取上传的视频。")
    except OSError:
        fail("INPUT_UNAVAILABLE", "无法读取上传的视频。")
    if not 0 < size <= MAX_BYTES:
        fail("VIDEO_SIZE_LIMIT", "请上传非空且不超过 512 MiB 的视频。")
    try:
        import av
        import numpy as np
        av.logging.set_level(av.logging.PANIC)
    except Exception:
        fail("MEDIA_DEPENDENCIES_MISSING", "本地视频处理依赖尚未安装。")
    try:
        duration, frames = video_frames(path, av)
    except MediaError:
        raise
    except Exception:
        fail("VIDEO_DECODE_FAILED", "视频画面解码失败，请重新导出后重试。")
    parts, audio_status, notices = audio_evidence(path, Path(sys.argv[3]), av, np, duration)
    for index, part in enumerate(parts):
        part["frames"] = frames[index * 6:(index + 1) * 6]
    transcript = "\n".join(part["transcript"] for part in parts)
    return {"duration": duration, "frames": frames, "segments": parts, "transcript": transcript, "audioStatus": audio_status, "warnings": notices}


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    logging.disable(logging.CRITICAL)
    warnings.filterwarnings("ignore")
    try:
        print(json.dumps(main(), ensure_ascii=False, separators=(",", ":")))
    except MediaError as error:
        print(json.dumps({"error": {"code": error.code, "message": error.message}}, ensure_ascii=False))
        sys.exit(1)
    except Exception:
        print(json.dumps({"error": {"code": "MEDIA_PROCESSING_FAILED", "message": "视频处理未完成，请重试。"}}, ensure_ascii=False))
        sys.exit(1)
