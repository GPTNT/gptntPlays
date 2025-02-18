using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class QueueRemover : MonoBehaviour {

	// Use this for initialization
	void Start () {
		GameObject box = GameObject.Find("PendingCommandsBox");
		box.SetActive(false);
	}
	
	// Update is called once per frame
	void Update () {
		
	}
}
