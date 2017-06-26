using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;
using WebSocketSharp;

using UnityEngine.Events;



[System.Serializable]
public class JsonCommand{
	public string	Command;

	public JsonCommand(string _Command){
		Command = _Command;
	}
};

[System.Serializable]
public class JsonCommand_Play : JsonCommand{
	public static string	CommandString = "Play";
	public string	Filename;

	public JsonCommand_Play() : base	( CommandString)	{}
};

[System.Serializable]
public class JsonCommand_Stop : JsonCommand{
	public static string	CommandString = "Stop";

	public JsonCommand_Stop() : base	( CommandString)	{}
};

[System.Serializable]
public class JsonCommand_SetStatus : JsonCommand{
	public static string	CommandString = "SetStatus";
	public string			Status;

	public JsonCommand_SetStatus() : base	( CommandString)	{}
};

[System.Serializable]
public class JsonCommand_Ping : JsonCommand{
	public static string	CommandString = "Ping";

	public JsonCommand_Ping() : base	( CommandString)	{}
};



public class SendWebsocketFrame : MonoBehaviour {

	class ByteFrame
	{
		public byte[]			Bytes;
		public int				Width;
		public int				Height;
		public TextureFormat	Format;

		public ByteFrame(byte[] _Bytes, int _Width, int _Height, TextureFormat _Format)
		{
			Bytes = BytePool.Global.Alloc(_Bytes);
			Width = _Width;
			Height = _Height;
			Format = _Format;
		}

		public void Dispose()
		{
			BytePool.Global.Release( Bytes );
			Bytes = null;
		}

	}

	public bool	IsServer = true;

	WebSocket	Socket;
	bool		SocketConnecting = false;

	Texture2D	ImageTexture;


	bool		DebugUpdate = false;
	
	public string[] _hosts;
	private int			_currentHostIndex = -1;
		

	[Range(0,10)]
	public float	RetryTimeSecs = 5;
	private float	RetryTimeout = 1;

	public UnityEvent_String	OnPlayFilename;
	public UnityEvent			OnStop;

	public UnityEngine.UI.Text	StatusText;

	//	websocket commands come on a different thread, so queue them for the next update
	public List<System.Action>	JobQueue;

	[Range(1,10)]
	public float	PingTimeSecs = 10;
	private float	PingTimeout = 1;

	List<ByteFrame>	EncodeQueue;
	int				EncodingCount = 0;
	int				MaxEncoding = 4;
	List<byte[]>	JpegQueue;
	int				JpegSendingCount = 0;
	int				MaxJpegSending = 4;

    public void setHost(string host) {
        _hosts = new string[1]{ host };
    }

	void SetStatus(string Status){
		if ( StatusText != null ){
			StatusText.text = Status + "\n";
		}
		else
			Debug.Log("Websocket: " + Status);
	}
		

	string GetCurrentHost(){
		if (_hosts == null || _hosts.Length == 0) return null;

		return _hosts[_currentHostIndex];
	}

	void Start()
	{
		if ( JpegQueue == null )
			JpegQueue = new List<byte[]>();
		if ( EncodeQueue == null )
			EncodeQueue = new List<ByteFrame>();

	}

	void Connect(){
	
		if ( Socket != null )
			return;

		if ( SocketConnecting )
			return;

		if (_hosts == null || _hosts.Length == 0) {
			SetStatus("No hosts specified");
			return;
		}

        _currentHostIndex++;
        if(_currentHostIndex >= _hosts.Length) _currentHostIndex = 0;
		
		var Host = GetCurrentHost();
		SetStatus("Connecting to " + Host + "...");

        Debug.Log("Trying to connect to: " + Host );
		
		var NewSocket = new WebSocket("ws://" + Host);
		SocketConnecting = true;
		//NewSocket.Log.Level = LogLevel.TRACE;

		NewSocket.OnOpen += (sender, e) => {
            QueueJob (() => {
				Socket = NewSocket;
				OnConnected();
			});
		};

		NewSocket.OnError += (sender, e) => {
            QueueJob (() => {
				OnError( e.Message, true );
			});
		};

		NewSocket.OnClose += (sender, e) => {
			SocketConnecting = false;
			/*
			if ( LastConnectedHost != null ){
				QueueJob (() => {
					SetStatus("Disconnected from " + LastConnectedHost );
				});
			}
			*/
			OnError( "Closed", true);
		};

		NewSocket.OnMessage += (sender, e) => {

			if ( e.Type == Opcode.TEXT )
				OnTextMessage( e.Data );
			else if ( e.Type == Opcode.BINARY )
				OnBinaryMessage( e.RawData );
			else
				OnError("Unknown opcode " + e.Type, false );
		};

		//Socket.Connect ();
        NewSocket.ConnectAsync ();
		
	}

	void Update(){


		/*
		if (Socket != null && !Socket.IsAlive) {
			OnError ("Socket not alive");
			Socket.Close ();
			Socket = null;
		}
*/
		if (Socket == null ) {

			if (RetryTimeout <= 0) {
				Connect ();
				RetryTimeout = RetryTimeSecs;
			} else {
				RetryTimeout -= Time.deltaTime;
			}
		}
	
		//	commands to execute from other thread
		if (JobQueue != null) {
			while (JobQueue.Count > 0) {

				if ( DebugUpdate )
					Debug.Log("Executing job 0/" + JobQueue.Count);
				var Job = JobQueue [0];
				JobQueue.RemoveAt (0);
				try
				{
					Job.Invoke ();
					if ( DebugUpdate )
						Debug.Log("Job Done.");
				}
				catch(System.Exception e)
				{
					Debug.Log("Job invoke exception: " + e.Message );
				}
			}
		}




		try
		{
			//while ( EncodeQueue.Count > 0 )
			for ( int i=0;	i<MaxEncoding;	i++ )
				SendNextEncode();
			ClearEncodingQueue();
		}
		catch(System.Exception e)
		{
			Debug.Log("SendNextEncode exception: " + e.Message );
		}


		try
		{
			//while ( JpegQueue.Count > 0 )
			for ( int i=0;	i<MaxJpegSending;	i++ )
				SendNextJpeg();
			ClearJpegQueue();
		}
		catch(System.Exception e)
		{
			Debug.Log("SendNextJpeg exception: " + e.Message );
		}
	}

	void QueueJpeg(byte[] Jpeg)
	{
		lock (JpegQueue)
		{
			JpegQueue.Add(Jpeg);
		}
	}


	void ClearJpegQueue()
	{
		lock(JpegQueue)
		{
			JpegQueue.Clear();
		}
	}

	void ClearEncodingQueue()
	{
		lock(EncodeQueue)
		{
			EncodeQueue.Clear();
		}
	}

	void SendNextJpeg()
	{
		if ( JpegSendingCount >= MaxJpegSending )
			return;

		lock (JpegQueue)
		{
			if ( Socket == null )
				JpegQueue.Clear();

			if ( JpegQueue.Count == 0 )
				return;
		}

		byte[] Jpeg = null;
		lock(JpegQueue)
		{
			Jpeg = JpegQueue[0];
			JpegQueue.RemoveAt(0);
		};

		if ( DebugUpdate )
			Debug.Log("sending jpeg x" + Jpeg.Length);

		Interlocked.Increment( ref JpegSendingCount );
		Socket.SendAsync( Jpeg, (Completed)=> {
			Interlocked.Decrement( ref JpegSendingCount );
		} );

		if ( DebugUpdate )
			Debug.Log("Done send async jpeg x" + Jpeg.Length);
	}

	void QueueEncode(ByteFrame Frame)
	{
		lock (EncodeQueue)
		{
			EncodeQueue.Add(Frame);
		}
	}


	void DoEncode(ByteFrame Frame)
	{		
		//	do encode
		bool SendTestJpeg = false;
#if UNITY_EDITOR
		bool EncodeWithPopEncode = false;
#else
		bool EncodeWithPopEncode = true;
#endif
		bool EncodeOnMonoThread = false;
		bool EncodeViaParallelTask = false;
		bool EncodeViaThreadPool = true;

		if (SendTestJpeg)
		{
			Interlocked.Increment( ref EncodingCount );
			QueueJpeg( jpeg2x2 );
			Interlocked.Decrement( ref EncodingCount );
		}
		else if ( EncodeWithPopEncode )
		{
			//	gr: executing this on the thread made the display disapear?? but it still worked at a decent frame rate (watching on viewer)
			System.Action EncodeJpegAndSend = () =>
			{
				System.Exception PopEncodeJpegException = null;
				System.Exception LoadRawJpegException = null;
				try
				{
					var Jpeg = PopEncodeJpeg.EncodeToJpeg( Frame.Bytes, Frame.Width, Frame.Height, 4, true );
					Frame.Dispose();
					QueueJpeg( Jpeg );
					Interlocked.Decrement( ref EncodingCount );
					return;
				}
				catch(System.Exception e)
				{
					PopEncodeJpegException = e;
				}

				try
				{
					//	can only use these on main thread
					QueueJob( ()=>
					{
						if ( ImageTexture == null )
							ImageTexture = new Texture2D( Frame.Width, Frame.Height, Frame.Format, false );
		
						Interlocked.Increment( ref EncodingCount );
						ImageTexture.LoadRawTextureData(Frame.Bytes);
						Frame.Dispose();
						//var Jpeg = ImageTexture.EncodeToJPG(50);
						var Jpeg = ImageTexture.EncodeToPNG();
						QueueJpeg( Jpeg );
						Interlocked.Decrement( ref EncodingCount );
						return;
					} );
				}
				catch(System.Exception e)
				{
					LoadRawJpegException = e;
				}

				Debug.LogError("Failed to encode jpeg; " + PopEncodeJpegException.Message + ", then " + LoadRawJpegException.Message );
				Frame.Dispose();
			};

			if ( EncodeOnMonoThread )
			{
				Interlocked.Increment( ref EncodingCount );
				QueueJob( EncodeJpegAndSend );
				Interlocked.Decrement( ref EncodingCount );
			}
#if WINDOWS_UWP
			else if ( EncodeViaParallelTask )
			{
				Interlocked.Increment( ref EncodingCount );
				System.Threading.Tasks.Parallel.Invoke( EncodeJpegAndSend );
			}
#endif
			else if ( EncodeViaThreadPool )
			{
				Interlocked.Increment( ref EncodingCount );
#if WINDOWS_UWP
				Windows.System.Threading.ThreadPool.RunAsync( (workitem)=> {	EncodeJpegAndSend(); } );
#else
				System.Threading.ThreadPool.QueueUserWorkItem( (workitem)=> {	EncodeJpegAndSend(); } );
#endif
			}
			else
			{
				Frame.Dispose();
				//	dunno how to encode
				throw new System.Exception("No method of encoding picked");
			}
		}
		else
		{
			Interlocked.Increment( ref EncodingCount );
			//	these go wrong on hololens and crash the app
			if ( ImageTexture == null )
			{
				ImageTexture = new Texture2D( Frame.Width, Frame.Height, Frame.Format, false );
			}
			ImageTexture.LoadRawTextureData(Frame.Bytes);
			Frame.Dispose();
			//var Jpeg = ImageTexture.EncodeToJPG(50);
			var Jpeg = ImageTexture.EncodeToPNG();
			QueueJpeg( Jpeg );
			Interlocked.Decrement( ref EncodingCount );
		}
	}


	void SendNextEncode()
	{
		if ( EncodingCount >= MaxEncoding )
			return;

		lock (EncodeQueue)
		{
			if ( Socket == null )
			{
				foreach( var f in EncodeQueue )
					f.Dispose();
				EncodeQueue.Clear();
			}

			if ( EncodeQueue.Count == 0 )
				return;
		}

		ByteFrame Frame = null;
		lock(EncodeQueue)
		{
			Frame = EncodeQueue[0];
			EncodeQueue.RemoveAt(0);
		};
	
		DoEncode( Frame );
	}


	void RegisterClient(){
		SetStatus ("Registering client");
		if ( Socket == null )
			Debug.LogWarning("Registering client - null socket");
		else
			Socket.Send ("iamclient");
	}

	void RegisterServer(){
		SetStatus ("Registering server");
		if ( Socket == null )
			Debug.LogWarning("Registering server - null socket");
		else
			Socket.Send ("iamserver");
	}

	void OnConnected(){
		SetStatus ("Connected");

		if ( IsServer )
			RegisterServer();
		else
			RegisterClient ();
	}

	void OnTextMessage(string Message){
		Debug.Log ("Message: " + Message);
	}

	void OnBinaryMessage(byte[] Message){
		SetStatus("Binary Message: " + Message.Length + " bytes");
	}

	void OnError(string Message,bool Close){
		//SetStatus("Error: " + Message );
		Debug.Log("Error: " + Message );

		if (Close) {
			if (Socket != null) {

				//	recurses if we came here from on close
				if ( Socket.IsAlive )
					Socket.Close ();
				Socket = null;
				SocketConnecting = false;
			}
		}
	}


	void OnApplicationQuit(){
		
		if (Socket != null) {
			//	if (Socket.IsAlive)
			Socket.Close ();
		}
		
	}

	void QueueJob(System.Action Job){
		if (JobQueue == null)
			JobQueue = new List<System.Action> ();
		JobQueue.Add( Job );
	}
	
 static byte[] jpeg2x2 = {
    0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00, 0x01, 
    0x01, 0x01, 0x00, 0x60, 0x00, 0x60, 0x00, 0x00, 0xff, 0xdb, 0x00, 0x43, 
    0x00, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01, 0x02, 0x02, 0x02, 0x02, 0x02, 
    0x02, 0x02, 0x02, 0x03, 0x05, 0x03, 0x03, 0x03, 0x03, 0x03, 0x06, 0x04, 
    0x04, 0x03, 0x05, 0x07, 0x06, 0x07, 0x07, 0x07, 0x06, 0x07, 0x07, 0x08, 
    0x09, 0x0b, 0x09, 0x08, 0x08, 0x0a, 0x08, 0x07, 0x07, 0x0a, 0x0d, 0x0a, 
    0x0a, 0x0b, 0x0c, 0x0c, 0x0c, 0x0c, 0x07, 0x09, 0x0e, 0x0f, 0x0d, 0x0c, 
    0x0e, 0x0b, 0x0c, 0x0c, 0x0c, 0xff, 0xdb, 0x00, 0x43, 0x01, 0x02, 0x02, 
    0x02, 0x03, 0x03, 0x03, 0x06, 0x03, 0x03, 0x06, 0x0c, 0x08, 0x07, 0x08, 
    0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 
    0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 
    0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 
    0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 
    0x0c, 0x0c, 0xff, 0xc0, 0x00, 0x11, 0x08, 0x00, 0x02, 0x00, 0x02, 0x03, 
    0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01, 0xff, 0xc4, 0x00, 
    0x1f, 0x00, 0x00, 0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 
    0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0xff, 0xc4, 0x00, 0xb5, 0x10, 0x00, 
    0x02, 0x01, 0x03, 0x03, 0x02, 0x04, 0x03, 0x05, 0x05, 0x04, 0x04, 0x00, 
    0x00, 0x01, 0x7d, 0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 
    0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 
    0x91, 0xa1, 0x08, 0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0, 0x24, 
    0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 
    0x26, 0x27, 0x28, 0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 
    0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 
    0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 
    0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 
    0x87, 0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 
    0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 
    0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 
    0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 
    0xda, 0xe1, 0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 
    0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa, 0xff, 0xc4, 0x00, 
    0x1f, 0x01, 0x00, 0x03, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 
    0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 
    0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0xff, 0xc4, 0x00, 0xb5, 0x11, 0x00, 
    0x02, 0x01, 0x02, 0x04, 0x04, 0x03, 0x04, 0x07, 0x05, 0x04, 0x04, 0x00, 
    0x01, 0x02, 0x77, 0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 
    0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71, 0x13, 0x22, 0x32, 0x81, 0x08, 
    0x14, 0x42, 0x91, 0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0, 0x15, 
    0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 
    0x19, 0x1a, 0x26, 0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 
    0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x53, 0x54, 0x55, 
    0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 
    0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 
    0x85, 0x86, 0x87, 0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 
    0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 
    0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 
    0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 
    0xd8, 0xd9, 0xda, 0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 
    0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa, 0xff, 0xda, 0x00, 
    0x0c, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3f, 0x00, 0xc7, 
    0xa2, 0x8a, 0x2b, 0xfb, 0xe0, 0xff, 0x00, 0x28, 0xcf, 0xff, 0xd9
};

	public void SendBytes(byte[] Image,TextureFormat Format,int Width,int Height)
    {
		Connect();
		
		if ( Socket != null )
			Socket.SendAsync("New image " + Width + "x" + Height + " format=" + Format + " JpegQueueSize:" + JpegQueue.Count + "(" + JpegSendingCount + ") EncodeQueueSize:" + EncodeQueue.Count + "(" + EncodingCount + ")", (s)=> { } );

		QueueEncode( new ByteFrame(Image,Width,Height,Format ) );
		
		
    }

	

}
