using Emgu.CV;

namespace CameraServer.Services.VideoRecording;

public interface IVideoRecorder
{
    public void SaveFrame(Mat? frame);
    public void Stop();
}