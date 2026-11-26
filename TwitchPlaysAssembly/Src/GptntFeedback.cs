using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Assets.Scripts.Missions;
using TwitchPlaysAssembly;

// Coordinates refer to the RGB frame, with the origin at the top left.
public class FeedbackRegion
{
	public string id;
	public string kind;
	public string visibility;
	public string shape;
	public int[] bounds;
	public float[] center;
	public float radius;
	public string failure;
}

public class FeedbackFrame
{
	public int version = 1;
	public long sequence;
	public long epoch;
	public int width;
	public int height;
	public string selectedModuleId;
	public string selectedModuleType;
	public string targetModuleId;
	public string targetModuleType;
	public int? targetModuleLocation;
	public bool complete;
	public string failure;
	public List<FeedbackRegion> regions = new List<FeedbackRegion>();
}

// Draws neutral geometry from every physical indicator variant, including inactive
// renderers. A full-scene depth pass establishes visibility;
// occluded or uncertain footprints are unavailable rather than guessed.
public sealed class GptntFeedback : IDisposable
{
	private sealed class Group
	{
		public FeedbackRegion Region;
		public List<Renderer> Sources = new List<Renderer>();
		public Bounds? WorldBounds;
	}

	private sealed class SavedRenderer
	{
		public Renderer Renderer;
		public bool Enabled;
		public MaterialPropertyBlock Block;
	}

	private BombComponent retainedTarget;
	private Bomb retainedBomb;

	public void ResetTarget()
	{
		retainedTarget = null;
		retainedBomb = null;
	}

	private Camera camera;
	private RenderTexture target;
	private Texture2D readback;
	private Material material;
	private readonly List<Material> regionMaterials = new List<Material>();
	private CommandBuffer feedbackDraws;
	private readonly List<SavedRenderer> saved = new List<SavedRenderer>();

	public FeedbackFrame Capture(Camera source, Shader shader, long sequence, long epoch, int width, int height)
	{
		FeedbackFrame frame = new FeedbackFrame
		{
			sequence = sequence, epoch = epoch, width = width, height = height,
		};
		RenderTexture previous = RenderTexture.active;
		try
		{
			if (!source || !shader || !shader.isSupported || shader.name != "Gptnt/SegmentationShader")
				throw new InvalidOperationException("Feedback replacement shader or RGB camera unavailable");
			foreach (float distance in source.layerCullDistances)
				if (distance != 0) throw new InvalidOperationException("Custom camera layer culling is unsupported");
			List<Group> groups = FindGroups(frame);
			if (groups.Count == 0)
				throw new InvalidOperationException("No feedback regions discovered");
			PrepareCamera(source, shader, width, height);
			Renderer[] scene = UnityEngine.Object.FindObjectsOfType<Renderer>();
			MaterialPropertyBlock black = new MaterialPropertyBlock();
			black.SetColor("_ObjectColor", Color.black);
			black.SetFloat("_Cutoff", 0);
			foreach (Renderer renderer in scene)
			{
				SavedRenderer state = new SavedRenderer
				{
					Renderer = renderer, Enabled = renderer.enabled,
					Block = new MaterialPropertyBlock(),
				};
				renderer.GetPropertyBlock(state.Block);
				saved.Add(state);
				renderer.SetPropertyBlock(black);
			}
			for (int index = 0; index < groups.Count; index++)
			{
				Group group = groups[index];
				if (group.Sources.Count == 0)
					throw new InvalidOperationException("Feedback region has no mesh: " + group.Region.id);
				foreach (Renderer renderer in group.Sources)
				{
					bool hasSnapshot = false;
					foreach (SavedRenderer state in saved) if (state.Renderer == renderer) hasSnapshot = true;
					if (!hasSnapshot)
					{
						SavedRenderer state = new SavedRenderer { Renderer = renderer, Enabled = renderer.enabled, Block = new MaterialPropertyBlock() };
						renderer.GetPropertyBlock(state.Block);
						saved.Add(state);
					}
					if ((source.cullingMask & (1 << renderer.gameObject.layer)) == 0)
						throw new InvalidOperationException("Feedback mesh excluded from RGB camera");
					Bounds bounds = QueueRegionDraw(renderer, index + 1);
					if (group.WorldBounds.HasValue)
					{
						Bounds union = group.WorldBounds.Value;
						union.Encapsulate(bounds);
						group.WorldBounds = union;
					}
					else group.WorldBounds = bounds;
					renderer.enabled = false;
				}
			}
			// The isolated pass supplies a state-independent footprint. The second
			// pass includes every RGB layer, so the bomb and room occlude that footprint.
			camera.cullingMask = 0;
			byte[] footprint = Render();
			camera.cullingMask = source.cullingMask;
			byte[] visible = Render();
			Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(camera);
			for (int index = 0; index < groups.Count; index++)
			{
				Group group = groups[index];
				bool offscreen = group.WorldBounds.HasValue
					&& !GeometryUtility.TestPlanesAABB(frustum, group.WorldBounds.Value);
				Measure(group.Region, index + 1, footprint, visible, width, height, offscreen);
			}
			frame.complete = true;
			foreach (FeedbackRegion region in frame.regions)
				if (region.visibility == "unavailable") frame.complete = false;
			if (!frame.complete) frame.failure = "Feedback visibility could not be established";
		}
		catch (Exception error)
		{
			// Isolate this optional capture. Never substitute guessed geometry when
			// the game build, mesh layout, shader, or camera cannot be handled.
			frame.complete = false;
			frame.failure = error.Message;
			foreach (FeedbackRegion region in frame.regions)
			{
				region.visibility = "unavailable";
				region.failure = error.Message;
			}
		}
		finally
		{
			if (feedbackDraws != null) feedbackDraws.Clear();
			foreach (Material regionMaterial in regionMaterials) UnityEngine.Object.Destroy(regionMaterial);
			regionMaterials.Clear();
			foreach (SavedRenderer state in saved)
			{
				if (!state.Renderer) continue;
				state.Renderer.enabled = state.Enabled;
				if (state.Block == null) continue;
				state.Renderer.SetPropertyBlock(state.Block.isEmpty ? null : state.Block);
			}
			saved.Clear();
			RenderTexture.active = previous;
		}
		return frame;
	}

	private List<Group> FindGroups(FeedbackFrame frame)
	{
		TwitchBomb[] bombs = UnityEngine.Object.FindObjectsOfType<TwitchBomb>();
		if (bombs.Length != 1) throw new InvalidOperationException("Feedback requires exactly one bomb");
		Bomb bomb = bombs[0].Bomb;
		Selectable parent = KTInputManager.Instance.SelectableManager.GetCurrentParent();
		BombComponent selected = parent ? parent.GetComponentInParent<BombComponent>() : null;
		if (retainedBomb != bomb)
		{
			ResetTarget();
			retainedBomb = bomb;
		}
		if (selected)
		{
			frame.selectedModuleId = ModuleId(bomb, selected);
			frame.selectedModuleType = selected.ComponentType.ToString();
			retainedTarget = selected;
		}
		// Automatic zoom-out after solving Venn must not discard the action's
		// target. Its physical status light can still be visible on the bomb face.
		BombComponent targetModule = retainedTarget;
		List<Group> groups = new List<Group>();
		// Strike groups include both lit and unlit alternatives, regardless of which
		// objects are active. The timer itself remains outside these groups.
		for (int strike = 0; strike < 2; strike++)
		{
			Group group = AddGroup(frame, groups, "bomb/strikes/" + strike, "strikes");
			AddNamed(group, bomb.transform, "Strike" + strike + "_Active");
			AddNamed(group, bomb.transform, "Strike" + strike + "_Inactive");
		}
		if (!targetModule) return groups;
		frame.targetModuleId = ModuleId(bomb, targetModule);
		frame.targetModuleType = targetModule.ComponentType.ToString();
		BaseModuleState targetState = new BaseModuleState(targetModule);
		if (targetState.index < 0 || targetState.index > 5)
			throw new InvalidOperationException("Feedback target has no supported bomb-face anchor");
		frame.targetModuleLocation = targetState.index + (targetState.onFront ? 6 : 0);
		switch (targetModule.ComponentType)
		{
			case ComponentTypeEnum.Wires: case ComponentTypeEnum.BigButton:
			case ComponentTypeEnum.Keypad: case ComponentTypeEnum.WhosOnFirst:
			case ComponentTypeEnum.Memory: case ComponentTypeEnum.Venn:
			case ComponentTypeEnum.WireSequence: case ComponentTypeEnum.Maze:
			case ComponentTypeEnum.Password: break;
			default: throw new InvalidOperationException("Unsupported feedback module: " + frame.targetModuleType);
		}
		AddNamed(AddGroup(frame, groups, frame.targetModuleId + "/status", "status"), targetModule.transform, "StatusLightParent");
		if (targetModule.ComponentType == ComponentTypeEnum.Keypad)
		{
			List<Transform> buttons = new List<Transform>();
			foreach (Transform node in targetModule.GetComponentsInChildren<Transform>(true))
				if (node.name == "LED_Off" && !buttons.Contains(node.parent)) buttons.Add(node.parent);
			if (buttons.Count != 4) throw new InvalidOperationException("Keypad requires four neutral LED groups");
			for (int index = 0; index < buttons.Count; index++)
			{
				Group group = AddGroup(frame, groups, frame.targetModuleId + "/keypad/" + index, "keypad");
				AddNamed(group, buttons[index], "LED_Off");
				AddNamed(group, buttons[index], "LED_Correct");
				AddNamed(group, buttons[index], "LED_Wrong");
			}
		}
		if (targetModule.ComponentType == ComponentTypeEnum.Memory || targetModule.ComponentType == ComponentTypeEnum.WhosOnFirst || targetModule.ComponentType == ComponentTypeEnum.WireSequence)
			AddNamed(AddGroup(frame, groups, frame.targetModuleId + "/stage", "stage"), targetModule.transform, "StageIndicator");
		return groups;
	}

	private static string ModuleId(Bomb bomb, BombComponent selected)
	{
		int index = 0;
		foreach (BombComponent component in bomb.BombComponents)
		{
			if (component == selected) return "bomb/module/" + index;
			index++;
		}
		throw new InvalidOperationException("Feedback target is not on the current bomb");
	}

	private static Group AddGroup(FeedbackFrame frame, List<Group> groups, string id, string kind)
	{
		Group group = new Group { Region = new FeedbackRegion { id = id, kind = kind, visibility = "unavailable", shape = kind == "status" ? "circle" : "rectangle" } };
		frame.regions.Add(group.Region);
		groups.Add(group);
		return group;
	}

	private static void AddNamed(Group group, Transform root, string name)
	{
		int matches = 0;
		foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
		{
			if (node.name != name) continue;
			matches++;
			foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
			{
				// The glow is a transparent effect quad. The status circle covers its
				// bloom; only physical LED meshes establish neutral geometry.
				if (group.Region.kind == "status" && renderer.name == "LightGlow") continue;
				if (!group.Sources.Contains(renderer)) group.Sources.Add(renderer);
			}
		}
		if (matches != 1) throw new InvalidOperationException("Expected one feedback object: " + name);
	}

	private void PrepareCamera(Camera source, Shader shader, int width, int height)
	{
		if (!camera)
		{
			GameObject host = new GameObject("FeedbackCamera");
			camera = host.AddComponent<Camera>();
			camera.enabled = false;
			material = new Material(shader);
			feedbackDraws = new CommandBuffer();
			feedbackDraws.name = "Neutral feedback geometry";
			camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, feedbackDraws);
		}
		if (!target || target.width != width || target.height != height)
		{
			if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
			if (readback) UnityEngine.Object.Destroy(readback);
			target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			target.antiAliasing = 1;
			readback = new Texture2D(width, height, TextureFormat.RGB24, false, true);
		}
		camera.CopyFrom(source);
		camera.enabled = false;
		camera.transform.position = source.transform.position;
		camera.transform.rotation = source.transform.rotation;
		camera.clearFlags = CameraClearFlags.Color;
		camera.backgroundColor = Color.black;
		camera.allowHDR = false;
		camera.allowMSAA = false;
		camera.useOcclusionCulling = false;
		camera.targetTexture = target;
		camera.SetReplacementShader(shader, "");
	}

	private Bounds QueueRegionDraw(Renderer source, int id)
	{
		MeshFilter filter = source.GetComponent<MeshFilter>();
		if (!(source is MeshRenderer) || !filter || !filter.sharedMesh)
			throw new InvalidOperationException("Unsupported feedback renderer: " + source.name);
		// Draw the renderer through Unity so static-batch submesh ranges and GPU-only
		// meshes remain valid. Reading sharedMesh vertices loses both guarantees.
		Material regionMaterial = new Material(material);
		regionMaterials.Add(regionMaterial);
		Color color = new Color(id / 255f, 0, 0, 1);
		regionMaterial.SetColor("_ObjectColor", color);
		MaterialPropertyBlock block = new MaterialPropertyBlock();
		block.SetColor("_ObjectColor", color);
		source.SetPropertyBlock(block);
		int materialCount = source.sharedMaterials.Length;
		if (materialCount == 0) throw new InvalidOperationException("Feedback mesh has no materials");
		for (int submesh = 0; submesh < materialCount; submesh++)
			feedbackDraws.DrawRenderer(source, regionMaterial, submesh);
		return source.bounds;
	}

	private byte[] Render()
	{
		bool invert = GL.invertCulling;
		CameraClearFlags clear = camera.clearFlags;
		try
		{
			// Opaque, double-sided occluders can hide extra pixels. Every
			// occluded footprint is unavailable because transparent materials may
			// still show that indicator in RGB.
			GL.invertCulling = false;
			camera.Render();
			camera.clearFlags = CameraClearFlags.Nothing;
			GL.invertCulling = true;
			camera.Render();
		}
		finally
		{
			GL.invertCulling = invert;
			camera.clearFlags = clear;
		}
		RenderTexture.active = target;
		readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
		readback.Apply();
		byte[] bytes = readback.GetRawTextureData();
		return (byte[]) bytes.Clone();
	}

	private static void Measure(FeedbackRegion region, int id, byte[] footprint, byte[] visible, int width, int height, bool provenOffscreen)
	{
		int count = 0, seen = 0, x0 = width, y0 = height, x1 = 0, y1 = 0;
		for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
		{
			int offset = (y * width + x) * 3;
			if (footprint[offset] != id || footprint[offset + 1] != 0 || footprint[offset + 2] != 0) continue;
			count++;
			if (visible[offset] == id && visible[offset + 1] == 0 && visible[offset + 2] == 0) seen++;
			x0 = Math.Min(x0, x); x1 = Math.Max(x1, x + 1);
			y0 = Math.Min(y0, height - y - 1); y1 = Math.Max(y1, height - y);
		}
		if (seen == 0 && count == 0 && provenOffscreen)
		{
			region.visibility = "invisible";
			return;
		}
		if (seen == 0 || seen != count)
		{
			region.visibility = "unavailable";
			region.failure = "Feedback visibility could not be established";
			return;
		}
		region.visibility = "visible";
		float scale = Math.Max(width / 512f, height / 384f);
		int padding = (int) Math.Ceiling(4 * scale);
		region.center = new float[] { (x0 + x1) / 2f, (y0 + y1) / 2f };
		// The chosen status radius is independent of the neutral mesh or glow extent.
		region.radius = 28 * scale;
		region.bounds = new int[] { Math.Max(0, x0 - padding), Math.Max(0, y0 - padding), Math.Min(width, x1 + padding), Math.Min(height, y1 + padding) };
	}

	public void Dispose()
	{
		if (feedbackDraws != null)
		{
			if (camera) camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, feedbackDraws);
			feedbackDraws.Release();
			feedbackDraws = null;
		}
		if (camera) UnityEngine.Object.Destroy(camera.gameObject);
		if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
		if (readback) UnityEngine.Object.Destroy(readback);
		if (material) UnityEngine.Object.Destroy(material);
	}
}
