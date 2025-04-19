using System;
using System.Collections;
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
	bool onFrontFace = true;
	bool onBackFace = false;
	bool onLeftSide = false;
	bool onRightSide = false;

	private void Start()
	{
		cam = Camera.main;
	}

	public void InitRotation()
	{
		bombRotationZ = 0;
		bombRotationX = 0;
		StartingFace = KTInputManager.Instance.SelectableManager.GetActiveFace() == FaceEnum.Front;
		onFrontFace = true;
		onBackFace = false;
		onLeftSide = false;
		onRightSide = false;
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
			GptntDebug.Log("First hit: " + hit.collider.gameObject.name);
			Selectable selectable = hit.collider.transform.parent.gameObject.GetComponent<Selectable>();
			if (!selectable)
			{
				GptntDebug.Log("Couldnt get from selectable parent");
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
				GptntDebug.Log("Selectable not found!");
				//GptntDebug.LogChildrenRecursive(hit.collider.transform.parent.gameObject);
				return "Nothing clickable";
			}

			if (selectable.FocusOnInteraction)
			{
				GptntDebug.Log("The selectable can be focused: " + selectable.name);
				FloatingHoldable floating = KTInputManager.Instance.SelectableManager.GetCurrentFloatingHoldable();

				KTInputManager.Instance.SelectableManager.UnlockSelection();
				KTInputManager.Instance.EnableInteraction();

				GptntDebug.Log("Selected in Selectable Manager: " + selectable.Children[0]);

				KTInputManager.Instance.SelectableManager.Select(selectable, false);
				KTInputManager.Instance.SelectableManager.HandleFaceSelection();

				floating.Focus(selectable.transform, selectable.FocusDistance, true, false, 0f);
				floating.OnFocusChild(selectable.gameObject);

				selectable.HandleInteract();
				KTInputManager.Instance.SelectableManager.HandleInteract();
				lastUsedSelectable = selectable;
				isZoomedIn = true;
				zoomedInto = selectable;
			}
			else
			{
				GptntDebug.Log("Not focusable: " + selectable.name + "\nInteracting: " + selectable.name);
				selectable.HandleInteract();
				if (selectable.HasInteractEnded)
					lastUsedSelectable = selectable;
				else
					lastUsedSelectable = null;
				selectable.SetHighlight(false);
			}
			return "clicked on: " + selectable.name;
		}
		else
		{
			GptntDebug.Log("No interactable hit.");
			return "No interactable hit";
		}
	}

	public string Release()
	{
		GptntDebug.Log("Called Release");
		if (!lastUsedSelectable)
		{
			GptntDebug.Log("No last used selectable.");
			return "No last used selectable.";
		}
		lastUsedSelectable.OnInteractEnded();
		return "Released: " + lastUsedSelectable.name;
	}

	public string ZoomOut()
	{
		if (isZoomedIn)
		{
			string response = "Zooming out from: " + zoomedInto.name;
			GptntDebug.Log(response);
			zoomedInto.HandleDeselect();
			KTInputManager.Instance.SelectableManager.HandleCancel();
			isZoomedIn = false;
			zoomedInto = null;
			return response;
		}
		GptntDebug.Log("Nothing to zoom out of.");
		return "No module zoomed into";
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
		if (direction.Equals("right"))
		{
			InputInterceptor.DisableInput();
			if (bombRotationZ == 180)
			{
				bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

			}
			else
			{
				bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

			}
			StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));
			if (onFrontFace)
			{
				bombRotationZ = 90;
			}
			else if (onBackFace)
			{
				bombRotationZ = -90;
			}
			else if (onLeftSide)
			{
				bombRotationZ = 0;
			}
			else if (onRightSide)
			{
				bombRotationZ = 180;
			}
			bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));
			alignFace90R();
		}
		if (direction.Equals("left"))
		{
			InputInterceptor.DisableInput();
			if (bombRotationZ == 180)
			{
				bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

			}
			else
			{
				bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

			}
			StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));
			if (onFrontFace)
			{
				bombRotationZ = -90;
			}
			else if (onBackFace)
			{
				bombRotationZ = 90;
			}
			else if (onLeftSide)
			{
				bombRotationZ = 180;
			}
			else if (onRightSide)
			{
				bombRotationZ = 0;
			}
			bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));
			alignFace90L();
		}

		if (direction.Equals("up"))
		{
			if (bombRotationX < 90)
			{
				InputInterceptor.DisableInput();
				if (bombRotationZ == 180)
				{
					bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

				}
				else
				{
					bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

				}
				StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));


				bombRotationX += 90;
				bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));

			}
		}

		if (direction.Equals("down"))
		{
			if (bombRotationX > -90)
			{
				InputInterceptor.DisableInput();
				if (bombRotationZ == 180)
				{
					bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));

				}
				else
				{
					bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));

				}
				StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));


				bombRotationX -= 90;
				bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));
			}
		}

		if (bombRotationX == 0)
		{
			if (bombRotationZ % 360 == 0)
			{
				StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));
				InputInterceptor.EnableInput();
			}
			else if (bombRotationZ % 180 == 0)
			{
				StartCoroutine(bomb.MyForceHeldRotation(!StartingFace, 0f));
				InputInterceptor.EnableInput();
			}
		}
		//throw new Exception($"X rotation: {bombRotationX}\n Z rotation: {bombRotationZ}");
		GptntDebug.Log("X: " + bombRotationX + "Z: " + bombRotationZ);
	}

	private void Rotation180()
	{
		ZoomOut();
		InputInterceptor.DisableInput();
		if (bombRotationZ == 180)
		{
			bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, 0));
		}
		else
		{
			bomb.RotateByLocalQuaternion(Quaternion.Euler(-bombRotationX, 0, -bombRotationZ));
		}
		StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));

		if (onFrontFace)
		{
			bombRotationZ = 180;
		}
		else if (onBackFace)
		{
			bombRotationZ = 0;
		}
		else if (onLeftSide)
		{
			bombRotationZ = 90;
		}
		else if (onRightSide)
		{
			bombRotationZ = -90;
		}
		bomb.RotateByLocalQuaternion(Quaternion.Euler(bombRotationX, 0, bombRotationZ));

		if (bombRotationX == 0)
		{
			if (bombRotationZ % 360 == 0)
			{
				StartCoroutine(bomb.MyForceHeldRotation(StartingFace, 0f));
				InputInterceptor.EnableInput();
			}
			else if (bombRotationZ % 180 == 0)
			{
				StartCoroutine(bomb.MyForceHeldRotation(!StartingFace, 0f));
				InputInterceptor.EnableInput();
			}
		}
		alignFace180();
	}

	private void alignFace180()
	{
		if (onFrontFace)
		{
			onFrontFace = false;
			onBackFace = true;
		}
		else if (onBackFace)
		{
			onBackFace = false;
			onFrontFace = true;
		}
		else if (onLeftSide)
		{
			onLeftSide = false;
			onRightSide = true;
		}
		else if (onRightSide)
		{
			onRightSide = false;
			onLeftSide = true;
		}
	}

	private void alignFace90L()
	{
		if (onFrontFace)
		{
			onFrontFace = false;
			onLeftSide = true;
		}
		else if (onBackFace)
		{
			onBackFace = false;
			onRightSide = true;
		}
		else if (onLeftSide)
		{
			onLeftSide = false;
			onBackFace = true;
		}
		else if (onRightSide)
		{
			onRightSide = false;
			onFrontFace = true;
		}
	}

	private void alignFace90R()
	{
		if (onFrontFace)
		{
			onFrontFace = false;
			onRightSide = true;
		}
		else if (onBackFace)
		{
			onBackFace = false;
			onLeftSide = true;
		}
		else if (onLeftSide)
		{
			onLeftSide = false;
			onFrontFace = true;
		}
		else if (onRightSide)
		{
			onRightSide = false;
			onBackFace = true;
		}
	}

	//private void alignFace90U()
	//{
	//	if (inMiddle && onFrontFace)
	//	{
	//		GptntDebug.Log("1");
	//		inMiddle = false;
	//		onTopFromFront = true;
	//		onFrontFace = false; // Changed this
	//	}

	//	else if (inMiddle && onBackFace)
	//	{
	//		GptntDebug.Log("2");
	//		inMiddle = false;
	//		onTopFromBack = true;
	//		onBackFace = false; // Changed this
	//	}

	//	else if (onBottomFromBack)
	//	{
	//		GptntDebug.Log("3");
	//		onBottomFromBack = false;
	//		inMiddle = true;
	//		onBackFace = true; // Changed this
	//	}
	//	else if (onBottomFromFront)
	//	{
	//		GptntDebug.Log("4");
	//		onBottomFromFront = false;
	//		inMiddle = true;
	//		onFrontFace = true; // Changed this
	//	}
	//	else if (onBottomFromLeftSide)
	//	{
	//		onBottomFromLeftSide = false;
	//		inMiddle = true;
	//	}

	//	else if (onTopFromRightSide)
	//	{
	//		onBottomFromRightSide = false;
	//		inMiddle = true;
	//	}
	//}

	//private void alignFace90D()
	//{
	//	GptntDebug.Log("Called alignFace90D");
	//	if (inMiddle && onFrontFace)
	//	{
	//		GptntDebug.Log("1");
	//		inMiddle = false;
	//		onBottomFromFront = true;
	//		onFrontFace = false; // Changed this
	//	}
	//	else if (inMiddle && onBackFace)
	//	{
	//		GptntDebug.Log("2");
	//		inMiddle = false;
	//		onBottomFromBack = true;
	//		onBackFace = false; // Changed this
	//	}

	//	else if (onTopFromFront)
	//	{
	//		GptntDebug.Log("3");
	//		onTopFromFront = false;
	//		inMiddle = true;
	//		onFrontFace = true; // Changed this
	//	}

	//	else if (onTopFromLeftSide)
	//	{
	//		onTopFromLeftSide = false;
	//		inMiddle = true;
	//	}

	//	else if (onTopFromRightSide)
	//	{
	//		onTopFromRightSide = false;
	//		inMiddle = true;
	//	}

	//	else if (onTopFromBack)
	//	{
	//		GptntDebug.Log("4");
	//		onTopFromBack = false;
	//		inMiddle = true;
	//		onBackFace = true; // Changed this
	//	}
	//}

	#endregion

}

