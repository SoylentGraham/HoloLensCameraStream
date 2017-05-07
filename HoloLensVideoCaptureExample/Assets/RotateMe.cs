using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotateMe : MonoBehaviour {
	
	[Range(-180,180)]
	public float		RotX = 0;

	[Range(-180,180)]
	public float		RotY = 0;

	[Range(-180,180)]
	public float		RotZ = 0;



	void Update () {
		
		var RotEular = this.transform.localEulerAngles;
		var RotQuat = Quaternion.Euler( RotX * Time.deltaTime,  RotY * Time.deltaTime,  RotZ * Time.deltaTime );
		this.transform.localRotation *= RotQuat;

	}
}
