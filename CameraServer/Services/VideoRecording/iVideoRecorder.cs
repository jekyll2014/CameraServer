using OpenCvSharp;

namespace CameraServer.Server.Services.VideoRecording;

public interface IVideoRecorder
{
    public void SaveFrame(Mat? frame);
    public void Stop();
}