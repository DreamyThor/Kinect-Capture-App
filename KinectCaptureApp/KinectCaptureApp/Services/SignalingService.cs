using SocketIOClient;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using KinectCaptureApp.Models;
using SocketIOClient.Common;

namespace KinectCaptureApp.Services
{
    public class SignalingService
    {
        private readonly DeviceConfig _config;
        private SocketIO _socket;

        public SignalingService(DeviceConfig config)
        {
            _config = config;
        }

        public async Task ConnectAsync()
        {
            // Global SSL bypass for .NET Framework — covers both HTTP and WebSocket transports
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;

            _socket = new SocketIO(new Uri(_config.backend_url), new SocketIOOptions
            {
                Transport = TransportProtocol.WebSocket  // skip polling, go straight to WS
            });

            _socket.OnConnected += async (sender, e) =>
            {
                Console.WriteLine("[SIGNALING] Connected to backend!");
                await RegisterDevice();
            };

            _socket.OnDisconnected += (sender, e) =>
                Console.WriteLine("[SIGNALING] Disconnected: " + e);

            _socket.OnError += (sender, e) =>
                Console.WriteLine("[SIGNALING ERROR] " + e);

            await _socket.ConnectAsync();
        }

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