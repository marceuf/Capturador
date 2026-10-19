using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using System.Windows;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Capturador;

/// <summary>
/// Grava uma área da tela em MP4 (H.264, 30 qps, com cursor). Os quadros são copiados da tela
/// numa thread própria, convertidos para NV12 e entregues ao codificador do Windows pelo MediaStreamSource. As janelas
/// do próprio app ficam fora do vídeo (WDA_EXCLUDEFROMCAPTURE).
/// </summary>
public sealed class ScreenRecorder
{
    private const int Fps = 30;
    /// <summary>Limite do codificador H.264 do Windows; áreas maiores são reduzidas no vídeo.</summary>
    private const int MaxWidth = 3840, MaxHeight = 2160;

    private sealed record Frame(byte[] Pixels, TimeSpan Time);

    private CancellationTokenSource? _stop;
    private Task? _session;

    public bool IsRecording => _session is not null;

    public event Action<bool>? StateChanged;

    public async Task StartAsync(PixelRect rect, string path)
    {
        if (_session is not null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, []);
        IRandomAccessStream? output = null;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            output = await file.OpenAsync(FileAccessMode.ReadWrite);

            // NV12: o formato do codificador H.264. Com BGRA, o vídeo saía verde e rosa.
            var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Nv12, (uint)rect.Width, (uint)rect.Height);
            input.FrameRate.Numerator = Fps;
            input.FrameRate.Denominator = 1;
            var source = new MediaStreamSource(new VideoStreamDescriptor(input)) { BufferTime = TimeSpan.Zero, CanSeek = false };

            var scale = Math.Min(1, Math.Min((double)MaxWidth / rect.Width, (double)MaxHeight / rect.Height));
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Audio = null;
            profile.Video.Width = (uint)Math.Max(2, (int)(rect.Width * scale) & ~1);
            profile.Video.Height = (uint)Math.Max(2, (int)(rect.Height * scale) & ~1);
            profile.Video.FrameRate.Numerator = Fps;
            profile.Video.FrameRate.Denominator = 1;
            profile.Video.Bitrate = (uint)Math.Clamp(profile.Video.Width * profile.Video.Height * Fps / 8, 1_000_000, 24_000_000);

            var frames = Channel.CreateBounded<Frame>(new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            var pool = new ConcurrentBag<byte[]>();
            TimeSpan? firstFrame = null;
            source.SampleRequested += async (_, args) =>
            {
                var deferral = args.Request.GetDeferral();
                try
                {
                    // Sem quadro novo e com a gravação encerrada, o fim do fluxo fecha o arquivo.
                    if (await frames.Reader.WaitToReadAsync() && frames.Reader.TryRead(out var f))
                    {
                        // O vídeo começa no primeiro quadro entregue, sem início congelado.
                        firstFrame ??= f.Time;
                        var sample = MediaStreamSample.CreateFromBuffer(f.Pixels.AsBuffer(), f.Time - firstFrame.Value);
                        sample.Duration = TimeSpan.FromSeconds(1.0 / Fps);
                        sample.Processed += (_, _) => pool.Add(f.Pixels);
                        args.Request.Sample = sample;
                    }
                    else args.Request.Sample = null;
                }
                catch (Exception)
                {
                    // Este evento roda fora da thread da interface: uma exceção aqui derrubaria o app.
                    try { args.Request.Sample = null; } catch (Exception) { }
                }
                finally { deferral.Complete(); }
            };

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, output, profile);
            if (!prepared.CanTranscode)
                throw new InvalidOperationException($"O Windows não conseguiu preparar o vídeo ({prepared.FailureReason}).");

            var stop = new CancellationTokenSource();
            var capture = Task.Run(() => CaptureLoop(rect, frames.Writer, pool, stop.Token));
            var encode = prepared.TranscodeAsync().AsTask();
            _stop = stop;
            _session = Finish(path, output, capture, encode, stop);
            StateChanged?.Invoke(true);
        }
        catch
        {
            output?.Dispose();
            DeleteQuietly(path);
            throw;
        }
    }

    private static void CaptureLoop(PixelRect rect, ChannelWriter<Frame> writer, ConcurrentBag<byte[]> pool, CancellationToken stop)
    {
        try
        {
            using var grabber = new ScreenGrabber(rect, includeLayered: false);
            var clock = Stopwatch.StartNew();
            var interval = TimeSpan.FromSeconds(1.0 / Fps);
            var next = TimeSpan.Zero;
            var bgra = new byte[rect.Width * rect.Height * 4];
            var size = Core.VideoFrames.Nv12Size(rect.Width, rect.Height);
            while (!stop.IsCancellationRequested)
            {
                grabber.Grab(bgra, drawCursor: true);
                // Reaproveita os buffers já codificados: alocar um por quadro sobrecarrega a memória.
                var frame = pool.TryTake(out var reused) ? reused : new byte[size];
                Core.VideoFrames.BgraToNv12(bgra, rect.Width, rect.Height, frame);
                writer.TryWrite(new Frame(frame, clock.Elapsed));
                next += interval;
                var wait = next - clock.Elapsed;
                if (wait > TimeSpan.Zero) stop.WaitHandle.WaitOne(wait);
                else next = clock.Elapsed;
            }
        }
        finally { writer.TryComplete(); }
    }

    private async Task Finish(string path, IDisposable output, Task capture, Task encode, CancellationTokenSource stop)
    {
        Exception? error = null;
        // Se o codificador falhar no meio, a captura para também (e vice-versa).
        var first = await Task.WhenAny(capture, encode);
        if (first.IsFaulted) stop.Cancel();
        try { await capture; } catch (Exception e) { error ??= e; }
        try { await encode; } catch (Exception e) { error ??= e; }
        output.Dispose();
        if (error is not null) DeleteQuietly(path);
        _session = null;
        _stop = null;
        StateChanged?.Invoke(false);
        AppHost.Instance.OnRecordingFinished(path, error);
    }

    public async Task StopAsync()
    {
        if (_session is not { } session) return;
        _stop?.Cancel();
        await session;
    }

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
