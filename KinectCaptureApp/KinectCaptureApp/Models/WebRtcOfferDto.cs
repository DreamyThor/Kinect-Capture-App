
namespace KinectCaptureApp.Models
{
    public class WebRtcOfferDto
    {
        public string sdp { get; set; }
        public string caregiverSocketId { get; set; }
    }

    public class IceCandidateDto
    {
        public string targetSocketId { get; set; }
        public object candidate { get; set; }
    }
}