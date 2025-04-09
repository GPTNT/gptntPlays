using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
	[SerializeField] public Shader shader;
	private int segmentationLayer = 31;
	private int defaultLayer = 11;
	// each element in renderers has an array of renderers for the children of an object
	private List<Renderer[]> renderersWithChildren;
	private List<GameObject> objectsOnSegmentationLayer;
	private TwitchBomb bomb;

	private void Start()
	{
		renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
		if (!shader) GptntDebug.Log("Shader is null");
	}

	public IEnumerator Capture(GameObject[] objects, TwitchBomb bomb, Action<byte[]> callback)
	{
		// TODO: Make sure the bomb is also put on the layer. 
		if (objects.Length == 0) yield return null;
		propertyBlock = new MaterialPropertyBlock();
		objectsOnSegmentationLayer = new List<GameObject>();
		this.bomb = bomb;
		Segment(objects);
		yield return new WaitForEndOfFrame();
		byte[] bytes = RenderTextureToPNGBytes(renderTexture);
		callback?.Invoke(bytes);
		ResetObjects();
	}

	public void Segment(GameObject[] objects)
	{
		DuplicateCamera();
		objects = tryGetVennWires(objects);
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
			GptntDebug.Log("Putting this on segmentation layer: " + obj.name);
			SetLayerRecursively(obj, segmentationLayer);
		}
		bomb.gameObject.layer = segmentationLayer;
	}
	private void SetLayerRecursively(GameObject obj, int layer)
	{
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
			vennObjects.Add(child.gameObject);
		}
		return vennObjects.ToArray();
	}

	// Convert a RenderTexture to a Texture2D
	private Texture2D ConvertRenderTextureToTexture2D(RenderTexture rt)
	{
		Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
		RenderTexture.active = rt;
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
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
				Debug.LogError("Main camera not found");
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
		duplicateCam.backgroundColor = Color.black;
		duplicateCam.targetTexture = renderTexture;
		duplicateCam.SetReplacementShader(shader, "");
	}
}
