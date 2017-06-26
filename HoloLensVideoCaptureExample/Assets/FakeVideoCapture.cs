﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;




public class FakeVideoCapture : MonoBehaviour {

	[Range(1.0f,60.0f)]
	public float				FrameRate = 30;
	float						FrameCountdown = 0;

	public UnityEvent_ImageWidthHeight	OnNewFrame;

	[Range(1,100)]
	public int				Width = 100;

	[Range(1,100)]
	public int				Height = 100;

	public List<Color32>	ColourCycle = new List<Color32>();
	int ColourIndex = 0;

	void Start()
	{
		if ( ColourCycle == null )
			ColourCycle = new List<Color32>();
		if ( ColourCycle.Count == 0 )
			ColourCycle.Add( Color.red );
	}

	void Update ()
	{
		FrameCountdown -= Time.deltaTime;

		if ( FrameCountdown > 0 )
			return;

		SendNextFrame();
		FrameCountdown += 1.0f / FrameRate;
	}

	void SendNextFrame()
	{
		ColourIndex++;
		var Colour = ColourCycle[ ColourIndex%ColourCycle.Count];
		int ComponentCount = 4;
		var Bytes = BytePool.Global.Alloc(Width*Height*ComponentCount);
		
		for (int b = 0; b < Bytes.Length; b += ComponentCount)
		{
			Bytes[b+0] = Colour.b;
			Bytes[b+1] = Colour.g;
			Bytes[b+2] = Colour.r;
			Bytes[b+3] = Colour.a;
		}
		
		OnNewFrame.Invoke( Bytes, TextureFormat.BGRA32, Width, Height );

		BytePool.Global.Release( Bytes );
	}
}
