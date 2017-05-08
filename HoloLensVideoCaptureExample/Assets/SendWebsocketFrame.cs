using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;
using UnityEngine.Events;


[System.Serializable]
public class UnityEvent_String : UnityEvent <string> {}


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

	public bool	IsServer = true;

	WebSocket	Socket;

	Texture2D	ImageTexture;



	
	public string[] _hosts;
	private int			_currentHostIndex = -1;
		
	string LastConnectedHost;

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


	void Connect(){

		if ( Socket != null )
			return;

		if (_hosts == null || _hosts.Length == 0) {
			SetStatus("No hosts specified");
			return;
		}

        _currentHostIndex++;
        if(_currentHostIndex >= _hosts.Length) _currentHostIndex = 0;
		
		var Host = GetCurrentHost();
		SetStatus("Connecting to " + Host + "...");
		LastConnectedHost = null;

        Debug.Log("Trying to connect to: " + Host );

		Socket = new WebSocket("ws://" + Host);
        Socket.Log.Level = LogLevel.TRACE;

		Socket.OnOpen += (sender, e) => {
            QueueJob (() => {
				OnConnected();
			});
		};

		Socket.OnError += (sender, e) => {
            QueueJob (() => {
				OnError( e.Message, true );
			});
		};

		Socket.OnClose += (sender, e) => {
			if ( LastConnectedHost != null ){
				QueueJob (() => {
					SetStatus("Disconnected from " + LastConnectedHost );
				});
			}

			OnError( "Closed", true);
		};

		Socket.OnMessage += (sender, e) => {

			if ( e.Type == Opcode.TEXT )
				OnTextMessage( e.Data );
			else if ( e.Type == Opcode.BINARY )
				OnBinaryMessage( e.RawData );
			else
				OnError("Unknown opcode " + e.Type, false );
		};

		//Socket.Connect ();
        Socket.ConnectAsync ();
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
				var Job = JobQueue [0];
				JobQueue.RemoveAt (0);
				Job.Invoke ();
			}
		}


		//	ping regularly (to trigger disconnect and notify server)
		if (Socket != null) {
        //if (Socket != null && Socket.IsAlive) {
			PingTimeout -= Time.deltaTime;
			if (PingTimeout < 0) {
				var PingJson = JsonUtility.ToJson (new JsonCommand_Ping ());
				Socket.Send (PingJson);
				PingTimeout = PingTimeSecs;
			}
		}
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
		LastConnectedHost = GetCurrentHost ();
		SetStatus ("Connected");

		if ( IsServer )
			RegisterServer();
		else
			RegisterClient ();
	}

	void OnTextMessage(string Message){
		Debug.Log ("Message: " + Message);

		//	try and parse json message
		try{
			JsonCommand Command = JsonUtility.FromJson<JsonCommand>( Message );
			/*
			if ( Command.Command == JsonCommand_Play.CommandString )
			{
				HandleCommand( JsonUtility.FromJson<JsonCommand_Play>( Message ) );
			}
			else if ( Command.Command == JsonCommand_Stop.CommandString )
			{
				HandleCommand( JsonUtility.FromJson<JsonCommand_Stop>( Message ) );
			}
			else if ( Command.Command == JsonCommand_SetStatus.CommandString )
			{
				HandleCommand( JsonUtility.FromJson<JsonCommand_SetStatus>( Message ) );
			}
			else
			*/
			{
				throw new System.Exception("Unhandled command " + Command.Command );
			}
		}catch( System.Exception e ) {
			Debug.LogError ("Error with websocket command: " + e.Message);
		}
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
			}
		}
	}


	void OnApplicationQuit(){
		/*
		if (Socket != null) {
			//	if (Socket.IsAlive)
			Socket.Close ();
		}
		*/
	}

	void QueueJob(System.Action Job){
		if (JobQueue == null)
			JobQueue = new List<System.Action> ();
		JobQueue.Add( Job );
	}


	public void SendBytes(byte[] Image,TextureFormat Format,int Width,int Height)
    {
		if ( ImageTexture == null )
		{
			ImageTexture = new Texture2D( Width, Height, Format, false );
		}

		Connect();
				
        ImageTexture.LoadRawTextureData(Image); 
		var Jpeg = ImageTexture.EncodeToJPG(50);

		//QueueJob( ()=>
		//{
		if ( Socket!= null )
		{
			Socket.SendAsync(Jpeg, (Success)=> { } );
		}
		//});

    }

	

}
