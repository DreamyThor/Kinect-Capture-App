using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace KinectCaptureApp.Services
{
    public class RecordingService
    {
        private Process _rgbProcess;
        private Process _irProcess;
        private Stream _rgbStdin;
        private Stream _irStdin;

        private bool _isRecording = false;
        private string _currentRgbPath;
        private string _currentIrPath;

        public bool IsRecording => _isRecording;

        // ── Start recording ───────────────────────────────────────────────────
        public void Start(string recordingPath, string patientId)
        {
            if (_isRecording) return;

            Directory.CreateDirectory(recordingPath);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _currentRgbPath = Path.Combine(recordingPath, $"RGB_{patientId}_{timestamp}.mp4");
            _currentIrPath = Path.Combine(recordingPath, $"IR_{patientId}_{timestamp}.mp4");

            _rgbProcess = StartFfmpegProcess(1920, 1080, 8, _currentRgbPath);
            _irProcess = StartFfmpegProcess(512, 424, 15, _currentIrPath);

            _rgbStdin = _rgbProcess.StandardInput.BaseStream;
            _irStdin = _irProcess.StandardInput.BaseStream;

            _isRecording = true;
            Console.WriteLine($"[Recording] Started → {_currentRgbPath}");
            Console.WriteLine($"[Recording] Started → {_currentIrPath}");
        }

        // ── Stop recording ────────────────────────────────────────────────────
        public void Stop()
        {
            if (!_isRecording) return;
            _isRecording = false;

            CloseProcess(_rgbStdin, _rgbProcess, "RGB");
            CloseProcess(_irStdin, _irProcess, "IR");

            _rgbStdin = null; _rgbProcess = null;
            _irStdin = null; _irProcess = null;
        }

        // ── Feed an RGB (BGRA 1920×1080) frame ────────────────────────────────
        public void AddRgbFrame(byte[] bgraData, int width, int height)
        {
            if (!_isRecording || _rgbStdin == null) return;
            try
            {
                var bgr = BgraToBgr24(bgraData);
                _rgbStdin.Write(bgr, 0, bgr.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Recording] RGB frame error: {ex.Message}");
            }
        }

        // ── Feed an IR (BGRA 512×424) frame ───────────────────────────────────
        public void AddIrFrame(byte[] bgraData, int width, int height)
        {
            if (!_isRecording || _irStdin == null) return;
            try
            {
                var bgr = BgraToBgr24(bgraData);
                _irStdin.Write(bgr, 0, bgr.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Recording] IR frame error: {ex.Message}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Process StartFfmpegProcess(int width, int height, int fps, string outputPath)
        {
            // Find ffmpeg.exe next to the running executable
            string ffmpegPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

            string args =
                $"-f rawvideo " +
                $"-pixel_format bgr24 " +
                $"-video_size {width}x{height} " +
                $"-framerate {fps} " +
                $"-i pipe:0 " +
                $"-c:v libx264 " +
                $"-preset fast " +
                $"-crf 23 " +
                $"-pix_fmt yuv420p " +
                $"-movflags +faststart " +
                $"-y \"{outputPath}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            // Log FFMpeg output to console for debugging
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[FFMpeg] {e.Data}");
            };

            process.Start();
            process.BeginErrorReadLine();
            return process;
        }

        private static void CloseProcess(Stream stdin, Process process, string label)
        {
            try
            {
                stdin?.Close();
                process?.WaitForExit(5000); // give FFMpeg 5s to finalize
                Console.WriteLine($"[Recording] {label} finalized");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Recording] {label} close error: {ex.Message}");
            }
        }

        // Strip alpha — FFMpeg expects BGR24 (3 bytes per pixel)
        private static byte[] BgraToBgr24(byte[] bgra)
        {
            int pixelCount = bgra.Length / 4;
            var bgr = new byte[pixelCount * 3];
            for (int i = 0; i < pixelCount; i++)
            {
                bgr[i * 3] = bgra[i * 4];     // B
                bgr[i * 3 + 1] = bgra[i * 4 + 1]; // G
                bgr[i * 3 + 2] = bgra[i * 4 + 2]; // R
            }
            return bgr;
        }
    }
}