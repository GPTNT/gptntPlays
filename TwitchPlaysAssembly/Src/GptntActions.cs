using System;
using System.Collections;
using Assets.Scripts.Input;
using UnityEngine;
using UnityEngine.UI;
using log4net;

public class GptntActions : MonoBehaviour
{
	private int interactableLayer = 11;
	private Camera cam;
	private Selectable lastUsedSelectable;
	public bool isZoomedIn { get; private set; }
	private Selectable zoomedInto;
	public TwitchBomb bomb;
	bool StartingFace;
	public SideFace activeFace = SideFace.Front; // for keeping track of which side face we are on / have to return to 
	public ZFace currentFace = ZFace.Side; // for keeping track of face perpendicular to the z axis

	private static ILog log = LogManager.GetLogger("GptntActions");

	public enum SideFace
	{
		Front = 0,
		Right = 90,
		Back = 180,
		Left = 270,
	}

	public enum ZFace
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

			ClickSelectable(selectable);
			return "clicked on: " + selectable.name;
		}
		else
		{
			return "No interactable hit";
		}
	}

	public void ClickSelectable(Selectable selectable)
	{
		if (selectable.FocusOnInteraction)
		{
			// This means the selectable is a module, update the bomb state
			ZoomIn(selectable);
		}
		else
		{
			SimonComponent simon = CheckSimonButton(selectable);
			selectable.HandleInteract();
			if (selectable.HasInteractEnded)
				lastUsedSelectable = selectable;
			selectable.SetHighlight(false);
			if (simon) simon.PlaySequenceDelay = 1f; // Set to the time we want. 
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

	public void ZoomIn(Selectable selectable)
	{
		FindObjectOfType<GptntStates>().UpdateZoomIn(selectable);
		StartCoroutine(ZoomInCoroutine(selectable));
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
		bomb.RotateByLocalQuaternion(Quaternion.Euler((int) currentFace, 0f, (int) activeFace));
		MaybeUpdateBombFace();
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
		MaybeUpdateBombFace();
	}

	#endregion

	private SideFace CycleSide(SideFace face, int step) // Front / Back / Left / Right
	{
		// Cycle the state "step" times to the right
		int deg = ((int) face + 90 * step) % 360;
		if (deg < 0) deg += 360;  // normalize
		log.Debug($"Setting Z spin to {deg} for side: {(SideFace) deg}");
		return (SideFace) deg;
	}

	private ZFace CycleFace(ZFace face, int step) // Top / Side / Bottom
	{
		int deg = Mathf.Clamp((int) face + 90 * step, -90, 90);
		return (ZFace) deg;
	}

	public string GetBombSide()
	{
		if (currentFace == ZFace.Side)
			return activeFace.ToString().ToLower();

		return currentFace.ToString().ToLower();
	}


	private float NormalizeAngle(float angle)
	{
		angle %= 360f;
		if (angle < 0f)
			angle += 360f;
		return angle;
	}

	private void MaybeUpdateBombFace() // Update the selectable that the bomb is facing
	{
		if (currentFace != ZFace.Side) return;
		if (activeFace.EqualsAny(SideFace.Left, SideFace.Right)) return;

		StartCoroutine(bomb.MyForceHeldRotation(activeFace == SideFace.Front, 0f));
	}
}

