using System;
using UnityEngine;

public class GptntActions : MonoBehaviour
{
	private int interactableLayer = 11;
	private Camera cam;

	private void Start()
	{
		cam = Camera.main;
	}
	public void SendAction(float x, float y)
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
			//PrintChildrenRecursive(hit.collider.transform.parent.gameObject);
			Selectable selectable = hit.collider.transform.parent.gameObject.GetComponent<Selectable>();

			if (selectable.FocusOnInteraction)
			{
				GptntDebug.Log("The selectable can be focused: " + selectable.name);
				FloatingHoldable floating = KTInputManager.Instance.SelectableManager.GetCurrentFloatingHoldable();
				floating.Focus(selectable.transform, selectable.FocusDistance, true, false, 0f);
				floating.OnFocusChild(selectable.gameObject);
				KTInputManager.Instance.SelectableManager.UnlockSelection();
				KTInputManager.Instance.EnableInteraction();
				GptntDebug.Log("Selected the Child: " + selectable.Children[0]);
				KTInputManager.Instance.SelectableManager.Select(selectable.Children[0], false);
				KTInputManager.Instance.Select(selectable.Children[0]);
				selectable.HandleInteract();
				KTInputManager.Instance.SelectableManager.HandleInteract();
			}
			else
			{
				GptntDebug.Log("Not focusable: " + selectable.name);
			}
			//GameObject module = hit.collider.transform.parent.gameObject;
			//if (module.GetComponent<BombComponent>())
			//{
			//	GptntDebug.Log("Module: " + module.name);
			//	KTInputManager.Instance.SelectableManager.Select(module.GetComponent<Selectable>(), false);
			//}
			////else if (hit.collider.gameObject.name.Equals("SelectableArea"))
			////{
			////	GptntDebug.Log("Selectable");
			////	BombComponent component = hit.collider.transform.parent.gameObject.GetComponent<BombComponent>();



			////	// TODO: Use the solver instead. 
			////	hit.collider.gameObject.GetComponent<SelectableArea>().Selectable.HandleInteract();
			////}
			//else
			//{
			//	GptntDebug.Log("No Component found");
			//	PrintChildrenRecursive(hit.collider.transform.parent.gameObject);
			//}
		}
		else
		{
			GptntDebug.Log("No interactable hit.");
		}
	}

	public void PrintChildrenRecursive(GameObject obj, int depth = 0)
	{
		GptntDebug.Log("Parent: " + obj.name);
		PrintAllComponents(obj);
		foreach (Transform child in obj.transform)
		{
			string indent = new string('-', depth);
			GptntDebug.Log($"{indent}{child.gameObject.name}");
			PrintAllComponents(child.gameObject);
			PrintChildrenRecursive(child.gameObject, depth + 1);
		}
	}

	public static void PrintAllComponents(GameObject obj)
	{
		Component[] components = obj.GetComponents<Component>();
		GptntDebug.Log($"Components on '{obj.name}':");

		foreach (Component comp in components)
		{
			if (comp != null)
				GptntDebug.Log("+ " + comp.GetType().Name);
			else
				GptntDebug.Log("+ [Missing Component]");
		}
	}
}

