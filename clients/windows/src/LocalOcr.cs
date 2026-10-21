using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ZhiyiDesktop
{
    internal static class LocalOcr
    {
        private static async Task<T> Wait<T>(Windows.Foundation.IAsyncOperation<T> operation)
        {
            while (operation.Status == Windows.Foundation.AsyncStatus.Started) await Task.Delay(20);
            try { return operation.GetResults(); }
            finally { operation.Close(); }
        }

        // Windows 自带识别，不上传图片，也不安装额外模型。
        internal static async Task<string> Read(byte[] image)
        {
            var engine = OcrEngine.TryCreateFromLanguage(new Language("zh-Hans")) ?? OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) throw new InvalidOperationException("系统未安装文字识别语言；仍可点击生成进行图像分析。");
            using (var stream = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(image);
                    await Wait<uint>(writer.StoreAsync());
                }
                stream.Seek(0);
                var decoder = await Wait<BitmapDecoder>(BitmapDecoder.CreateAsync(stream));
                using (var bitmap = await Wait<SoftwareBitmap>(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)))
                {
                    var result = await Wait<OcrResult>(engine.RecognizeAsync(bitmap));
                    return WeChatCapture.NormalizeLines(result.Lines.Select(line => line.Text), 6000);
                }
            }
        }
    }
}
