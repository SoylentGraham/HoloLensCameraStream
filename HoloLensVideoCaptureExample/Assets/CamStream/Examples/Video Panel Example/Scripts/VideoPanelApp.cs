using UnityEngine;
using HoloLensCameraStream;
using UnityEngine.Events;


[System.Serializable]
public class UnityEvent_ImageWidthHeight : UnityEvent <byte[],TextureFormat,int,int> {}


public class BytePool
{
	static byte[]	Buffer;

	public static byte[]	Alloc(int Size)
	{
		if ( Buffer != null )
			if ( Buffer.Length != Size )
				Buffer = null;

		if ( Buffer == null )
			Buffer = new byte[Size];

		return Buffer;
	}

	public static void		Release(byte[] ReleasedBuffer)
	{

	}	
};


/// <summary>
/// This example gets the video frames at 30 fps and displays them on a Unity texture,
/// which is locked the User's gaze.
/// </summary>
public class VideoPanelApp : MonoBehaviour
{
	public UnityEvent_ImageWidthHeight		OnNewFrameMonoThread;
	public UnityEvent_ImageWidthHeight		OnNewFrame;
	public UnityEvent						OnError;

    HoloLensCameraStream.Resolution _resolution;
	
    VideoCapture _videoCapture;

    void Start()
    {
        //Call this in Start() to ensure that the CameraStreamHelper is already "Awake".
		try
		{
	        CameraStreamHelper.Instance.GetVideoCaptureAsync(OnVideoCaptureCreated);
			//You could also do this "shortcut":
			//CameraStreamManager.Instance.GetVideoCaptureAsync(v => videoCapture = v);
		}
		catch(System.Exception e)
		{
			Debug.LogException(e);
			OnError.Invoke();
		}
    }

    private void OnDestroy()
    {
        if (_videoCapture != null)
        {
            _videoCapture.FrameSampleAcquired -= OnFrameSampleAcquired;
            _videoCapture.Dispose();
        }
    }

    void OnVideoCaptureCreated(VideoCapture videoCapture)
    {
        if (videoCapture == null)
        {
            Debug.LogError("Did not find a video capture object. You may not be using the HoloLens.");
			OnError.Invoke();
            return;
        }
        
        this._videoCapture = videoCapture;

        _resolution = CameraStreamHelper.Instance.GetLowestResolution();
        float frameRate = CameraStreamHelper.Instance.GetHighestFrameRate(_resolution);
        videoCapture.FrameSampleAcquired += OnFrameSampleAcquired;

        CameraParameters cameraParams = new CameraParameters();
        cameraParams.cameraResolutionHeight = _resolution.height;
        cameraParams.cameraResolutionWidth = _resolution.width;
        cameraParams.frameRate = Mathf.RoundToInt(frameRate);
        cameraParams.pixelFormat = CapturePixelFormat.BGRA32;

		cameraParams.enableHolograms = true;
		cameraParams.enableRecordingIndicator = false;
		cameraParams.enableVideoStabilization = false;

       // UnityEngine.WSA.Application.InvokeOnAppThread(() => { _videoPanelUI.SetResolution(_resolution.width, _resolution.height); }, false);

        videoCapture.StartVideoModeAsync(cameraParams, OnVideoModeStarted);
    }

    void OnVideoModeStarted(VideoCaptureResult result)
    {
        if (result.success == false)
        {
            Debug.LogWarning("Could not start video mode.");
			OnError.Invoke();
            return;
        }

        Debug.Log("Video capture started.");
    }

    void OnFrameSampleAcquired(VideoCaptureSample sample)
    {
		var Bytes = BytePool.Alloc( sample.dataLength );
        sample.CopyRawImageDataIntoBuffer(Bytes);
        sample.Dispose();

		//	immediate callback
		OnNewFrame.Invoke( Bytes, TextureFormat.BGRA32, _resolution.width, _resolution.height );
 
		//	unity-happy callback
        UnityEngine.WSA.Application.InvokeOnAppThread(() =>
        {
			OnNewFrameMonoThread.Invoke( Bytes, TextureFormat.BGRA32, _resolution.width, _resolution.height );
        }, false);
    }
}
