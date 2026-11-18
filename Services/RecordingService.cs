using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Vortice.MediaFoundation;

namespace UToolBar.Services;

public enum RecordFormat
{
    Mp4,
    Gif
}

public enum RecordQuality
{
    Sd,
    Hd
}

public sealed class RecordingOptions
{
    public Rectangle Region { get; set; }
    public RecordFormat Format { get; set; } = RecordFormat.Mp4;
    public RecordQuality Quality { get; set; } = RecordQuality.Sd;
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; }
    public string OutputPath { get; set; }
}

/// <summary>录屏会话：MP4 走 Media Foundation 硬件编码，GIF 走低帧率采集 + 流式编码</summary>
public static class RecordingService
{
    private static RecordingSession _session;

    public static bool IsRecording => _session is { IsRunning: true };
    public static DateTime StartedAt => _session?.StartedAt ?? DateTime.Now;

    public static event Action<string> Saved;
    public static event Action<string> Failed;

    public static string Folder
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "UTB录屏");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string SuggestFileName(RecordFormat format)
    {
        var ext = format == RecordFormat.Gif ? "gif" : "mp4";
        return Path.Combine(Folder, $"UTB录屏_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");
    }

    public static bool Start(RecordingOptions options)
    {
        if (IsRecording) return false;
        if (options.Region.Width < 16 || options.Region.Height < 16) return false;

        var session = new RecordingSession(options);
        session.Saved += path => Saved?.Invoke(path);
        session.Failed += message =>
        {
            _session = null;
            Failed?.Invoke(message);
        };
        session.Ended += () => _session = null;

        if (!session.Start())
        {
            return false;
        }

        _session = session;
        return true;
    }

    public static void Stop()
    {
        _session?.Stop();
    }
}

internal sealed class RecordingSession
{
    private readonly RecordingOptions _options;
    private readonly ManualResetEventSlim _stopSignal = new(false);
    private Thread _thread;

    private WasapiLoopbackCapture _loopback;
    private WasapiCapture _microphone;
    private BufferedWaveProvider _loopbackBuffer;
    private BufferedWaveProvider _microphoneBuffer;
    private IWaveProvider _loopbackReader;
    private IWaveProvider _microphoneReader;

    public event Action<string> Saved;
    public event Action<string> Failed;
    public event Action Ended;

    public DateTime StartedAt { get; } = DateTime.Now;
    public bool IsRunning { get; private set; }

    public RecordingSession(RecordingOptions options)
    {
        _options = options;
    }

    public bool Start()
    {
        IsRunning = true;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "UToolBar.Recorder"
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        return true;
    }

    public void Stop()
    {
        _stopSignal.Set();
    }

    private void Run()
    {
        try
        {
            if (_options.Format == RecordFormat.Gif)
                RunGif();
            else
                RunMp4();
        }
        catch (Exception ex)
        {
            App.Trace("record failed: " + ex);
            Failed?.Invoke(ex.Message);
        }
        finally
        {
            IsRunning = false;
            StopAudio();
            Ended?.Invoke();
        }
    }

    // ================= MP4 =================

    private void RunMp4()
    {
        MediaFactory.MFStartup(true);
        IMFSinkWriter writer = null;
        try
        {
            var region = NormalizeRegion(_options.Region);
            int width = Even(region.Width);
            int height = Even(region.Height);

            // 标清：高度压到 720 以内，文件更小、编码更轻
            if (_options.Quality == RecordQuality.Sd && height > 720)
            {
                double scale = 720.0 / height;
                width = Even((int)Math.Round(width * scale));
                height = Even((int)Math.Round(height * scale));
            }

            const int fps = 30;
            int bitrate = _options.Quality == RecordQuality.Hd ? 10_000_000 : 4_000_000;

            var attributes = MediaFactory.MFCreateAttributes(3);
            // 部分 Intel 硬件编码器对任意宽度（非 16 对齐）会崩溃，改用系统软件编码器更稳
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 0u);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteDisableConverters, 0u);
            attributes.Set(SinkWriterAttributeKeys.LowLatency, 0u);

            writer = MediaFactory.MFCreateSinkWriterFromURL(_options.OutputPath, null, attributes);
            attributes.Dispose();

            int videoStream = writer.AddStream(CreateVideoOutType(width, height, fps, bitrate));
            writer.SetInputMediaType(videoStream, CreateVideoInType(width, height, fps), null);

            int audioStream = -1;
            if (_options.SystemAudio || _options.Microphone)
            {
                StartAudio();
                if (_loopbackReader != null || _microphoneReader != null)
                {
                    audioStream = writer.AddStream(CreateAudioOutType());
                    writer.SetInputMediaType(audioStream, CreateAudioInType(), null);
                }
            }

            writer.BeginWriting();

            using var capture = new ScreenFrameCapture(region, width, height);
            var clock = Stopwatch.StartNew();
            long nextVideo = 0;
            long nextAudio = 0;
            const int audioChunkBytes = 960 * 4; // 20ms, 48kHz stereo 16bit
            long audioTime = 0;

            while (!_stopSignal.IsSet)
            {
                long now = clock.ElapsedTicks * 10_000_000L / Stopwatch.Frequency;

                if (now >= nextVideo)
                {
                    var frame = capture.Capture();
                    if (frame != null)
                    {
                        WriteVideoSample(writer, videoStream, frame, now, 10_000_000L / fps);
                    }
                    nextVideo += 10_000_000L / fps;
                    if (nextVideo < now - 10_000_000L) nextVideo = now;
                }

                if (audioStream >= 0 && now >= nextAudio)
                {
                    // 一次循环里最多补 10 块（200ms），避免卡顿后音轨越落越远
                    int burst = 0;
                    while (audioStream >= 0 && now >= nextAudio && burst++ < 10)
                    {
                        WriteAudioSample(writer, audioStream, audioChunkBytes, audioTime);
                        audioTime += 200_000L; // 960 帧 @ 48kHz = 20ms
                        nextAudio += 200_000L;
                    }

                    // 落后太多就整体跳过，时间戳一起跳，避免后面的音频样本变成“过去时间”被封装器丢掉
                    if (nextAudio < now - 2_000_000L)
                    {
                        long skip = now - nextAudio;
                        nextAudio += skip;
                        audioTime += skip;
                    }
                }

                long next = audioStream >= 0 ? Math.Min(nextVideo, nextAudio) : nextVideo;
                int sleep = (int)Math.Min(8, Math.Max(1, (next - now) / 10_000));
                Thread.Sleep(sleep);
            }

            writer.Finalize();
            writer.Dispose();
            writer = null;

            Saved?.Invoke(_options.OutputPath);
        }
        finally
        {
            writer?.Dispose();
            MediaFactory.MFShutdown();
        }
    }

    private static IMFMediaType CreateVideoOutType(int width, int height, int fps, int bitrate)
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        type.Set(MediaTypeAttributeKeys.Subtype, new Guid("34363248-0000-0010-8000-00AA00389B71")); // H264
        type.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
        type.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // progressive
        type.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)width << 32) | (uint)height);
        type.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)fps << 32) | 1u);
        type.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)1 << 32) | 1u);
        return type;
    }

    private static IMFMediaType CreateVideoInType(int width, int height, int fps)
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Argb32);
        type.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        type.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)width << 32) | (uint)height);
        type.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)fps << 32) | 1u);
        type.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)1 << 32) | 1u);
        return type;
    }

    private static IMFMediaType CreateAudioOutType()
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        type.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
        type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000u);
        type.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
        type.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000u); // 192 kbps
        type.Set(MediaTypeAttributeKeys.AacPayloadType, 0u);
        type.Set(MediaTypeAttributeKeys.AacAudioProfileLevelIndication, 0x29u);
        return type;
    }

    private static IMFMediaType CreateAudioInType()
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        type.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000u);
        type.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
        type.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        type.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4u);
        type.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 192000u);
        return type;
    }

    private static void WriteVideoSample(IMFSinkWriter writer, int stream, Bitmap frame, long timestamp, long duration)
    {
        int width = frame.Width;
        int height = frame.Height;
        int stride = width * 4;
        int length = stride * height;

        var buffer = MediaFactory.MFCreateMemoryBuffer(length);
        buffer.Lock(out IntPtr pointer, out _, out _);
        var data = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Media Foundation 的 RGB/ARGB 输入按自下而上解释，逐行倒序写入
            for (int y = 0; y < height; y++)
            {
                CopyMemory(pointer + y * stride, data.Scan0 + (height - 1 - y) * data.Stride, (IntPtr)stride);
            }
        }
        finally
        {
            frame.UnlockBits(data);
            buffer.Unlock();
        }

        buffer.CurrentLength = length;
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = timestamp;
        sample.SampleDuration = duration;
        writer.WriteSample(stream, sample);
        buffer.Dispose();
    }

    private void WriteAudioSample(IMFSinkWriter writer, int stream, int bytes, long timestamp)
    {
        byte[] mixed = new byte[bytes];
        byte[] temp = new byte[bytes];
        bool any = false;

        if (_loopbackReader != null)
        {
            int read = _loopbackReader.Read(temp, 0, bytes);
            if (read > 0)
            {
                MixInto(mixed, temp, read);
                any = true;
            }
        }

        if (_microphoneReader != null)
        {
            int read = _microphoneReader.Read(temp, 0, bytes);
            if (read > 0)
            {
                MixInto(mixed, temp, read);
                any = true;
            }
        }

        if (!any && _loopbackReader == null && _microphoneReader == null) return;

        var buffer = MediaFactory.MFCreateMemoryBuffer(bytes);
        buffer.Lock(out IntPtr pointer, out _, out _);
        Marshal.Copy(mixed, 0, pointer, bytes);
        buffer.Unlock();
        buffer.CurrentLength = bytes;

        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = timestamp;
        sample.SampleDuration = 200_000L; // 20ms
        writer.WriteSample(stream, sample);
        buffer.Dispose();
    }

    private static void MixInto(byte[] target, byte[] source, int bytes)
    {
        int count = Math.Min(bytes, target.Length) / 2 * 2;
        for (int i = 0; i < count; i += 2)
        {
            int a = target[i] | (target[i + 1] << 8);
            int b = source[i] | (source[i + 1] << 8);
            if (a > 32767) a -= 65536;
            if (b > 32767) b -= 65536;
            int sum = a + b;
            if (sum > 32767) sum = 32767;
            else if (sum < -32768) sum = -32768;
            target[i] = (byte)(sum & 0xFF);
            target[i + 1] = (byte)((sum >> 8) & 0xFF);
        }
    }

    // ================= 音频采集 =================

    private void StartAudio()
    {
        var target = new WaveFormat(48000, 16, 2);

        if (_options.SystemAudio)
        {
            try
            {
                _loopback = new WasapiLoopbackCapture();
                _loopbackBuffer = new BufferedWaveProvider(_loopback.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferLength = _loopback.WaveFormat.AverageBytesPerSecond * 2
                };
                _loopback.DataAvailable += (_, e) =>
                {
                    try { _loopbackBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded); }
                    catch { }
                };
                _loopback.StartRecording();
                _loopbackReader = CreatePcmReader(_loopbackBuffer, target);
            }
            catch
            {
                _loopback = null;
                _loopbackReader = null;
            }
        }

        if (_options.Microphone)
        {
            try
            {
                _microphone = new WasapiCapture();
                _microphoneBuffer = new BufferedWaveProvider(_microphone.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferLength = _microphone.WaveFormat.AverageBytesPerSecond * 2
                };
                _microphone.DataAvailable += (_, e) =>
                {
                    try { _microphoneBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded); }
                    catch { }
                };
                _microphone.StartRecording();
                _microphoneReader = CreatePcmReader(_microphoneBuffer, target);
            }
            catch
            {
                _microphone = null;
                _microphoneReader = null;
            }
        }
    }

    private static IWaveProvider CreatePcmReader(BufferedWaveProvider buffer, WaveFormat target)
    {
        IWaveProvider provider = buffer;
        if (provider.WaveFormat.SampleRate != target.SampleRate ||
            provider.WaveFormat.Channels != target.Channels ||
            provider.WaveFormat.BitsPerSample != target.BitsPerSample)
        {
            provider = new MediaFoundationResampler(provider, target)
            {
                ResamplerQuality = 40
            };
        }
        return provider;
    }

    private void StopAudio()
    {
        try { _loopback?.StopRecording(); } catch { }
        try { _microphone?.StopRecording(); } catch { }
        try { (_loopbackReader as IDisposable)?.Dispose(); } catch { }
        try { (_microphoneReader as IDisposable)?.Dispose(); } catch { }
        try { _loopback?.Dispose(); } catch { }
        try { _microphone?.Dispose(); } catch { }
        _loopback = null;
        _microphone = null;
        _loopbackReader = null;
        _microphoneReader = null;
        _loopbackBuffer = null;
        _microphoneBuffer = null;
    }

    // ================= GIF =================

    private void RunGif()
    {
        var region = NormalizeRegion(_options.Region);
        int targetWidth = Math.Min(800, region.Width);
        targetWidth = Math.Max(64, targetWidth / 2 * 2);
        int targetHeight = Math.Max(2, (int)Math.Round(region.Height * targetWidth / (double)region.Width) / 2 * 2);

        const int fps = 10;
        using var capture = new ScreenFrameCapture(region, targetWidth, targetHeight);
        using var stream = File.Create(_options.OutputPath);
        using var gif = new GifWriter(stream, targetWidth, targetHeight);

        var clock = Stopwatch.StartNew();
        long nextFrame = 0;
        while (!_stopSignal.IsSet)
        {
            long now = clock.ElapsedTicks * 10_000_000L / Stopwatch.Frequency;
            if (now < nextFrame)
            {
                Thread.Sleep(4);
                continue;
            }

            var frame = capture.Capture();
            if (frame != null)
            {
                var data = frame.LockBits(new Rectangle(0, 0, targetWidth, targetHeight),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    gif.WriteFrame(data.Scan0, data.Stride, 100 / fps);
                }
                finally
                {
                    frame.UnlockBits(data);
                }
            }

            nextFrame += 10_000_000L / fps;
            if (nextFrame < now - 10_000_000L) nextFrame = now;
        }

        gif.Finish();
        Saved?.Invoke(_options.OutputPath);
    }

    // ================= 工具 =================

    private static Rectangle NormalizeRegion(Rectangle region)
    {
        if (region.Width < 16) region.Width = 16;
        if (region.Height < 16) region.Height = 16;
        return region;
    }

    private static int Even(int value) => Math.Max(2, value / 2 * 2);

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, IntPtr length);
}

/// <summary>按固定输出尺寸抓屏，需要缩放时在抓取后重采样</summary>
internal sealed class ScreenFrameCapture : IDisposable
{
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private const int CursorShowing = 0x0001;
    private const int DiNormal = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public System.Drawing.Point ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo info);
    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
        int cx, int cy, int istep, IntPtr hbrFlickerFreeDraw, int diFlags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int width, int height,
        IntPtr hdcSrc, int srcX, int srcY, uint rop);

    private readonly Rectangle _region;
    private readonly int _width;
    private readonly int _height;
    private readonly Bitmap _native;
    private readonly Bitmap _output;
    private readonly Graphics _nativeGraphics;
    private readonly Graphics _outputGraphics;

    public ScreenFrameCapture(Rectangle region, int width, int height)
    {
        _region = region;
        _width = width;
        _height = height;
        _native = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        _nativeGraphics = Graphics.FromImage(_native);
        if (region.Width != width || region.Height != height)
        {
            _output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            _outputGraphics = Graphics.FromImage(_output);
            _outputGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            _outputGraphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
        }
    }

    public Bitmap Capture()
    {
        try
        {
            // CaptureBlt 把分层窗口（动态壁纸等）也抓进来，纯 CopyFromScreen 会漏掉
            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                IntPtr destDc = _nativeGraphics.GetHdc();
                try
                {
                    BitBlt(destDc, 0, 0, _native.Width, _native.Height, screenDc,
                        _region.Left, _region.Top, SrcCopy | CaptureBlt);
                }
                finally
                {
                    _nativeGraphics.ReleaseHdc(destDc);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }

            var bitmap = _output ?? _native;
            if (_output != null)
            {
                _outputGraphics.DrawImage(_native, new Rectangle(0, 0, _width, _height));
            }

            DrawCursor(bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void DrawCursor(Bitmap bitmap)
    {
        try
        {
            var info = new CursorInfo { cbSize = Marshal.SizeOf<CursorInfo>() };
            if (!GetCursorInfo(ref info) || info.hCursor == IntPtr.Zero) return;
            if ((info.flags & CursorShowing) == 0) return;

            int x = info.ptScreenPos.X - _region.Left;
            int y = info.ptScreenPos.Y - _region.Top;
            if (x < -32 || y < -32 || x > _width + 32 || y > _height + 32) return;

            if (_output != null && _native.Width != _width)
            {
                x = x * _width / _native.Width;
                y = y * _height / _native.Height;
            }

            // 用当前真实光标句柄绘制，避免后台线程 Cursor.Current 默认拿到“忙碌圆圈”
            int hotX = 0, hotY = 0;
            if (GetIconInfo(info.hCursor, out var icon))
            {
                hotX = icon.xHotspot;
                hotY = icon.yHotspot;
                if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
                if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
            }

            using var graphics = Graphics.FromImage(bitmap);
            IntPtr hdc = graphics.GetHdc();
            try
            {
                DrawIconEx(hdc, x - hotX, y - hotY, info.hCursor, 0, 0, 0, IntPtr.Zero, DiNormal);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _nativeGraphics?.Dispose();
        _outputGraphics?.Dispose();
        _native?.Dispose();
        _output?.Dispose();
    }
}

