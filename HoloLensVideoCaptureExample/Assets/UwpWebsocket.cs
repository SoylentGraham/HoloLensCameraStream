#if WINDOWS_UWP
#define CUSTOM_WEBSOCKET
#endif
#if CUSTOM_WEBSOCKET

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Windows.Networking.Sockets;
using System.Threading.Tasks;
using Windows.Web;

using System;	//	System.Uri
using System.IO;

using System.Runtime.InteropServices.WindowsRuntime;	//	AsBuffer

//	https://github.com/Microsoft/Windows-universal-samples/blob/master/Samples/WebSocket/cs/Scenario2_Binary.xaml.cs


namespace WebSocketSharp
{
	public enum Opcode
	{
		TEXT,
		BINARY
	};
};

public class WebSocket
{
	StreamWebSocket	socket;
	Uri				url;
	List<byte[]>	SendQueue;

	public WebSocket(string Url)
	{
		url = TryGetUri( Url );
		socket = new StreamWebSocket();
		socket.Closed += OnClosed;
	}
	
	public UnityEvent<int,string>	OnOpen;
	public UnityEvent<int,string>	OnError;
	public UnityEvent<int,string>	OnClose;
	public UnityEvent<int,string>	OnMessage;

	public bool IsAlive
	{
		get
		{
			return true;
		}
	}

	
	public void	Send(string Utf8)
	{
		throw new System.Exception("String needs converting to byte");
	}

	public void	Send(byte[] Data)
	{
		if ( SendQueue == null )
			SendQueue = new List<byte[]>();

		SendQueue.Add( Data );
	}

	public void SendAsync(string data, System.Action<bool> completed)
	{
		Send(data);
	}

	public void SendAsync(byte[] data, System.Action<bool> completed)
	{
		Send(data);
	}
		
	public void ConnectAsync ()
	{
		StartAsync();
	}

	public void Close()
	{
		if (socket != null)
		{
			try
			{
				socket.Close(1000, "Closed due to user request.");
			}
			catch (Exception ex)
			{
				OnError.Invoke(0,ex.Message);
			}
			socket = null;
		}
	}

	void OnClosed(IWebSocket Socket,WebSocketClosedEventArgs Event)
	{
	}

	void OnRecvData(byte[] Data)
	{

	}
	
	static System.Uri TryGetUri(string uriString)
    {
        Uri webSocketUri;
        if (!Uri.TryCreate(uriString.Trim(), UriKind.Absolute, out webSocketUri))
			throw new System.Exception("Error: Invalid URI");
		
        // Fragments are not allowed in WebSocket URIs.
        if (!String.IsNullOrEmpty(webSocketUri.Fragment))
        	throw new System.Exception("Error: URI fragments not supported in WebSocket URIs.");

        // Uri.SchemeName returns the canonicalized scheme name so we can use case-sensitive, ordinal string
        // comparison.
        if ((webSocketUri.Scheme != "ws") && (webSocketUri.Scheme != "wss"))
        	throw new System.Exception("Error: WebSockets only support ws:// and wss:// schemes.");

        return webSocketUri;
    }

	async Task StartAsync()
	{
		/*	
		// If we are connecting to wss:// endpoint, by default, the OS performs validation of
		// the server certificate based on well-known trusted CAs. We can perform additional custom
		// validation if needed.
		if (SecureWebSocketCheckBox.IsChecked == true)
		{
			// WARNING: Only test applications should ignore SSL errors.
			// In real applications, ignoring server certificate errors can lead to Man-In-The-Middle
			// attacks. (Although the connection is secure, the server is not authenticated.)
			// Note that not all certificate validation errors can be ignored.
			// In this case, we are ignoring these errors since the certificate assigned to the localhost
			// URI is self-signed and has subject name = fabrikam.com
			streamWebSocket.Control.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
			streamWebSocket.Control.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);

			// Add event handler to listen to the ServerCustomValidationRequested event. This enables performing
			// custom validation of the server certificate. The event handler must implement the desired
			// custom certificate validation logic.
			streamWebSocket.ServerCustomValidationRequested += OnServerCustomValidationRequested;

			// Certificate validation is meaningful only for secure connections.
			if (server.Scheme != "wss")
			{
				AppendOutputLine("Note: Certificate validation is performed only for the wss: scheme.");
			}
		}
		*/

		try
		{
			await socket.ConnectAsync(url);
		}
		catch (Exception ex) // For debugging
		{
			socket.Dispose();
			socket = null;

			//	gr: do error!
			OnError.Invoke(0,ex.Message);
			return;
		}
		OnOpen.Invoke(0,"");

		// Start a task to continuously read for incoming data
		Task receiving = ReceiveDataAsync(socket);

		// Start a task to continuously write outgoing data
		Task sending = SendDataAsync(socket);
	}

	// Continuously read incoming data. For reading data we'll show how to use activeSocket.InputStream.AsStream()
    // to get a .NET stream. Alternatively you could call readBuffer.AsBuffer() to use IBuffer with
    // activeSocket.InputStream.ReadAsync.
    private async Task ReceiveDataAsync(StreamWebSocket activeSocket)
    {
        Stream readStream = socket.InputStream.AsStreamForRead();
        try
        {
            byte[] readBuffer = new byte[1000];

            while (true)
            {
                if (socket != activeSocket)
                {
                    // Our socket is no longer active. Stop reading.
                    return;
                }

				//	gr: work out where messages split!
                int BytesRead = await readStream.ReadAsync(readBuffer, 0, readBuffer.Length);
				
				// Do something with the data.
				// This sample merely reports that the data was received.
				var PartBuffer = new byte[BytesRead];
				Array.Copy(readBuffer, PartBuffer, BytesRead);
				OnRecvData( PartBuffer );
            }
        }
        catch (Exception ex)
        {
            WebErrorStatus status = WebSocketError.GetStatus(ex.GetBaseException().HResult);

            switch (status)
            {
                case WebErrorStatus.OperationCanceled:
					OnError.Invoke(0,"Background read canceled.");
                    break;

                default:
					OnError.Invoke(0,"Read error: " + status + "; " + ex.Message);
                    break;
            }
        }
    }

	private async Task SendDataAsync(StreamWebSocket activeSocket)
	{
		try
		{
			// Send until the socket gets closed/stopped
			while (true)
			{
				if (socket != activeSocket)
				{
					// Our socket is no longer active. Stop sending.
					return;
				}

				if ( SendQueue == null || SendQueue.Count == 0 )
				{
					//	sleep thread plx
					await Task.Delay( TimeSpan.FromMilliseconds(500) );
					continue;
				}

				byte[] SendBuffer = null;
				lock(SendQueue)
				{
					SendBuffer = SendQueue[0];
					SendQueue.RemoveAt(0);
				}
				await activeSocket.OutputStream.WriteAsync( SendBuffer.AsBuffer() );
			}
		}
		catch (Exception ex)
		{
			WebErrorStatus status = WebSocketError.GetStatus(ex.GetBaseException().HResult);

			switch (status)
			{
				case WebErrorStatus.OperationCanceled:
					OnError.Invoke( 0, "Background write canceled.");
					break;

				default:
					OnError.Invoke( 0, "Error " + status + " exception: " + ex.Message );
					break;
			}
		}
	}
};



#endif
