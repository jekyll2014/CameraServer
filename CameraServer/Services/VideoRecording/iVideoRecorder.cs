using Emgu.CV;

namespace CameraServer.Services.VideoRecording;

public interface iVideoRecorder
{
    public void SaveFrame(Mat? frame);
    public void Stop();
}