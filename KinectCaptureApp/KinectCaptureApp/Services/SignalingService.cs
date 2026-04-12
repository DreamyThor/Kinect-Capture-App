using SocketIOClient;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Threading.Tasks;
using KinectCaptureApp.Models;
using SocketIOClient.Common;

namespace KinectCaptureApp.Services
{
    public class SignalingService
    {
        private readonly DeviceConfig _config;
        private SocketIO _socket;

        // Caregiver's socket ID — needed to route answer and ICE back
        private string _caregiverSocketId;

        // ── Events raised for MainWindow to wire to WebRtcService ─────────────
        public event Func<string, string, Task> OnOfferReceived;      // (sdp, caregiverSocketId)
        public event Action<string> OnIceCandidateReceived;           // (candidateJson)

        public SignalingService(DeviceConfig config)
        {
            _config = config;
        }

        public async Task ConnectAsync()
        {
            ServicePointManager.ServerCertificateValidationCallback =
                (sender, cert, chain, errors) => true;

            _socket = new SocketIO(new Uri(_config.backend_url), new SocketIOOptions
            {
                Transport = TransportProtocol.WebSocket
            });

            // ── Lifecycle ─────────────────────────────────────────────────────
            _socket.OnConnected += async (sender, e) =>
            {
                Console.WriteLine("[SIGNALING] Connected to backend!");
                await RegisterDevice();
            };

            _socket.OnDisconnected += (sender, e) =>
                Console.WriteLine("[SIGNALING] Disconnected: " + e);

            _socket.OnError += (sender, e) =>
                Console.WriteLine("[SIGNALING ERROR] " + e);

            // ── WebRTC: incoming offer from caregiver (via backend) ────────────
            _socket.On("webrtc-offer", async ctx =>
            {
                try
                {
                    var data = ctx.GetValue<WebRtcOfferDto>(0);

                    if (string.IsNullOrEmpty(data?.sdp) ||
                        string.IsNullOrEmpty(data?.caregiverSocketId))
                    {
                        Console.WriteLine("[SIGNALING] Invalid webrtc-offer payload");
                        return;
                    }

                    _caregiverSocketId = data.caregiverSocketId;
                    Console.WriteLine($"[SIGNALING] Received webrtc-offer from caregiver {_caregiverSocketId}");

                    if (OnOfferReceived != null)
                    {
                        await OnOfferReceived.Invoke(data.sdp, _caregiverSocketId);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[SIGNALING] webrtc-offer error: " + ex.Message);
                }
            });

            // ── WebRTC: incoming ICE candidate from caregiver ─────────────────
            _socket.On("ice-candidate", ctx =>
            {
                try
                {
                    var data = ctx.GetValue<IceCandidateDto>(0);

                    if (data?.candidate == null)
                    {
                        Console.WriteLine("[SIGNALING] ice-candidate missing candidate");
                        return Task.CompletedTask;
                    }

                    string candidateJson = JsonConvert.SerializeObject(data.candidate);
                    Console.WriteLine("[SIGNALING] Received ICE candidate from caregiver");

                    OnIceCandidateReceived?.Invoke(candidateJson);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[SIGNALING] ice-candidate error: " + ex.Message);
                }
                return Task.CompletedTask;
            });

            await _socket.ConnectAsync();
        }

        // ── Send SDP answer back to caregiver ─────────────────────────────────
        public async Task SendAnswerAsync(string caregiverSocketId, string sdp)
        {
            await _socket.EmitAsync("webrtc-answer", new object[]
            {
                caregiverSocketId,
                sdp
            });
            Console.WriteLine("[SIGNALING] Answer sent to caregiver " + caregiverSocketId);
        }

        // ── Send our ICE candidates to the caregiver ──────────────────────────
        public async Task SendIceCandidateAsync(string caregiverSocketId, string candidateJson)
        {
            object candidate;
            try
            {
                candidate = JsonConvert.DeserializeObject(candidateJson);
            }
            catch
            {
                candidate = candidateJson;
            }

            await _socket.EmitAsync("ice-candidate", new object[]
            {
        new
        {
            targetSocketId = caregiverSocketId,
            candidate
        }
            });

            Console.WriteLine("[SIGNALING] ICE candidate sent to caregiver " + caregiverSocketId);
        }

        // ── Register device on connect ─────────────────────────────────────────
        private async Task RegisterDevice()
        {
            Console.WriteLine("[SIGNALING] Sending register-device...");
            try
            {
                await _socket.EmitAsync(
                    "register-device",
                    new object[] { _config.device_id, _config.patient_id, _config.room });
                Console.WriteLine("[SIGNALING] register-device sent successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SIGNALING ERROR] " + ex.Message);
            }
        }
    }
}