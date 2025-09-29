using System;
using System.Collections;
using System.Security.Cryptography;
using Assets.Scripts.Input;
using UnityEngine;

public class GptntActions : MonoBehaviour
{
	private int interactableLayer = 11;
	private Camera cam;
	private Selectable lastUsedSelectable;
	private bool isZoomedIn;
	private Selectable zoomedInto;
	public TwitchBomb bomb;

	public float bombRotationX { get; private set; } = 0f;
	public float bombRotationZ { get; private set; } = 0f;
	bool StartingFace;
	SideFace activeFace = SideFace.Front; // for keeping track of which side face we are on / have to return to 
	ZFace currentFace = ZFace.Side; // for keeping track of face perpendicular to the z axis

	private enum SideFace
	{
		Front = 0,
		Right = 90,
		Back = 180,
		Left = 270,
	}

	private enum ZFace
	{
		Top = 90,
		Side = 0,
		Bottom = -90,
	}

	public Action OnZoomOut;
	
	private void Start()
	{
		cam = Camera.main;
	}

	public void InitRotation()
	{
		bombRotationZ = 0;
		bombRotationX = 0;
		StartingFace = KTInputManager.Instance.SelectableManager.GetActiveFace() == FaceEnum.Front;
		activeFace = SideFace.Front;
		currentFace = ZFace.Side;
	}

	#region Mouse clicks
	public string Click(float x, float y)
	{
		Vector3 screenPoint = new Vector3(
			x * Screen.width,
			y * Screen.height,
			cam.nearClipPlane
		);

		Ray ray = cam.ScreenPointToRay(screenPoint);

		int layerMask = 1 << interactableLayer;

		if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask))
		{
			Selectable selectable = hit.collider.transform.parent.gameObject.GetComponent<Selectable>();
			if (!selectable)
			{
				if (hit.collider.gameObject.GetComponent<SelectableArea>())
				{
					selectable = hit.collider.gameObject.GetComponent<SelectableArea>().Selectable;
				}
				else
				{
					selectable = null;
				}
					
			}

			if (!selectable)
			{
				return "Nothing clickable";
			}
			
			if (selectable.FocusOnInteraction)
			{
				// This means the selectable is a module, update the bomb state
				FindObjectOfType<GptntStates>().UpdateZoomIn(selectable);
				StartCoroutine(ZoomInCoroutine(selectable));
			}
			else
			{
				SimonComponent simon = CheckSimonButton(selectable);
				selectable.HandleInteract();
				if (selectable.HasInteractEnded)
					lastUsedSelectable = selectable;
				selectable.SetHighlight(false);
				if(simon) simon.PlaySequenceDelay = 1f; // Set to the time we want. 
			}
			return "clicked on: " + selectable.name;
		}
		else
		{
			return "No interactable hit";
		}
	}

	public string Release()
	{
		if (!lastUsedSelectable)
		{
			return "No last used selectable.";
		}
		lastUsedSelectable.OnInteractEnded();
		string response = "Released: " + lastUsedSelectable.name;
		lastUsedSelectable = null;
		return response;
	}

	public string ZoomOut()
	{
		if (isZoomedIn)
		{
			OnZoomOut?.Invoke();
			string response = "Zooming out from: " + zoomedInto.name;
			StartCoroutine(ZoomOutCoroutine());
			return response;
		}
		return "No module zoomed into";
	}

	private IEnumerator ZoomOutCoroutine()
	{
		yield return new WaitUntil(() => Time.timeScale > 0);
		zoomedInto.HandleDeselect();
		KTInputManager.Instance.SelectableManager.HandleCancel();
		isZoomedIn = false;
		zoomedInto = null;
	}

	private IEnumerator ZoomInCoroutine(Selectable selectable)
	{
		yield return new WaitUntil(() => Time.timeScale > 0);
		FloatingHoldable floating = KTInputManager.Instance.SelectableManager.GetCurrentFloatingHoldable();

		KTInputManager.Instance.SelectableManager.UnlockSelection();
		KTInputManager.Instance.EnableInteraction();


		KTInputManager.Instance.SelectableManager.Select(selectable, false);
		KTInputManager.Instance.SelectableManager.HandleFaceSelection();
		floating.Focus(selectable.transform, selectable.FocusDistance, true, false, 0f);
		floating.OnFocusChild(selectable.gameObject);

		selectable.HandleInteract();
		KTInputManager.Instance.SelectableManager.HandleInteract();
		lastUsedSelectable = selectable;
		isZoomedIn = true;
		zoomedInto = selectable;
		CheckSimonModule(selectable);
	}

	// If we click a simon button, increase the time it takes before it resets back to default
	private SimonComponent CheckSimonButton(Selectable selectable)
	{
		SimonComponent simon = selectable.Parent.GetComponent<SimonComponent>();
		if (simon == null)
			return null;

		GptntDebug.Log("[Simon] Resetting Simon");
		simon.PlaySequenceDelay = 5f; // Reset back to default time
		// change to 5
		// click
		// chaneg to 1 if strike
		return simon;
	}

	// Zooming into smion says
	private void CheckSimonModule(Selectable selectable)
	{
		SimonComponent simon = selectable.GetComponent<SimonComponent>();
		if (simon == null)
			return;
		GptntDebug.Log("Reset simon to 1f");
		simon.PlaySequenceDelay = 1f;
		simon.PassSequenceDelay = 1f;
		simon.StopAllCoroutines();
		simon.StartCoroutine("PlaySequence", simon.PlaySequenceDelay);
	}

	#endregion

	#region Rotation

	public IEnumerator Rotate90(string direction)
	{
		if (isZoomedIn)
		{
			ZoomOut();
			yield return new WaitForSeconds(0.5f);
		}
		Rotation90(direction);
	}

	public IEnumerator Rotate180()
	{
		if (isZoomedIn)
		{
			ZoomOut();
			yield return new WaitForSeconds(0.5f);
		}
		Rotation180();
	}

	private void Rotation90(string direction)
	{
		InputInterceptor.DisableInput();
		if (direction.Equals("right"))
		{
			activeFace = CycleSide(activeFace, 1);
		}
		else if (direction.Equals("left"))
		{
			activeFace = CycleSide(activeFace, -1);
		}else if (direction.Equals("up"))
		{
			currentFace = CycleFace(currentFace, 1);
		}else if (direction.Equals("down"))
		{
			currentFace = CycleFace(currentFace, -1);
		}
		bomb.RotateByLocalQuaternion(Quaternion.Euler((int) currentFace, 0, (int) activeFace));
		GptntDebug.Log($"[Debug] Set the bomb rotation to X:{(int) currentFace} Z:{(int) activeFace}");
	}

	private void Rotation180()
	{
		InputInterceptor.DisableInput();
		switch (currentFace)
		{
			case ZFace.Side:
				activeFace = CycleSide(activeFace, 2);
				break;
			case ZFace.Top:
				currentFace = CycleFace(currentFace, -2);
				break;
			case ZFace.Bottom:
				currentFace = CycleFace(currentFace, 2);
				break;
		}
		bomb.RotateByLocalQuaternion(Quaternion.Euler((int) currentFace, 0, (int) activeFace));
		GptntDebug.Log($"[Debug] Set the bomb rotation to X:{(int) currentFace} Z:{(int) activeFace}");
	}

	#endregion

	private SideFace CycleSide(SideFace face, int step)
	{
		// Cycle the state "step" times to the right
		int deg = ((int) face + 90 * step) % 360;
		if (deg < 0) deg += 360;  // normalize
		return (SideFace) deg;
	}

	private ZFace CycleFace(ZFace face, int step)
	{
		int deg = Mathf.Clamp((int) face + 90 * step, -90, 90);

		return (ZFace) deg;
	}

	public string GetBombSide()
	{
		// Normalize the angles between 0 and 360
		float xRotation = NormalizeAngle(bombRotationX);
		float zRotation = NormalizeAngle(bombRotationZ);

		// Check for top/bottom tilt (looking up/down)
		if (xRotation > 45f && xRotation < 135f)
			return "bottom";  // Looking downward
		if (xRotation > 225f && xRotation < 315f)
			return "top";     // Looking upward

		if (zRotation >= 315f || zRotation < 45f)
			return "front";
		if (zRotation >= 45f && zRotation < 135f)
			return "right";
		if (zRotation >= 135f && zRotation < 225f)
			return "back";
		if (zRotation >= 225f && zRotation < 315f)
			return "left";

		return "unknown";
	}

	private float NormalizeAngle(float angle)
	{
		angle %= 360f;
		if (angle < 0f)
			angle += 360f;
		return angle;
	}
}

