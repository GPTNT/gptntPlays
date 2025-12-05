using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using log4net;

/*
*	Script to segment objects in a scene
*	Usage: Attach this script to a GameObject in the scene
*	Then start the Capture corroutine with a list of GameObjects to segment and a callback to specify what to do with the bytes
*	Example:
* 	StartCoroutine(segmentation.Capture(cubes , (bytes) => {
*				System.IO.File.WriteAllBytes("Assets/segmentation.png", bytes);
*			}));
*/
public class Segmentation : MonoBehaviour
{

	private MaterialPropertyBlock propertyBlock = null;
	private Camera mainCam = null;
	private Camera duplicateCam = null;
	private GameObject duplicate = null;
	private RenderTexture renderTexture;
	private Texture2D tex;
	private Rect rect;
	[SerializeField] public Shader shader;
	private int segmentationLayer = 31;
	private int defaultLayer = 11;
	// each element in renderers has an array of renderers for the children of an object
	private List<Renderer[]> renderersWithChildren;
	private List<GameObject> objectsOnSegmentationLayer;
	private static ILog log = LogManager.GetLogger("Segmentation");


	public void Init(int width, int height)
	{
		renderTexture = new RenderTexture(width, height, 24);
		renderTexture.antiAliasing = 1;
		renderTexture.filterMode = FilterMode.Point;
		tex = new Texture2D(width, height);
		rect = new Rect(0, 0, width, height);
		if (!shader)
		{
			var shaderHolder = GetComponent("ShaderHolder");
			if (shaderHolder != null)
			{
				var shaderField = shaderHolder.GetType().GetField("shader");
				if (shaderField != null)
				{
					shader = shaderField.GetValue(shaderHolder) as Shader;
					log.Debug(GptntDebug.FormatMessage("Shader retrieved from ShaderHolder"));
				}
			}
			else
			{
				log.Error(GptntDebug.FormatMessage("ShaderHolder component not found"));
			}
		}

	}

	public IEnumerator Capture(GameObject[] objects, Action<byte[]> callback)
	{
		if (objects.Length == 0)
		{
			callback?.Invoke(null);
			yield break;
		}
		propertyBlock = new MaterialPropertyBlock();
		objectsOnSegmentationLayer = new List<GameObject>();
		Segment(objects);
		yield return new WaitForEndOfFrame();
		byte[] bytes = RenderTextureToPNGBytes(renderTexture);
		callback?.Invoke(bytes);
		renderTexture.Release();
		ResetObjects();
	}

	public void Segment(GameObject[] objects)
	{
		DuplicateCamera();

		objects = tryGetVennWires(objects);
		objects = tryGetButton(objects);
		objects = tryGetModule(objects);

		SetObjectsToSegmentationLayer(objects);
		renderersWithChildren = GetRenderers(objects);

		float hue = 0f;
		// Sets a unique color for each object
		foreach (Renderer[] ls in renderersWithChildren)
		{
			propertyBlock.SetColor("_ObjectColor", Color.HSVToRGB(hue, 1, 1));
			hue += 1f / renderersWithChildren.Count;
			foreach (Renderer renderer in ls)
			{
				renderer.SetPropertyBlock(propertyBlock);
			}
		}
	}

	private void ResetObjects()
	{
		foreach (Renderer[] list in renderersWithChildren)
		{
			foreach (Renderer r in list)
			{
				r.SetPropertyBlock(null);
			}
			foreach (var obj in objectsOnSegmentationLayer)
			{
				obj.layer = defaultLayer;
			}
		}
	}

	private void SetObjectsToSegmentationLayer(GameObject[] objects)
	{
		foreach (var obj in objects)
		{
			SetLayerRecursively(obj, segmentationLayer);
		}
	}

	private void SetLayerRecursively(GameObject obj, int layer)
	{
		if (obj.name.Equals("StatusLightParent"))
			return;
			
		obj.layer = layer;
		if(layer == segmentationLayer) objectsOnSegmentationLayer.Add(obj);

		foreach (Transform child in obj.transform)
		{
			SetLayerRecursively(child.gameObject, layer);
		}
	}

	// Helper function to get all renderers from a list of game objects
	private List<Renderer[]> GetRenderers(GameObject[] objects)
	{
		List<Renderer[]> renderers = new List<Renderer[]>();
		foreach (GameObject obj in objects)
		{
			Renderer[] child = obj.GetComponentsInChildren<Renderer>();
			renderers.Add(child);
		}
		return renderers;
	}

	// Check if VennWires since the venn wires module selectables dont have a renderer under them
	private GameObject[] tryGetVennWires(GameObject[] objects)
	{
		// Checks if the objects are the wires themselves and the not the whole module
		if (!objects[0].name.StartsWith("VennWire") || objects[0].name.StartsWith("VennWiresComponent")) return objects;
		
		List<GameObject> vennObjects = new List<GameObject>();
		Transform venn = KTInputManager.Instance.SelectableManager.GetCurrentParent().transform;
		int childCount = venn.childCount;
		venn = venn.GetChild(childCount - 2);
		childCount = venn.childCount;
		for (int i = childCount - 1; i > childCount - 7; i--)
		{
			Transform child = venn.GetChild(i);
			foreach (Transform grandChild in child)
			{
				vennObjects.Add(grandChild.gameObject);
			}
			
		}
		return vennObjects.ToArray();
	}

	// Check if Button since the button also has the casing and light strip as part of the selectable.
	private GameObject[] tryGetButton(GameObject[] objects)
	{
		if (!objects[0].name.Equals("Button")) return objects;
		objects[0] = objects[0].transform.GetChild(0).gameObject;
		return objects;
	}

	private GameObject[] tryGetModule(GameObject[] objects)
	{
		List<GameObject> objectsWithoutLight = new List<GameObject>();
		foreach (var obj in objects)
		{
			if (!obj.name.Equals("StatusLightParent"))
				objectsWithoutLight.Add(obj);
		}
		return objectsWithoutLight.ToArray();
	}

	// Convert a RenderTexture to a Texture2D
	private Texture2D ConvertRenderTextureToTexture2D(RenderTexture rt)
	{
		RenderTexture.active = rt;
		tex.ReadPixels(rect, 0, 0);
		tex.Apply();
		RenderTexture.active = null;
		return tex;
	}

	private byte[] RenderTextureToPNGBytes(RenderTexture texture)
	{
		byte[] bytes = ConvertRenderTextureToTexture2D(texture).EncodeToPNG();
		return bytes;
	}

	// Helper function to duplicate the camera
	private void DuplicateCamera()
	{
		if (duplicate) return;

		if (!mainCam)
		{
			mainCam = Camera.main;
			if (!mainCam)
			{
				log.Warn(GptntDebug.FormatMessage("Main camera not found"));
				return;
			}
		}

		duplicate = new GameObject();
		duplicate.name = "SegmentationCamera";
		duplicate.transform.SetParent(mainCam.transform);
		duplicate.transform.localPosition = Vector3.zero;
		duplicate.transform.localRotation = Quaternion.identity;
		duplicate.transform.localScale = Vector3.one;

		// duplicate.hideFlags = HideFlags.HideInHierarchy;

		duplicateCam = duplicate.AddComponent<Camera>();

		duplicateCam.cullingMask = 1 << segmentationLayer;
		duplicateCam.aspect = mainCam.aspect;
		duplicateCam.nearClipPlane = mainCam.nearClipPlane;
		duplicateCam.farClipPlane = mainCam.farClipPlane;
		duplicateCam.fieldOfView = mainCam.fieldOfView;
		duplicateCam.rect = mainCam.rect;
		duplicateCam.depth = mainCam.depth + 1;
		duplicateCam.clearFlags = CameraClearFlags.Color;
		duplicateCam.allowMSAA = false;
		duplicateCam.backgroundColor = Color.black;
		duplicateCam.targetTexture = renderTexture;
		duplicateCam.SetReplacementShader(shader, "");
	}
}
