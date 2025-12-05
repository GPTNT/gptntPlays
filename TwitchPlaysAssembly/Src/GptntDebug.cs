using UnityEngine;
using log4net;

public class GptntDebug : MonoBehaviour
{
	private static string path = "gptntlogs.log";
	private static ILog log = LogManager.GetLogger("Helper");

	public static string FormatMessage(string message, string traceId = null, string spanId = null)
	{
		string finalMessage = "";
		if (traceId != null)
			finalMessage += $"trace_id={traceId} ";
		if (spanId != null)
			finalMessage += $"span_id={spanId} ";
		return finalMessage + message;
	}

	public static void LogChildrenRecursive(GameObject obj,bool withComponents, int depth = 0)
	{
		foreach (Transform child in obj.transform)
		{
			string indent = new string('-', depth);
			log.Debug(FormatMessage($"{indent}{child.gameObject.name}"));
			if (withComponents) {LogAllComponents(child.gameObject);}
			LogChildrenRecursive(child.gameObject, withComponents, depth + 1);
		}
	}

	public static void LogAllComponents(GameObject obj)
	{
		Component[] components = obj.GetComponents<Component>();
		log.Debug(FormatMessage($"Components on '{obj.name}':"));

		foreach (Component comp in components)
		{
			if (comp != null)
				log.Debug(FormatMessage("+ " + comp.GetType().Name));
			else
				log.Debug(FormatMessage("+ [Missing Component]"));
		}
	}

}

