using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KinectCaptureApp.Services
{
    public class WebRtcService
    {
        // ── Events ────────────────────────────────────────────────────────────
        public event Func<string, Task> OnAnswerReady;         // SDP answer string
        public event Action<string> OnIceCandidateReady;   // candidate JSON string

        // ── Internals ─────────────────────────────────────────────────────────
        private RTCPeerConnection _pc;
        private VideoEncoderEndPoint _encoder;
        private uint _timestamp = 0;

        // RTP clock for VP8 is 90 kHz; sending at ~15 fps
        private const uint TIMESTAMP_INCREMENT = 90000 / 15;

        // Scale Kinect 1920x1080 down — VP8 encoding at full res is too heavy
        private const int TARGET_WIDTH = 640;
        private const int TARGET_HEIGHT = 360;

        // ── Handle incoming SDP offer ─────────────────────────────────────────
        public async Task HandleOfferAsync(string sdpOffer)
        {
            Console.WriteLine("[WebRTC] Handling offer...");

            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                }
            };

            _pc = new RTCPeerConnection(config);
            _encoder = new VideoEncoderEndPoint();

            // Use static SupportedFormats
            var track = new MediaStreamTrack(
                VideoEncoderEndPoint.SupportedFormats,
                MediaStreamStatusEnum.SendOnly);

            _pc.addTrack(track);

            // Forward encoded samples to the peer connection
            _encoder.OnVideoSourceEncodedSample += (uint duration, byte[] sample) =>
            {
                _timestamp += duration;
                _pc.SendVideo(_timestamp, sample);
            };

            // Forward ICE candidates to caregiver via SignalingService
            _pc.onicecandidate += (candidate) =>
            {
                if (candidate != null)
                {
                    Console.WriteLine("[WebRTC] ICE candidate ready");
                    OnIceCandidateReady?.Invoke(candidate.toJSON());
                }
            };

            _pc.onconnectionstatechange += (state) =>
                Console.WriteLine($"[WebRTC] Connection state: {state}");

            _pc.oniceconnectionstatechange += (state) =>
                Console.WriteLine($"[WebRTC] ICE connection state: {state}");

            // Apply the caregiver's offer
            var setResult = _pc.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = sdpOffer
            });

            if (setResult != SetDescriptionResultEnum.OK)
            {
                Console.WriteLine($"[WebRTC] setRemoteDescription failed: {setResult}");
                return;
            }

            // Create and apply our answer
            var answer = _pc.createAnswer();
            await _pc.setLocalDescription(answer);

            Console.WriteLine("[WebRTC] Answer created");
            OnAnswerReady?.Invoke(answer.sdp);
        }

        // ── Add ICE candidate from caregiver ─────────────────────────────────
        public void AddIceCandidate(string candidateJson)
        {
            if (_pc == null) return;
            try
            {
                var init = JsonConvert.DeserializeObject<RTCIceCandidateInit>(candidateJson);
                _pc.addIceCandidate(init);
                Console.WriteLine("[WebRTC] ICE candidate added");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebRTC] addIceCandidate error: {ex.Message}");
            }
        }

        // ── Send a Kinect color frame as VP8 ─────────────────────────────────
        // Called from MainWindow every other color frame (~15fps)
        public void SendFrame(byte[] bgraData, int srcWidth, int srcHeight)
        {
            if (_pc == null || _pc.connectionState != RTCPeerConnectionState.connected)
                return;

            try
            {
                // 1. Scale the frame
                var scaled = ScaleBgra(bgraData, srcWidth, srcHeight, TARGET_WIDTH, TARGET_HEIGHT);

                // 2. Convert BGRA → I420
                var i420 = BgraToI420(scaled, TARGET_WIDTH, TARGET_HEIGHT);

                // 3. Push the raw frame to the encoder
                _encoder.ExternalVideoSourceRawSample(
                    (uint)(1000 / 15),                // duration in ms for ~15 FPS
                    TARGET_WIDTH,
                    TARGET_HEIGHT,
                    i420,
                    VideoPixelFormatsEnum.I420);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebRTC] SendFrame error: {ex.Message}");
            }
        }

        public void Close()
        {
            try { _pc?.Close("bye"); } catch { }
            _pc = null;
            _encoder = null;
            Console.WriteLine("[WebRTC] Closed");
        }

        // ── Nearest-neighbour BGRA scale ─────────────────────────────────────
        private static byte[] ScaleBgra(byte[] src, int srcW, int srcH, int dstW, int dstH)
        {
            var dst = new byte[dstW * dstH * 4];
            float xRatio = (float)srcW / dstW;
            float yRatio = (float)srcH / dstH;

            for (int y = 0; y < dstH; y++)
            {
                int srcY = (int)(y * yRatio);
                for (int x = 0; x < dstW; x++)
                {
                    int srcX = (int)(x * xRatio);
                    int srcIdx = (srcY * srcW + srcX) * 4;
                    int dstIdx = (y * dstW + x) * 4;
                    dst[dstIdx] = src[srcIdx];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
            return dst;
        }

        // ── BGRA → I420 (BT.601) ─────────────────────────────────────────────
        private static byte[] BgraToI420(byte[] bgra, int width, int height)
        {
            int frameSize = width * height;
            var i420 = new byte[frameSize * 3 / 2];
            int yIdx = 0;
            int uIdx = frameSize;
            int vIdx = frameSize + frameSize / 4;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int idx = (row * width + col) * 4;
                    byte b = bgra[idx];
                    byte g = bgra[idx + 1];
                    byte r = bgra[idx + 2];

                    i420[yIdx++] = (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

                    if (row % 2 == 0 && col % 2 == 0)
                    {
                        i420[uIdx++] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                        i420[vIdx++] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
                    }
                }
            }
            return i420;
        }
    }
}