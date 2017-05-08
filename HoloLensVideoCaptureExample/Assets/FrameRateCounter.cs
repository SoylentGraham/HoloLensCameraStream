using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;


[System.Serializable]
public class UnityEvent_OnFrameRateUpdate : UnityEvent <float> {}



public class FrameRateCounter : MonoBehaviour {

	public UnityEvent_OnFrameRateUpdate	OnFrameRateUpdate;

	
	int		FrameCounter = 0;
	float	LastFrameCountTime = 0;


	public void		Increment(int Amount=1)
	{
		FrameCounter += Amount;
	}

	void Update ()
	{
		float TimeSinceFrameCount = Time.time - LastFrameCountTime;
		if (TimeSinceFrameCount >= 1.0f)
		{
			float FrameCountF = FrameCounter / TimeSinceFrameCount;

			try
			{
				OnFrameRateUpdate.Invoke(FrameCountF);
			}
			catch(System.Exception e)
			{ 
				Debug.LogException(e);
			}

			string FrameCountString = "" + FrameCountF.ToString("0.00") + "fps";
			Debug.Log(FrameCountString);

			FrameCounter = 0;
			LastFrameCountTime = Time.time;
		}

	}
}
