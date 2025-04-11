using System;
using UnityEngine;

public class GptntActions : MonoBehaviour
{
	private int interactableLayer = 11;
	private Camera cam;
	private Selectable lastUsedSelectable;
	private bool isZoomedIn;
	private Selectable zoomedInto;

	private void Start()
	{
		cam = Camera.main;
	}
	public void Click(float x, float y)
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
				GptntDebug.LogChildrenRecursive(hit.collider.transform.parent.gameObject);
				return;
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
				lastUsedSelectable = selectable;
			}
		}
		else
		{
			GptntDebug.Log("No interactable hit.");
		}
	}

	public void Release()
	{
		if (!lastUsedSelectable)
		{
			GptntDebug.Log("No last used selectable.");
			return;
		}
		lastUsedSelectable.OnInteractEnded();
	}

	public void ZoomOut()
	{
		if (isZoomedIn)
		{
			GptntDebug.Log("Zooming out from: " + zoomedInto.name);
			isZoomedIn = false;
			zoomedInto.HandleDeselect();
			KTInputManager.Instance.SelectableManager.HandleCancel();
		}
	}

}

