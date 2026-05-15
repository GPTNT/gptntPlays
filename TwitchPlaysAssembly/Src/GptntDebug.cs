using UnityEngine;
using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;

public class GptntDebug : MonoBehaviour
{
	private static string path = "gptntlogs.log";
	private static ILog log = LogManager.GetLogger("Helper");

	public static void AddSessionId(string sessionId)
	{
		// Get the hierarchy
		var hierarchy = (Hierarchy) LogManager.GetRepository();

		// Loop through all appenders (Console, File, etc.)
		foreach (var appender in hierarchy.GetAppenders())
		{
			// Check if the appender has a Layout property (most do)
			if (appender is AppenderSkeleton skeleton)
			{
				// Create the pattern in code
				var patternLayout = new PatternLayout();
				patternLayout.ConversionPattern = "%5level %utcdate{ISO8601} [%logger{1}] [" + sessionId + "] %message%newline";
				patternLayout.ActivateOptions(); // Important: compiles the pattern

				// Apply it
				skeleton.Layout = patternLayout;
			}
		}
	}

	public static void ResetLogFormat()
	{
		// Get the hierarchy
		var hierarchy = (Hierarchy) LogManager.GetRepository();

		// Loop through all appenders (Console, File, etc.)
		foreach (var appender in hierarchy.GetAppenders())
		{
			// Check if the appender has a Layout property (most do)
			if (appender is AppenderSkeleton skeleton)
			{
				// Create the pattern in code
				var patternLayout = new PatternLayout();
				patternLayout.ConversionPattern = "%5level %utcdate{ISO8601} [%logger{1}] %message%newline";
				patternLayout.ActivateOptions(); // Important: compiles the pattern

				// Apply it
				skeleton.Layout = patternLayout;
			}
		}
	}

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

