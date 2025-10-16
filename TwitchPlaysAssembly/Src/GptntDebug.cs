using UnityEngine;
using log4net;

public class GptntDebug : MonoBehaviour
{
	private static string path = "gptntlogs.log";
	private static ILog log = LogManager.GetLogger("Helper");

	public static void LogChildrenRecursive(GameObject obj,bool withComponents, int depth = 0)
	{
		foreach (Transform child in obj.transform)
		{
			string indent = new string('-', depth);
			log.Debug($"{indent}{child.gameObject.name}");
			if (withComponents) {LogAllComponents(child.gameObject);}
			LogChildrenRecursive(child.gameObject, withComponents, depth + 1);
		}
	}

	public static void LogAllComponents(GameObject obj)
	{
		Component[] components = obj.GetComponents<Component>();
		log.Debug($"Components on '{obj.name}':");

		foreach (Component comp in components)
		{
			if (comp != null)
				log.Debug("+ " + comp.GetType().Name);
			else
				log.Debug("+ [Missing Component]");
		}
	}

}

