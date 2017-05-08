using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;



[System.Serializable]
public class UnityEvent_OnString : UnityEvent <string> {}



public class FloatToString : MonoBehaviour {

	public string				Prefix;
	public string				Suffix;
	public UnityEvent_OnString	OnString;

	public void SetFloat(float Value)
	{
		OnString.Invoke( Prefix + Value.ToString("0.00") + Suffix );
	}

}
