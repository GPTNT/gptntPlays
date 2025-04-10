using System;
using System.IO;
using UnityEngine;

public class GptntDebug : MonoBehaviour
{
	private static string path = Path.Combine(Application.persistentDataPath, "gptntlogs.log");

	public static void Log(string message)
	{
		StreamWriter writer = new StreamWriter(path, true);
		writer.WriteLine(message);
		writer.Close();
	}

	public static void LogChildrenRecursive(GameObject obj, int depth = 0)
	{
		Log("Parent: " + obj.name);
		LogAllComponents(obj);
		foreach (Transform child in obj.transform)
		{
			string indent = new string('-', depth);
			Log($"{indent}{child.gameObject.name}");
			LogAllComponents(child.gameObject);
			LogChildrenRecursive(child.gameObject, depth + 1);
		}
	}

	public static void LogAllComponents(GameObject obj)
	{
		Component[] components = obj.GetComponents<Component>();
		Log($"Components on '{obj.name}':");

		foreach (Component comp in components)
		{
			if (comp != null)
				Log("+ " + comp.GetType().Name);
			else
				Log("+ [Missing Component]");
		}
	}

}

