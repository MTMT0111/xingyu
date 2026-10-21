import { mkdtemp, open, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Readable } from 'node:stream';

export const MAX_VIDEO_BYTES = 512 * 1024 * 1024;
export const VIDEO_TIMEOUT_MS = 90 * 60 * 1000;

// Both local uploads and the WeChat connector stream to disk, never to a base64 JSON body.
export async function storeVideoStream(stream, signal) {
  const directory = await mkdtemp(join(tmpdir(), 'zhiyi-source-'));
  const path = join(directory, 'video');
  const dispose = () => rm(directory, { recursive: true, force: true });
  const source = typeof stream.getReader === 'function' ? Readable.fromWeb(stream) : stream;
  const cancel = () => source.destroy(signal.reason);
  signal.addEventListener('abort', cancel, { once: true });
  let file;
  try {
    signal.throwIfAborted();
    file = await open(path, 'w+');
    let size = 0;
    for await (const chunk of source) {
      signal.throwIfAborted();
      size += chunk.length;
      if (size > MAX_VIDEO_BYTES) throw new Error('视频超过本机单文件 512 MB 容量，请使用较低清晰度版本。');
      await file.writeFile(chunk);
    }
    signal.throwIfAborted();
    const header = Buffer.alloc(16);
    await file.read(header, 0, header.length, 0);
    const mime = header.toString('ascii', 4, 8) === 'ftyp' ? 'video/mp4' : header.subarray(0, 4).equals(Buffer.from('1a45dfa3', 'hex')) ? 'video/webm' : null;
    if (!mime || size < 16) throw new Error('未取得完整的 MP4、MOV 或 WebM 视频文件。');
    await file.close(); file = null;
    return { kind: 'video', name: 'video.mp4', mime, path, size, dispose };
  } catch (error) {
    if (file) await file.close();
    await dispose();
    if (!error.mediaCode) error.mediaCode = 'MEDIA_INVALID';
    throw error;
  } finally { signal.removeEventListener('abort', cancel); source.destroy(); }
}
